param(
    [Parameter(Mandatory=$true)][string]$ResourceGroup,
    [string]$Location = "centralindia",
    [string]$SqlAdminLogin = "cloudpassadmin",
    [string]$EnvironmentName = "prod"
)

$ErrorActionPreference = "Stop"

Write-Host "1. Verifying Azure CLI login..." -ForegroundColor Cyan
az account show | Out-Null

Write-Host "2. Creating Resource Group '$ResourceGroup' in '$Location'..." -ForegroundColor Cyan
az group create --name $ResourceGroup --location $Location | Out-Null

$sqlPassword = Read-Host "Enter strong Azure SQL Admin Password" -AsSecureString
$sqlPasswordPlain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($sqlPassword))

$ticketSecret = if ($env:CLOUDPASS_TICKET_SIGNING_SECRET) { $env:CLOUDPASS_TICKET_SIGNING_SECRET } else { [Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 })) }
$paySecret = if ($env:CLOUDPASS_PAYMENT_KEY_SECRET) { $env:CLOUDPASS_PAYMENT_KEY_SECRET } else { [Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 })) }
$whSecret = if ($env:CLOUDPASS_PAYMENT_WEBHOOK_SECRET) { $env:CLOUDPASS_PAYMENT_WEBHOOK_SECRET } else { [Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 })) }

Write-Host "3. Deploying Azure Bicep Infrastructure..." -ForegroundColor Cyan
$deployJson = az deployment group create `
    --resource-group $ResourceGroup `
    --template-file "$PSScriptRoot\main.bicep" `
    --parameters environmentName=$EnvironmentName `
                 sqlAdminLogin=$SqlAdminLogin `
                 sqlAdminPassword=$sqlPasswordPlain `
                 ticketSigningSecret=$ticketSecret `
                 paymentKeySecret=$paySecret `
                 paymentWebhookSecret=$whSecret `
    --output json | ConvertFrom-Json

$webAppName = $deployJson.properties.outputs.webAppName.value
$webAppUrl = $deployJson.properties.outputs.webAppUrl.value

Write-Host "4. Building & Publishing CloudPass.Web..." -ForegroundColor Cyan
$repoRoot = Resolve-Path "$PSScriptRoot\..\.."
$publishDir = Join-Path $repoRoot "artifacts\publish"
$zipPath = Join-Path $repoRoot "artifacts\cloudpass.zip"

dotnet publish (Join-Path $repoRoot "src\CloudPass.Web\CloudPass.Web.csproj") -c Release -o $publishDir
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath

Write-Host "5. Deploying package to Azure App Service '$webAppName'..." -ForegroundColor Cyan
az webapp deploy --resource-group $ResourceGroup --name $webAppName --src-path $zipPath --type zip

Write-Host "6. Verifying Cloud Health Endpoint..." -ForegroundColor Cyan
Start-Sleep -Seconds 10
Invoke-RestMethod -Uri "$webAppUrl/health"

Write-Host "`nDeployment Complete! Live URL: $webAppUrl" -ForegroundColor Green
