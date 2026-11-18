using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CloudPass.Infrastructure.Data;

/// <summary>
/// Design-time factory for generating SQL Server EF Core migrations cleanly.
/// </summary>
public class CloudPassDbContextFactory : IDesignTimeDbContextFactory<CloudPassDbContext>
{
    public CloudPassDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CloudPassDbContext>();
        var connectionString = Environment.GetEnvironmentVariable("CLOUDPASS_SQL_CONNECTION")
            ?? "Server=(localdb)\\mssqllocaldb;Database=CloudPassDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

        optionsBuilder.UseSqlServer(connectionString, sql =>
        {
            sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(15), null);
        });

        return new CloudPassDbContext(optionsBuilder.Options);
    }
}
