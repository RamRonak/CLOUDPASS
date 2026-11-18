using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CloudPass.Tests;

public class CloudPassWebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath;

    public CloudPassWebAppFactory()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"cloudpass_test_{Guid.NewGuid():N}.db");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={_dbPath}");
        builder.UseSetting("Security:TicketSigningSecret", "test-suite-hmac-secret-key-for-cloudpass-verification-2026");
        builder.UseSetting("Security:EnforceSingleUseTicketValidation", "true");
        builder.UseSetting("PaymentGateway:Provider", "Razorpay-TestMode");
        builder.UseSetting("PaymentGateway:KeyId", "rzp_test_cloudpass_suite");
        builder.UseSetting("PaymentGateway:KeySecret", "test-suite-razorpay-key-secret-2026");
        builder.UseSetting("PaymentGateway:WebhookSecret", "test-suite-razorpay-webhook-secret-2026");
        builder.UseSetting("PaymentGateway:TestMode", "true");
        builder.UseSetting("Storage:Provider", "Local");
        builder.UseSetting("Storage:LocalRootPath", Path.Combine(Path.GetTempPath(), $"cloudpass_storage_{Guid.NewGuid():N}"));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch
        {
            // Ignore temp cleanup race on Windows
        }
    }
}
