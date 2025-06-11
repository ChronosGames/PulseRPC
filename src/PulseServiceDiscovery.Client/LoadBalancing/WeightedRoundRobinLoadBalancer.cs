using Microsoft.Extensions.Logging;
using PulseServiceDiscovery.Abstractions;
using PulseServiceDiscovery.Abstractions.Enums;
using PulseServiceDiscovery.Abstractions.Models;
using System.Collections.Concurrent;

namespace PulseServiceDiscovery.Client.LoadBalancing;

/// <summary>
/// 加权轮询负载均衡器
/// </summary>
public class WeightedRoundRobinLoadBalancer : ILoadBalancer
{
    private readonly ILogger<WeightedRoundRobinLoadBalancer> _logger;
    private readonly ConcurrentDictionary<string, WeightedEndpointState> _endpointStates = new();
    private readonly ConcurrentDictionary<string, EndpointStatistics> _statistics = new();

    public LoadBalancingStrategy Strategy => LoadBalancingStrategy.WeightedRoundRobin;

    public WeightedRoundRobinLoadBalancer(ILogger<WeightedRoundRobinLoadBalancer> logger)
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
            _logger.LogDebug("Only one endpoint available: {Endpoint}", singleEndpoint.Address);
            return Task.FromResult<ServiceEndpoint?>(singleEndpoint);
        }

        var selectedEndpoint = SelectUsingWeightedRoundRobin(endpoints);

        _logger.LogDebug("Selected weighted endpoint: {Endpoint} (Weight: {Weight})",
            selectedEndpoint?.Address, selectedEndpoint?.Weight);

        return Task.FromResult(selectedEndpoint);
    }

    private ServiceEndpoint? SelectUsingWeightedRoundRobin(IReadOnlyList<ServiceEndpoint> endpoints)
    {
        var totalWeight = endpoints.Sum(e => e.Weight);
        if (totalWeight <= 0)
        {
            // 如果所有权重都是0或负数，回退到简单轮询
            _logger.LogWarning("All endpoints have zero or negative weights, falling back to round-robin");
            return SelectUsingSimpleRoundRobin(endpoints);
        }

        // 更新所有端点的当前权重
        foreach (var endpoint in endpoints)
        {
            var state = _endpointStates.AddOrUpdate(endpoint.Id,
                new WeightedEndpointState(endpoint.Weight),
                (_, existing) => existing.UpdateWeight(endpoint.Weight));

            // 增加当前权重
            state.AddCurrentWeight();
        }

        // 找到当前权重最大的端点
        ServiceEndpoint? selectedEndpoint = null;
        WeightedEndpointState? selectedState = null;
        int maxCurrentWeight = int.MinValue;

        foreach (var endpoint in endpoints)
        {
            if (_endpointStates.TryGetValue(endpoint.Id, out var state))
            {
                if (state.CurrentWeight > maxCurrentWeight)
                {
                    maxCurrentWeight = state.CurrentWeight;
                    selectedEndpoint = endpoint;
                    selectedState = state;
                }
            }
        }

        // 减少选中端点的当前权重
        if (selectedState != null)
        {
            selectedState.SubtractTotalWeight(totalWeight);
        }

        return selectedEndpoint;
    }

    private ServiceEndpoint? SelectUsingSimpleRoundRobin(IReadOnlyList<ServiceEndpoint> endpoints)
    {
        // 简单轮询作为后备方案
        var key = "simple_round_robin";
        var state = _endpointStates.AddOrUpdate(key,
            new WeightedEndpointState(1),
            (_, existing) => existing);

        var index = (int)(state.GetAndIncrementCounter() % endpoints.Count);
        return endpoints[index];
    }

    public void ReportResult(ServiceEndpoint endpoint, LoadBalancingResult result, TimeSpan responseTime)
    {
        if (endpoint == null) return;

        var stats = _statistics.AddOrUpdate(endpoint.Id,
            new EndpointStatistics(),
            (_, existing) => existing.UpdateWith(result, responseTime));

        _logger.LogDebug("Updated statistics for endpoint {Endpoint}: {Stats}",
            endpoint.Address, stats);
    }

    public void Reset()
    {
        _endpointStates.Clear();
        _statistics.Clear();
        _logger.LogDebug("Reset WeightedRoundRobin load balancer state");
    }

    public Dictionary<string, object> GetStatistics()
    {
        return new Dictionary<string, object>
        {
            ["Strategy"] = Strategy.ToString(),
            ["EndpointStatesCount"] = _endpointStates.Count,
            ["EndpointStatistics"] = _statistics.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToDictionary()),
            ["WeightedStates"] = _endpointStates.ToDictionary(
                kvp => kvp.Key,
                kvp => new Dictionary<string, object>
                {
                    ["ConfiguredWeight"] = kvp.Value.ConfiguredWeight,
                    ["CurrentWeight"] = kvp.Value.CurrentWeight
                })
        };
    }
}

/// <summary>
/// 加权端点状态
/// </summary>
internal class WeightedEndpointState
{
    private int _configuredWeight;
    private int _currentWeight;
    private long _counter;
    private readonly object _lock = new();

    public int ConfiguredWeight
    {
        get { lock (_lock) return _configuredWeight; }
    }

    public int CurrentWeight
    {
        get { lock (_lock) return _currentWeight; }
    }

    public WeightedEndpointState(int weight)
    {
        _configuredWeight = Math.Max(weight, 1); // 确保权重至少为1
        _currentWeight = 0;
        _counter = 0;
    }

    public WeightedEndpointState UpdateWeight(int newWeight)
    {
        lock (_lock)
        {
            _configuredWeight = Math.Max(newWeight, 1);
        }
        return this;
    }

    public void AddCurrentWeight()
    {
        lock (_lock)
        {
            _currentWeight += _configuredWeight;
        }
    }

    public void SubtractTotalWeight(int totalWeight)
    {
        lock (_lock)
        {
            _currentWeight -= totalWeight;
        }
    }

    public long GetAndIncrementCounter()
    {
        lock (_lock)
        {
            return _counter++;
        }
    }
}
