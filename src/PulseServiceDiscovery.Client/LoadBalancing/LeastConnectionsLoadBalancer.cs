using Microsoft.Extensions.Logging;
using PulseServiceDiscovery.Abstractions;
using PulseServiceDiscovery.Abstractions.Enums;
using PulseServiceDiscovery.Abstractions.Models;
using System.Collections.Concurrent;

namespace PulseServiceDiscovery.Client.LoadBalancing;

/// <summary>
/// 最少连接负载均衡器
/// </summary>
public class LeastConnectionsLoadBalancer : ILoadBalancer
{
    private readonly ILogger<LeastConnectionsLoadBalancer> _logger;
    private readonly ConcurrentDictionary<string, ConnectionState> _connectionStates = new();
    private readonly ConcurrentDictionary<string, EndpointStatistics> _statistics = new();

    public LoadBalancingStrategy Strategy => LoadBalancingStrategy.LeastConnections;

    public LeastConnectionsLoadBalancer(ILogger<LeastConnectionsLoadBalancer> logger)
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

        var selectedEndpoint = SelectEndpointWithLeastConnections(endpoints);

        _logger.LogDebug("Selected endpoint with least connections: {Endpoint} (Connections: {Connections})",
            selectedEndpoint?.Address,
            selectedEndpoint != null ? GetConnectionCount(selectedEndpoint.Id) : 0);

        return Task.FromResult(selectedEndpoint);
    }

    private ServiceEndpoint? SelectEndpointWithLeastConnections(IReadOnlyList<ServiceEndpoint> endpoints)
    {
        ServiceEndpoint? selectedEndpoint = null;
        int minConnections = int.MaxValue;
        double bestScore = double.MinValue;

        foreach (var endpoint in endpoints)
        {
            var state = _connectionStates.GetOrAdd(endpoint.Id, _ => new ConnectionState());
            var connections = state.ActiveConnections;

            // 计算加权分数：考虑连接数和权重
            var score = CalculateScore(connections, endpoint.Weight);

            // 选择分数最高的端点（连接数最少，权重最高）
            if (score > bestScore || (score == bestScore && connections < minConnections))
            {
                bestScore = score;
                minConnections = connections;
                selectedEndpoint = endpoint;
            }
        }

        // 增加选中端点的连接计数
        if (selectedEndpoint != null)
        {
            var state = _connectionStates[selectedEndpoint.Id];
            state.IncrementConnections();
        }

        return selectedEndpoint;
    }

    private static double CalculateScore(int connections, int weight)
    {
        // 权重越高，连接数越少，分数越高
        // 使用倒数来确保连接数越少分数越高
        var connectionPenalty = connections == 0 ? 0 : 1.0 / connections;
        return weight * 1000 + connectionPenalty * 100;
    }

    public void ReportResult(ServiceEndpoint endpoint, LoadBalancingResult result, TimeSpan responseTime)
    {
        if (endpoint == null) return;

        // 更新统计信息
        var stats = _statistics.AddOrUpdate(endpoint.Id,
            new EndpointStatistics(),
            (_, existing) => existing.UpdateWith(result, responseTime));

        // 减少连接计数（假设请求完成）
        if (_connectionStates.TryGetValue(endpoint.Id, out var state))
        {
            state.DecrementConnections();
        }

        _logger.LogDebug("Updated statistics for endpoint {Endpoint}: {Stats}, Connections: {Connections}",
            endpoint.Address, stats, GetConnectionCount(endpoint.Id));
    }

    public void Reset()
    {
        _connectionStates.Clear();
        _statistics.Clear();
        _logger.LogDebug("Reset LeastConnections load balancer state");
    }

    public Dictionary<string, object> GetStatistics()
    {
        return new Dictionary<string, object>
        {
            ["Strategy"] = Strategy.ToString(),
            ["EndpointCount"] = _connectionStates.Count,
            ["ConnectionStates"] = _connectionStates.ToDictionary(
                kvp => kvp.Key,
                kvp => new Dictionary<string, object>
                {
                    ["ActiveConnections"] = kvp.Value.ActiveConnections,
                    ["TotalConnections"] = kvp.Value.TotalConnections
                }),
            ["EndpointStatistics"] = _statistics.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToDictionary())
        };
    }

    /// <summary>
    /// 获取指定端点的活跃连接数
    /// </summary>
    /// <param name="endpointId">端点ID</param>
    /// <returns>活跃连接数</returns>
    public int GetConnectionCount(string endpointId)
    {
        return _connectionStates.TryGetValue(endpointId, out var state) ? state.ActiveConnections : 0;
    }

    /// <summary>
    /// 手动增加连接计数（用于外部连接管理）
    /// </summary>
    /// <param name="endpointId">端点ID</param>
    public void IncrementConnections(string endpointId)
    {
        var state = _connectionStates.GetOrAdd(endpointId, _ => new ConnectionState());
        state.IncrementConnections();
    }

    /// <summary>
    /// 手动减少连接计数（用于外部连接管理）
    /// </summary>
    /// <param name="endpointId">端点ID</param>
    public void DecrementConnections(string endpointId)
    {
        if (_connectionStates.TryGetValue(endpointId, out var state))
        {
            state.DecrementConnections();
        }
    }
}

/// <summary>
/// 连接状态跟踪
/// </summary>
internal class ConnectionState
{
    private int _activeConnections;
    private long _totalConnections;
    private readonly object _lock = new();

    public int ActiveConnections
    {
        get { lock (_lock) return _activeConnections; }
    }

    public long TotalConnections
    {
        get { lock (_lock) return _totalConnections; }
    }

    public void IncrementConnections()
    {
        lock (_lock)
        {
            _activeConnections++;
            _totalConnections++;
        }
    }

    public void DecrementConnections()
    {
        lock (_lock)
        {
            if (_activeConnections > 0)
            {
                _activeConnections--;
            }
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _activeConnections = 0;
        }
    }
}
