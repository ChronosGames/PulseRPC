using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using PulseRPC.Messaging;

namespace PulseRPC.Client.Configuration;

/// <summary>
/// 基于连接的负载均衡器实现
/// 支持多种负载均衡策略，针对已建立的连接进行智能分配
/// </summary>
public sealed class ConnectionLoadBalancer : ILoadBalancer
{
    private readonly ILogger<ConnectionLoadBalancer> _logger;
    private readonly ConcurrentDictionary<string, int> _roundRobinCounters = new();
    private readonly Random _random = new();
    private readonly object _lock = new();

    /// <summary>
    /// 负载均衡策略
    /// </summary>
    public LoadBalancingStrategy Strategy { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public ConnectionLoadBalancer(
        LoadBalancingStrategy strategy = LoadBalancingStrategy.RoundRobin,
        ILogger<ConnectionLoadBalancer>? logger = null)
    {
        Strategy = strategy;
        _logger = logger ?? NullLogger<ConnectionLoadBalancer>.Instance;
    }

    /// <summary>
    /// 选择最佳连接
    /// </summary>
    public IClientChannel? SelectConnection(IReadOnlyList<IClientChannel> connections, LoadBalancingHint hint = LoadBalancingHint.None)
    {
        if (connections == null || connections.Count == 0)
        {
            _logger.LogWarning("没有可用的连接进行负载均衡");
            return null;
        }

        // 过滤健康的连接
        var healthyConnections = connections.Where(IsHealthyConnection).ToList();
        if (healthyConnections.Count == 0)
        {
            _logger.LogWarning("没有健康的连接可用于负载均衡");
            return null;
        }

        // 根据提示调整策略
        var effectiveStrategy = ApplyHint(Strategy, hint);

        var selectedConnection = effectiveStrategy switch
        {
            LoadBalancingStrategy.RoundRobin => SelectRoundRobin(healthyConnections),
            LoadBalancingStrategy.Random => SelectRandom(healthyConnections),
            LoadBalancingStrategy.LeastConnections => SelectLeastConnections(healthyConnections),
            LoadBalancingStrategy.WeightedRoundRobin => SelectWeightedRoundRobin(healthyConnections),
            LoadBalancingStrategy.ConsistentHash => SelectConsistentHash(healthyConnections, hint),
            _ => SelectRoundRobin(healthyConnections) // 默认使用轮询
        };

        if (selectedConnection != null)
        {
            _logger.LogDebug("负载均衡选择连接: {Strategy} -> {ConnectionId}",
                effectiveStrategy, selectedConnection.Id);
        }

        return selectedConnection;
    }

    /// <summary>
    /// 轮询选择
    /// </summary>
    private IClientChannel SelectRoundRobin(IReadOnlyList<IClientChannel> connections)
    {
        lock (_lock)
        {
            var key = "global";
            var counter = _roundRobinCounters.AddOrUpdate(key, 0, (_, value) => (value + 1) % connections.Count);
            return connections[counter];
        }
    }

    /// <summary>
    /// 随机选择
    /// </summary>
    private IClientChannel SelectRandom(IReadOnlyList<IClientChannel> connections)
    {
        lock (_lock)
        {
            var index = _random.Next(connections.Count);
            return connections[index];
        }
    }

    /// <summary>
    /// 最少连接选择 - 选择活跃请求数最少的连接
    /// </summary>
    private IClientChannel SelectLeastConnections(IReadOnlyList<IClientChannel> connections)
    {
        IClientChannel? bestConnection = null;
        int minActiveRequests = int.MaxValue;

        foreach (var connection in connections)
        {
            var activeRequests = connection.Statistics.ActiveRequests;

            if (activeRequests < minActiveRequests)
            {
                minActiveRequests = activeRequests;
                bestConnection = connection;
            }
        }

        if (bestConnection == null)
        {
            // 降级处理：如果所有连接统计信息不可用，使用第一个连接
            _logger.LogWarning("无法获取连接统计信息，使用第一个连接");
            return connections[0];
        }

        _logger.LogDebug("最少连接策略：选择连接 {ConnectionId}，活跃请求数: {ActiveRequests}",
            bestConnection.Id, minActiveRequests);

        return bestConnection;
    }

    /// <summary>
    /// 加权轮询选择
    /// </summary>
    private IClientChannel SelectWeightedRoundRobin(IReadOnlyList<IClientChannel> connections)
    {
        // TODO: 从连接标签或配置中读取权重信息
        // 当前实现：忽略权重，使用普通轮询
        _logger.LogDebug("加权轮询策略：当前使用简化实现（普通轮询）");
        return SelectRoundRobin(connections);
    }

    /// <summary>
    /// 一致性哈希选择
    /// </summary>
    private IClientChannel SelectConsistentHash(IReadOnlyList<IClientChannel> connections, LoadBalancingHint hint)
    {
        // TODO: 实现基于虚拟节点的一致性哈希环
        // 当前实现：基于提示的简单哈希
        var hashKey = hint.ToString() + DateTime.UtcNow.Ticks.ToString();
        var hash = hashKey.GetHashCode();
        var index = Math.Abs(hash) % connections.Count;

        _logger.LogDebug("一致性哈希策略：选择连接索引 {Index}", index);
        return connections[index];
    }

    /// <summary>
    /// 应用负载均衡提示
    /// </summary>
    private static LoadBalancingStrategy ApplyHint(LoadBalancingStrategy strategy, LoadBalancingHint hint)
    {
        return hint switch
        {
            LoadBalancingHint.PreferLocal => LoadBalancingStrategy.LeastConnections,
            LoadBalancingHint.PreferFast => LoadBalancingStrategy.LeastConnections,
            LoadBalancingHint.Sticky => LoadBalancingStrategy.ConsistentHash,
            LoadBalancingHint.Distribute => LoadBalancingStrategy.RoundRobin,
            _ => strategy
        };
    }

    /// <summary>
    /// 检查连接是否健康
    /// </summary>
    private static bool IsHealthyConnection(IClientChannel connection)
    {
        return connection.State switch
        {
            ExtendedConnectionState.Connected => true,
            ExtendedConnectionState.Active => true,
            ExtendedConnectionState.Idle => true,
            _ => false
        };
    }

    /// <summary>
    /// 获取连接权重
    /// </summary>
    private static int GetConnectionWeight(IClientChannel connection)
    {
        // TODO: 从连接标签或配置中读取权重
        // 当前实现：所有连接权重相等
        return 1;
    }

    /// <summary>
    /// 重置轮询计数器
    /// </summary>
    public void ResetCounters()
    {
        _roundRobinCounters.Clear();
        _logger.LogDebug("重置负载均衡计数器");
    }

    /// <summary>
    /// 获取统计信息
    /// </summary>
    public LoadBalancerStatistics GetStatistics()
    {
        return new LoadBalancerStatistics
        {
            Strategy = Strategy,
            TotalSelections = _roundRobinCounters.Values.Sum(),
            ActiveCounters = _roundRobinCounters.Count
        };
    }
}

// LoadBalancingHint enum is defined in IPulseClient.cs

/// <summary>
/// 负载均衡器统计信息
/// </summary>
public sealed class LoadBalancerStatistics
{
    /// <summary>
    /// 负载均衡策略
    /// </summary>
    public LoadBalancingStrategy Strategy { get; set; }

    /// <summary>
    /// 总选择次数
    /// </summary>
    public long TotalSelections { get; set; }

    /// <summary>
    /// 活跃计数器数量
    /// </summary>
    public int ActiveCounters { get; set; }

    /// <summary>
    /// 统计时间
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
