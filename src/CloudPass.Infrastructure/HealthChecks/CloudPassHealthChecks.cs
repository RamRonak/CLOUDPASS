using CloudPass.Application.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CloudPass.Infrastructure.HealthChecks;

public class CloudStorageHealthCheck : IHealthCheck
{
    private readonly ITicketStorageService _storage;

    public CloudStorageHealthCheck(ITicketStorageService storage) => _storage = storage;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var ok = await _storage.HealthCheckAsync(cancellationToken);
        return ok
            ? HealthCheckResult.Healthy($"Cloud storage ({_storage.ProviderName}) is accessible.")
            : HealthCheckResult.Degraded($"Cloud storage ({_storage.ProviderName}) is degraded.");
    }
}

public class BackgroundQueueHealthCheck : IHealthCheck
{
    private readonly IBackgroundTaskQueue _queue;

    public BackgroundQueueHealthCheck(IBackgroundTaskQueue queue) => _queue = queue;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var depth = _queue.QueuedCount;
        var data = new Dictionary<string, object> { ["queuedItems"] = depth };
        if (depth > 400)
            return Task.FromResult(HealthCheckResult.Degraded("Background queue approaching capacity.", data: data));
        return Task.FromResult(HealthCheckResult.Healthy("Background async processing queue healthy.", data: data));
    }
}
