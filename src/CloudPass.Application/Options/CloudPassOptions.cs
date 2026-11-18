namespace CloudPass.Application.Options;

public class SecurityOptions
{
    public const string SectionName = "Security";
    public string TicketSigningSecret { get; set; } = string.Empty;
    public bool EnforceSingleUseTicketValidation { get; set; } = true;
}

public class PaymentGatewayOptions
{
    public const string SectionName = "PaymentGateway";
    public string Provider { get; set; } = "Razorpay-TestMode";
    public string KeyId { get; set; } = string.Empty;
    public string KeySecret { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public bool TestMode { get; set; } = true;
}

public class StorageOptions
{
    public const string SectionName = "Storage";
    public string Provider { get; set; } = "Local"; // "AzureBlob" or "Local"
    public string? AzureBlobConnectionString { get; set; }
    public string ContainerName { get; set; } = "cloudpass-tickets";
    public string LocalRootPath { get; set; } = "App_Data/CloudStorage";
}
