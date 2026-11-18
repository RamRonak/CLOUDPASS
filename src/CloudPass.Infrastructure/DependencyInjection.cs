using CloudPass.Application.Interfaces;
using CloudPass.Application.Options;
using CloudPass.Application.Services;
using CloudPass.Domain.Entities;
using CloudPass.Infrastructure.Data;
using CloudPass.Infrastructure.HealthChecks;
using CloudPass.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CloudPass.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCloudPassInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SecurityOptions>(opt =>
        {
            opt.TicketSigningSecret = configuration["Security:TicketSigningSecret"] ?? string.Empty;
            if (bool.TryParse(configuration["Security:EnforceSingleUseTicketValidation"], out var singleUse))
                opt.EnforceSingleUseTicketValidation = singleUse;
        });

        services.Configure<PaymentGatewayOptions>(opt =>
        {
            opt.Provider = configuration["PaymentGateway:Provider"] ?? "Razorpay-TestMode";
            opt.KeyId = configuration["PaymentGateway:KeyId"] ?? string.Empty;
            opt.KeySecret = configuration["PaymentGateway:KeySecret"] ?? string.Empty;
            opt.WebhookSecret = configuration["PaymentGateway:WebhookSecret"] ?? string.Empty;
            if (bool.TryParse(configuration["PaymentGateway:TestMode"], out var testMode))
                opt.TestMode = testMode;
        });

        services.Configure<StorageOptions>(opt =>
        {
            opt.Provider = configuration["Storage:Provider"] ?? "Local";
            opt.AzureBlobConnectionString = configuration["Storage:AzureBlobConnectionString"];
            opt.ContainerName = configuration["Storage:ContainerName"] ?? "cloudpass-tickets";
            opt.LocalRootPath = configuration["Storage:LocalRootPath"] ?? "App_Data/CloudStorage";
        });

        var dbProvider = configuration["Database:Provider"] ?? "Sqlite";
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.Equals(dbProvider, "SqlServer", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(connectionString) &&
            !connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            services.AddDbContext<CloudPassDbContext>(options =>
                options.UseSqlServer(connectionString, sql =>
                {
                    sql.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(15),
                        errorNumbersToAdd: null);
                    sql.CommandTimeout(30);
                }));
        }
        else
        {
            var sqliteConn = string.IsNullOrWhiteSpace(connectionString)
                ? "Data Source=App_Data/cloudpass.db"
                : connectionString;

            if (sqliteConn.Contains("App_Data", StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "App_Data"));
            }

            services.AddDbContext<CloudPassDbContext>(options =>
                options.UseSqlite(sqliteConn));
        }

        services.AddMemoryCache();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<IFareCalculationService, FareCalculationService>();
        services.AddSingleton<ITicketSecurityService, TicketSecurityService>();
        services.AddSingleton<IQrCodeService, QrCodeService>();
        services.AddSingleton<IPdfTicketService, PdfTicketService>();
        services.AddSingleton<ITicketStorageService, CloudTicketStorageService>();
        services.AddSingleton<IBackgroundTaskQueue>(_ => new BackgroundTaskQueue(500));

        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<IBusPassService, BusPassService>();

        services.AddHostedService<QueuedHostedService>();
        services.AddHostedService<PassExpirationMonitorService>();

        services.AddHealthChecks()
            .AddDbContextCheck<CloudPassDbContext>("database", tags: new[] { "ready" })
            .AddCheck<CloudStorageHealthCheck>("cloud_storage", tags: new[] { "ready" })
            .AddCheck<BackgroundQueueHealthCheck>("background_queue", tags: new[] { "live", "ready" });

        return services;
    }
}
