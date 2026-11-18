using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Xunit;
using Xunit.Abstractions;

namespace CloudPass.Tests;

public class PerformanceAndReliabilityTests : IClassFixture<CloudPassWebAppFactory>
{
    private readonly CloudPassWebAppFactory _factory;
    private readonly ITestOutputHelper _output;

    public PerformanceAndReliabilityTests(CloudPassWebAppFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Fact]
    public async Task Performance_ConcurrentReadWorkload_MeasuresActualLatencyAndThroughput()
    {
        var client = _factory.CreateClient();
        var warmup = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, warmup.StatusCode);

        const int totalRequests = 120;
        const int concurrency = 20;
        var endpoints = new[]
        {
            "/",
            "/Bus/Search?Source=Mumbai&Destination=Pune",
            "/Bus/Search?HasAC=true&IsSleeper=true",
            "/health/ready"
        };

        var latencies = new ConcurrentBag<double>();
        var failures = 0;
        var semaphore = new SemaphoreSlim(concurrency);
        var swTotal = Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, totalRequests).Select(async i =>
        {
            await semaphore.WaitAsync();
            try
            {
                var sw = Stopwatch.StartNew();
                var res = await client.GetAsync(endpoints[i % endpoints.Length]);
                sw.Stop();
                latencies.Add(sw.Elapsed.TotalMilliseconds);
                if (!res.IsSuccessStatusCode) Interlocked.Increment(ref failures);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        swTotal.Stop();

        var sorted = latencies.OrderBy(x => x).ToArray();
        var avg = sorted.Average();
        var p50 = sorted[(int)(sorted.Length * 0.50)];
        var p95 = sorted[(int)(sorted.Length * 0.95)];
        var p99 = sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.99))];
        var rps = totalRequests / swTotal.Elapsed.TotalSeconds;

        _output.WriteLine($"[MEASURED LOAD BENCHMARK] Requests={totalRequests}, Concurrency={concurrency}, Failures={failures}");
        _output.WriteLine($"[MEASURED LOAD BENCHMARK] TotalTime={swTotal.Elapsed.TotalMilliseconds:F1}ms, Throughput={rps:F1} req/s");
        _output.WriteLine($"[MEASURED LOAD BENCHMARK] Latency(ms): Avg={avg:F2}, P50={p50:F2}, P95={p95:F2}, P99={p99:F2}");

        Assert.Equal(0, failures);
        Assert.True(p95 < 2000, $"P95 latency too high: {p95:F2}ms");
    }

    [Fact]
    public async Task Reliability_InvalidResourceAndErrorHandling_RespondGracefully()
    {
        var client = _factory.CreateClient();

        var missingSchedule = await client.GetAsync("/Bus/Details/999999");
        Assert.Equal(HttpStatusCode.NotFound, missingSchedule.StatusCode);

        var homeRes = await client.GetAsync("/");
        Assert.True(homeRes.Headers.Contains("X-Content-Type-Options"));
        Assert.True(homeRes.Headers.Contains("X-Frame-Options"));
        Assert.True(homeRes.Headers.Contains("X-CloudPass-Node"));
    }
}
