// =============================================================================
// CLOUDPASS — Microsoft Azure Production Infrastructure as Code (Bicep)
// Provisions:
//   1. Azure App Service Plan (PremiumV3 P1v3 Linux, Multi-Instance Ready)
//   2. Azure App Service (HTTPS Only, AlwaysOn, Health Check /health/ready, MSI)
//   3. Azure Monitor Autoscale Settings (Min 2, Max 10; CPU & HTTP Queue rules)
//   4. Azure SQL Server + Azure SQL Database (TLS 1.2+, Automated Backups)
//   5. Azure Storage Account (GRS) + Private Blob Container + Soft Delete
//   6. Log Analytics Workspace + Azure Application Insights
//   7. Azure Key Vault (RBAC enabled for secrets)
// =============================================================================

@description('Azure region for all CloudPass resources')
param location string = resourceGroup().location

@description('Short unique prefix for globally unique Azure names')
param projectPrefix string = 'cloudpass'

@description('Environment name (dev, staging, prod)')
@allowed(['dev', 'staging', 'prod'])
param environmentName string = 'prod'

@description('Azure SQL Administrator Login')
param sqlAdminLogin string

@secure()
@description('Azure SQL Administrator Password (pass via Azure CLI / Key Vault, never hardcode)')
param sqlAdminPassword string

@secure()
@description('Cryptographic HMAC-SHA256 signing secret for CloudPass QR tokens')
param ticketSigningSecret string

@secure()
@description('Razorpay Test-Mode API Key Secret')
param paymentKeySecret string

@secure()
@description('Razorpay Signed Webhook Verification Secret')
param paymentWebhookSecret string

var uniqueSuffix = take(uniqueString(resourceGroup().id), 6)
var appServicePlanName = '${projectPrefix}-asp-${environmentName}-${uniqueSuffix}'
var webAppName = '${projectPrefix}-web-${environmentName}-${uniqueSuffix}'
var sqlServerName = '${projectPrefix}-sql-${environmentName}-${uniqueSuffix}'
var sqlDatabaseName = 'CloudPassDb'
var storageAccountName = toLower('${projectPrefix}st${uniqueSuffix}')
var logAnalyticsName = '${projectPrefix}-law-${environmentName}-${uniqueSuffix}'
var appInsightsName = '${projectPrefix}-ai-${environmentName}-${uniqueSuffix}'
var keyVaultName = '${projectPrefix}-kv-${uniqueSuffix}'
var autoscaleName = '${projectPrefix}-autoscale-${environmentName}'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    RetentionInDays: 30
  }
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  sku: { name: 'Standard_GRS' }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    deleteRetentionPolicy: { enabled: true, days: 14 }
    containerDeleteRetentionPolicy: { enabled: true, days: 14 }
  }
}

resource ticketsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'cloudpass-tickets'
  properties: { publicAccess: 'None' }
}

resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-05-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: { name: 'S1', tier: 'Standard', capacity: 20 }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 268435456000
    requestedBackupStorageRedundancy: 'Geo'
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 14
  }
}

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  kind: 'linux'
  sku: { name: 'P1v3', tier: 'PremiumV3', capacity: 2 }
  properties: { reserved: true }
}

var sqlConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDatabaseName};Persist Security Info=False;User ID=${sqlAdminLogin};Password=${sqlAdminPassword};MultipleActiveResultSets=True;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      healthCheckPath: '/health/ready'
      connectionStrings: [
        {
          name: 'DefaultConnection'
          connectionString: sqlConnectionString
          type: 'SQLAzure'
        }
      ]
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        { name: 'Database__Provider', value: 'SqlServer' }
        { name: 'Storage__Provider', value: 'AzureBlob' }
        { name: 'Storage__ContainerName', value: 'cloudpass-tickets' }
        { name: 'Storage__AzureBlobConnectionString', value: storageConnectionString }
        { name: 'Security__TicketSigningSecret', value: ticketSigningSecret }
        { name: 'Security__EnforceSingleUseTicketValidation', value: 'true' }
        { name: 'PaymentGateway__Provider', value: 'Razorpay-TestMode' }
        { name: 'PaymentGateway__KeySecret', value: paymentKeySecret }
        { name: 'PaymentGateway__WebhookSecret', value: paymentWebhookSecret }
        { name: 'PaymentGateway__TestMode', value: 'true' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
      ]
    }
  }
}

resource autoscaleSettings 'Microsoft.Insights/autoscalesettings@2022-10-01' = {
  name: autoscaleName
  location: location
  properties: {
    enabled: true
    targetResourceUri: appServicePlan.id
    profiles: [
      {
        name: 'CloudPassDynamicTrafficProfile'
        capacity: { minimum: '2', default: '2', maximum: '10' }
        rules: [
          {
            metricTrigger: {
              metricName: 'CpuPercentage'
              metricResourceUri: appServicePlan.id
              timeGrain: 'PT1M'
              statistic: 'Average'
              timeWindow: 'PT5M'
              timeAggregation: 'Average'
              operator: 'GreaterThan'
              threshold: 70
            }
            scaleAction: { direction: 'Increase', type: 'ChangeCount', value: '2', cooldown: 'PT5M' }
          }
          {
            metricTrigger: {
              metricName: 'HttpQueueLength'
              metricResourceUri: appServicePlan.id
              timeGrain: 'PT1M'
              statistic: 'Average'
              timeWindow: 'PT5M'
              timeAggregation: 'Average'
              operator: 'GreaterThan'
              threshold: 100
            }
            scaleAction: { direction: 'Increase', type: 'ChangeCount', value: '1', cooldown: 'PT5M' }
          }
          {
            metricTrigger: {
              metricName: 'CpuPercentage'
              metricResourceUri: appServicePlan.id
              timeGrain: 'PT1M'
              statistic: 'Average'
              timeWindow: 'PT10M'
              timeAggregation: 'Average'
              operator: 'LessThan'
              threshold: 30
            }
            scaleAction: { direction: 'Decrease', type: 'ChangeCount', value: '1', cooldown: 'PT10M' }
          }
        ]
      }
    ]
  }
}

output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output webAppName string = webApp.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output storageAccountName string = storageAccount.name
output appInsightsName string = appInsights.name
output autoscalePolicyName string = autoscaleSettings.name
