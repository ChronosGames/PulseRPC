using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;
using PulseRPC.Messaging;

namespace PulseRPC.Client.Configuration;

/// <summary>
/// 基于连接的负载均衡器实现
/// 支持多种负载均衡策略，针对已建立的连接进行智能分配
/// </summary>
public sealed class ConnectionLoadBalancer : IContextualLoadBalancer
{
    private readonly ILogger<ConnectionLoadBalancer> _logger;
    private readonly Dictionary<string, int> _roundRobinCounters = new();
    private readonly Dictionary<string, long> _weightedCurrentWeights = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _lastWeights = new(StringComparer.Ordinal);
    private readonly IConnectionWeightProvider _weightProvider;
    private readonly int _consistentHashVirtualNodes;
    private readonly Random _random = new();
    private readonly object _lock = new();
    private string? _consistentHashMembership;
    private HashPoint[] _consistentHashRing = Array.Empty<HashPoint>();
    private long _totalSelections;

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
        : this(strategy, new ConnectionLoadBalancingOptions(), logger)
    {
    }

    /// <summary>
    /// 使用强类型高级配置创建负载均衡器。
    /// </summary>
    internal ConnectionLoadBalancer(
        LoadBalancingStrategy strategy,
        ConnectionLoadBalancingOptions options,
        ILogger<ConnectionLoadBalancer>? logger)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }
        if (options.ConsistentHashVirtualNodes is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "ConsistentHashVirtualNodes must be between 1 and 4096.");
        }

        Strategy = strategy;
        _weightProvider = options.WeightProvider ?? DescriptorConnectionWeightProvider.Instance;
        _consistentHashVirtualNodes = options.ConsistentHashVirtualNodes;
        _logger = logger ?? NullLogger<ConnectionLoadBalancer>.Instance;
    }

    /// <summary>
    /// 选择最佳连接
    /// </summary>
    public IClientChannel? SelectConnection(IReadOnlyList<IClientChannel> connections, LoadBalancingHint hint = LoadBalancingHint.None)
        => SelectConnection(connections, new LoadBalancingContext(hint));

    /// <summary>
    /// 使用稳定路由上下文选择最佳连接。
    /// </summary>
    IClientChannel? IContextualLoadBalancer.SelectConnection(
        IReadOnlyList<IClientChannel> connections,
        LoadBalancingContext context)
        => SelectConnection(connections, context);

    internal IClientChannel? SelectConnection(
        IReadOnlyList<IClientChannel> connections,
        LoadBalancingContext context)
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
        var effectiveStrategy = ApplyHint(Strategy, context.Hint);

        var selectedConnection = effectiveStrategy switch
        {
            LoadBalancingStrategy.RoundRobin => SelectRoundRobin(healthyConnections),
            LoadBalancingStrategy.Random => SelectRandom(healthyConnections),
            LoadBalancingStrategy.LeastConnections => SelectLeastConnections(healthyConnections),
            LoadBalancingStrategy.WeightedRoundRobin => SelectWeightedRoundRobin(healthyConnections),
            LoadBalancingStrategy.ConsistentHash => SelectConsistentHash(healthyConnections, context.StickyKey),
            _ => throw new ArgumentOutOfRangeException(nameof(Strategy), effectiveStrategy, "未知负载均衡策略。")
        };

        if (selectedConnection != null)
        {
            Interlocked.Increment(ref _totalSelections);
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
            var counter = _roundRobinCounters.TryGetValue(key, out var previous)
                ? (previous + 1) % connections.Count
                : 0;
            _roundRobinCounters[key] = counter;
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

    private IClientChannel SelectWeightedRoundRobin(IReadOnlyList<IClientChannel> connections)
    {
        var orderedConnections = NormalizeConnections(connections);
        lock (_lock)
        {
            var weights = orderedConnections.ToDictionary(
                connection => connection.Id,
                connection => GetValidatedWeight(connection),
                StringComparer.Ordinal);
            if (!HaveSameWeights(weights, _lastWeights))
            {
                _weightedCurrentWeights.Clear();
                _lastWeights.Clear();
                foreach (var item in weights)
                {
                    _lastWeights[item.Key] = item.Value;
                }
            }

            long totalWeight = 0;
            long selectedWeight = long.MinValue;
            IClientChannel? selected = null;
            foreach (var connection in orderedConnections)
            {
                var weight = weights[connection.Id];
                totalWeight += weight;
                var currentWeight = _weightedCurrentWeights.TryGetValue(connection.Id, out var current)
                    ? current + weight
                    : weight;
                _weightedCurrentWeights[connection.Id] = currentWeight;
                if (currentWeight > selectedWeight)
                {
                    selected = connection;
                    selectedWeight = currentWeight;
                }
            }

            selected ??= orderedConnections[0];
            _weightedCurrentWeights[selected.Id] -= totalWeight;
            return selected;
        }
    }

    private IClientChannel SelectConsistentHash(
        IReadOnlyList<IClientChannel> connections,
        string? stickyKey)
    {
        if (string.IsNullOrWhiteSpace(stickyKey))
        {
            throw new InvalidOperationException(
                "ConsistentHash requires a non-empty LoadBalancingContext.StickyKey.");
        }

        var orderedConnections = NormalizeConnections(connections);
        var membership = string.Join(
            "|",
            orderedConnections.Select(connection =>
                connection.Id.Length.ToString(CultureInfo.InvariantCulture) + ":" + connection.Id));
        HashPoint[] ring;
        lock (_lock)
        {
            if (!string.Equals(_consistentHashMembership, membership, StringComparison.Ordinal))
            {
                _consistentHashRing = BuildConsistentHashRing(orderedConnections);
                _consistentHashMembership = membership;
            }

            ring = _consistentHashRing;
        }

        var keyHash = StableHash(stickyKey);
        var lower = 0;
        var upper = ring.Length;
        while (lower < upper)
        {
            var midpoint = lower + ((upper - lower) / 2);
            if (ring[midpoint].Hash < keyHash)
            {
                lower = midpoint + 1;
            }
            else
            {
                upper = midpoint;
            }
        }

        var connectionId = ring[lower == ring.Length ? 0 : lower].ConnectionId;
        return orderedConnections.First(connection =>
            string.Equals(connection.Id, connectionId, StringComparison.Ordinal));
    }

    private HashPoint[] BuildConsistentHashRing(IReadOnlyList<IClientChannel> connections)
    {
        var ring = new List<HashPoint>(connections.Count * _consistentHashVirtualNodes);
        foreach (var connection in connections)
        {
            for (var virtualNode = 0; virtualNode < _consistentHashVirtualNodes; virtualNode++)
            {
                var identity = connection.Id + "\0" + virtualNode.ToString(CultureInfo.InvariantCulture);
                ring.Add(new HashPoint(StableHash(identity), connection.Id));
            }
        }

        ring.Sort(static (left, right) =>
        {
            var hashComparison = left.Hash.CompareTo(right.Hash);
            return hashComparison != 0
                ? hashComparison
                : StringComparer.Ordinal.Compare(left.ConnectionId, right.ConnectionId);
        });
        return ring.ToArray();
    }

    private int GetValidatedWeight(IClientChannel connection)
    {
        var weight = _weightProvider.GetWeight(connection);
        if (weight <= 0)
        {
            throw new InvalidOperationException(
                $"Weight provider returned {weight} for connection '{connection.Id}'; weights must be positive.");
        }

        return weight;
    }

    private static IClientChannel[] NormalizeConnections(IReadOnlyList<IClientChannel> connections)
    {
        var ordered = connections.OrderBy(connection => connection.Id, StringComparer.Ordinal).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(ordered[index].Id))
            {
                throw new InvalidOperationException("Advanced load balancing requires every connection to have a non-empty Id.");
            }

            if (index > 0 && string.Equals(ordered[index - 1].Id, ordered[index].Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Advanced load balancing requires unique connection IDs; duplicate '{ordered[index].Id}'.");
            }
        }

        return ordered;
    }

    private static bool HaveSameWeights(
        IReadOnlyDictionary<string, int> current,
        IReadOnlyDictionary<string, int> previous)
    {
        return current.Count == previous.Count
            && current.All(item => previous.TryGetValue(item.Key, out var weight) && weight == item.Value);
    }

    private static ulong StableHash(string value)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        foreach (var character in value)
        {
            hash ^= (byte)character;
            hash *= prime;
            hash ^= (byte)(character >> 8);
            hash *= prime;
        }

        return hash;
    }

    /// <summary>
    /// 应用负载均衡提示
    /// </summary>
    private static LoadBalancingStrategy ApplyHint(LoadBalancingStrategy strategy, LoadBalancingHint hint)
    {
        return hint switch
        {
            LoadBalancingHint.LeastConnections => LoadBalancingStrategy.LeastConnections,
            LoadBalancingHint.PreferLocal => LoadBalancingStrategy.LeastConnections,
            LoadBalancingHint.PreferFast => LoadBalancingStrategy.LeastConnections,
            LoadBalancingHint.StickySession => LoadBalancingStrategy.ConsistentHash,
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
    /// 重置轮询计数器
    /// </summary>
    public void ResetCounters()
    {
        lock (_lock)
        {
            _roundRobinCounters.Clear();
            _weightedCurrentWeights.Clear();
            _lastWeights.Clear();
            _consistentHashMembership = null;
            _consistentHashRing = Array.Empty<HashPoint>();
            Interlocked.Exchange(ref _totalSelections, 0);
        }
        _logger.LogDebug("重置负载均衡计数器");
    }

    /// <summary>
    /// 获取统计信息
    /// </summary>
    public LoadBalancerStatistics GetStatistics()
    {
        lock (_lock)
        {
            return new LoadBalancerStatistics
            {
                Strategy = Strategy,
                TotalSelections = Interlocked.Read(ref _totalSelections),
                ActiveCounters = _roundRobinCounters.Count + _weightedCurrentWeights.Count
            };
        }
    }

    private readonly struct HashPoint
    {
        public HashPoint(ulong hash, string connectionId)
        {
            Hash = hash;
            ConnectionId = connectionId;
        }

        public ulong Hash { get; }
        public string ConnectionId { get; }
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
