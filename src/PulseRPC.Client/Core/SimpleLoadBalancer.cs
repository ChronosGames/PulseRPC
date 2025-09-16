using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;

namespace PulseRPC.Client.Core;

/// <summary>
/// 简单负载均衡器实现 - Stage 1 基础版本
/// </summary>
public sealed class SimpleLoadBalancer : ILoadBalancer
{
    private readonly ILogger<SimpleLoadBalancer> _logger;
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
    public SimpleLoadBalancer(
        LoadBalancingStrategy strategy = LoadBalancingStrategy.RoundRobin,
        ILogger<SimpleLoadBalancer>? logger = null)
    {
        Strategy = strategy;
        _logger = logger ?? NullLogger<SimpleLoadBalancer>.Instance;
    }

    /// <summary>
    /// 选择最佳连接
    /// </summary>
    public IConnection? SelectConnection(IReadOnlyList<IConnection> connections, LoadBalancingHint hint = LoadBalancingHint.None)
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

        IConnection? selectedConnection = effectiveStrategy switch
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
    private IConnection SelectRoundRobin(IReadOnlyList<IConnection> connections)
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
    private IConnection SelectRandom(IReadOnlyList<IConnection> connections)
    {
        lock (_lock)
        {
            var index = _random.Next(connections.Count);
            return connections[index];
        }
    }

    /// <summary>
    /// 最少连接选择
    /// </summary>
    private IConnection SelectLeastConnections(IReadOnlyList<IConnection> connections)
    {
        // 简化实现：选择第一个连接（Stage 1）
        // 在 Stage 2 中会实现真正的连接数统计
        _logger.LogDebug("最少连接策略：当前使用简化实现（选择第一个连接）");
        return connections[0];
    }

    /// <summary>
    /// 加权轮询选择
    /// </summary>
    private IConnection SelectWeightedRoundRobin(IReadOnlyList<IConnection> connections)
    {
        // 简化实现：忽略权重，使用普通轮询（Stage 1）
        // 在 Stage 2 中会实现真正的权重支持
        _logger.LogDebug("加权轮询策略：当前使用简化实现（普通轮询）");
        return SelectRoundRobin(connections);
    }

    /// <summary>
    /// 一致性哈希选择
    /// </summary>
    private IConnection SelectConsistentHash(IReadOnlyList<IConnection> connections, LoadBalancingHint hint)
    {
        // 简化实现：基于连接ID的哈希（Stage 1）
        // 在 Stage 2 中会实现真正的一致性哈希环
        var hashKey = hint.ToString() + DateTime.UtcNow.Ticks.ToString();
        var hash = hashKey.GetHashCode();
        var index = Math.Abs(hash) % connections.Count;

        _logger.LogDebug("一致性哈希策略：当前使用简化实现（基于提示的哈希）");
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
    private static bool IsHealthyConnection(IConnection connection)
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
    private static int GetConnectionWeight(IConnection connection)
    {
        // 简化实现：所有连接权重相等（Stage 1）
        // 在 Stage 2 中从连接标签或配置中读取权重
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

// LoadBalancingHint enum is defined in IPulseRPCClient.cs

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