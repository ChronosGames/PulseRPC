using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using PulseRPC.LoadBalancing;
using PulseRPC.ServiceDiscovery;

namespace PulseServiceDiscovery.Client.LoadBalancing;

/// <summary>
/// 轮询负载均衡器
/// </summary>
public class RoundRobinLoadBalancer : ILoadBalancer
{
    private readonly ILogger<RoundRobinLoadBalancer> _logger;
    private readonly ConcurrentDictionary<string, long> _counters = new();
    private readonly ConcurrentDictionary<string, EndpointStatistics> _statistics = new();

    public LoadBalancingStrategy Strategy => LoadBalancingStrategy.RoundRobin;

    public RoundRobinLoadBalancer(ILogger<RoundRobinLoadBalancer> logger)
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

        // 获取或创建计数器
        var key = GetCounterKey(endpoints);
        var counter = _counters.AddOrUpdate(key, 0, (_, current) => current + 1);

        // 使用模运算选择端点
        var index = (int)(counter % endpoints.Count);
        var selectedEndpoint = endpoints[index];

        _logger.LogDebug("Selected endpoint {Index}/{Count}: {Endpoint}",
            index + 1, endpoints.Count, selectedEndpoint);

        return Task.FromResult<ServiceEndpoint?>(selectedEndpoint);
    }

    public void ReportResult(ServiceEndpoint endpoint, LoadBalancingResult result, TimeSpan responseTime)
    {
        if (endpoint == null) return;

        var stats = _statistics.AddOrUpdate(endpoint.Id,
            new EndpointStatistics(),
            (_, existing) => existing.UpdateWith(result, responseTime));

        _logger.LogDebug("Updated statistics for endpoint {Endpoint}: {Stats}",
            endpoint, stats);
    }

    public void Reset()
    {
        _counters.Clear();
        _statistics.Clear();
        _logger.LogDebug("Reset RoundRobin load balancer state");
    }

    public Dictionary<string, object> GetStatistics()
    {
        return new Dictionary<string, object>
        {
            ["Strategy"] = Strategy.ToString(),
            ["ActiveCounters"] = _counters.Count,
            ["EndpointStatistics"] = _statistics.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToDictionary())
        };
    }

    private static string GetCounterKey(IReadOnlyList<ServiceEndpoint> endpoints)
    {
        // 使用端点ID的组合作为键，确保相同的端点集合使用相同的计数器
        var sortedIds = endpoints.Select(e => e.Id).OrderBy(id => id);
        return string.Join(",", sortedIds);
    }
}
