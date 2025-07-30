using Microsoft.Extensions.Logging;
using PulseRPC.ServiceDiscovery;
using System.Collections.Concurrent;
using PulseRPC.Infrastructure;
using PulseRPC.Routing;

namespace PulseRPC.LoadBalancing;

/// <summary>
/// 随机负载均衡器
/// </summary>
public class RandomLoadBalancer : ILoadBalancer
{
    private readonly ILogger<RandomLoadBalancer> _logger;
    private readonly Random _random = new();
    private readonly ConcurrentDictionary<string, EndpointStatistics> _statistics = new();
    private readonly object _randomLock = new();

    public LoadBalancingStrategy Strategy => LoadBalancingStrategy.Random;

    public RandomLoadBalancer(ILogger<RandomLoadBalancer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<ServiceEndpoint?> SelectAsync(
        IReadOnlyList<ServiceEndpoint> endpoints,
        LoadBalancingContext context,
        CancellationToken cancellationToken = default)
    {
        if (endpoints == null || endpoints.Count == 0)
        {
            _logger.LogWarning("No endpoints available for load balancing");
            return Task.FromResult<ServiceEndpoint?>(null);
        }

        if (endpoints.Count == 1)
        {
            var singleEndpoint = endpoints[0];
            _logger.LogDebug("Only one endpoint available: {Endpoint}", singleEndpoint);
            return Task.FromResult<ServiceEndpoint?>(singleEndpoint);
        }

        // 线程安全的随机选择
        int index;
        lock (_randomLock)
        {
            index = _random.Next(endpoints.Count);
        }

        var selectedEndpoint = endpoints[index];

        _logger.LogDebug("Randomly selected endpoint {Index}/{Count}: {Endpoint}",
            index + 1, endpoints.Count, selectedEndpoint);

        return Task.FromResult<ServiceEndpoint?>(selectedEndpoint);
    }

    public void ReportResult(ServiceEndpoint endpoint, LoadBalancingResult result, TimeSpan responseTime)
    {
        if (endpoint == null) return;

        var stats = _statistics.AddOrUpdate(endpoint.ServiceId,
            new EndpointStatistics(),
            (_, existing) => existing.UpdateWith(result, responseTime));

        _logger.LogDebug("Updated statistics for endpoint {Endpoint}: {Stats}", endpoint, stats);
    }

    public void Reset()
    {
        _statistics.Clear();
        _logger.LogDebug("Reset Random load balancer state");
    }

    public Dictionary<string, object> GetStatistics()
    {
        return new Dictionary<string, object>
        {
            ["Strategy"] = Strategy.ToString(),
            ["EndpointCount"] = _statistics.Count,
            ["EndpointStatistics"] = _statistics.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToDictionary())
        };
    }
}
