using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace DistributedGameApp.Infrastructure.ServiceClient;

/// <summary>
/// 通用服务路由器 - 支持多种路由策略
/// </summary>
public class ServiceRouter : IDisposable
{
    private readonly ServiceConnectionManager _connectionManager;
    private readonly ILogger<ServiceRouter> _logger;
    private readonly RoutingStrategy _strategy;
    private readonly ConsistentHash<string>? _consistentHash;
    private int _roundRobinIndex;
    private readonly object _roundRobinLock = new();
    private bool _isDisposed;

    public ServiceRouter(
        ServiceConnectionManager connectionManager,
        ILogger<ServiceRouter> logger,
        RoutingStrategy strategy = RoutingStrategy.ConsistentHash,
        int virtualNodesPerNode = 150)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _strategy = strategy;

        // 如果使用一致性哈希，初始化哈希环
        if (_strategy == RoutingStrategy.ConsistentHash)
        {
            _consistentHash = new ConsistentHash<string>(virtualNodesPerNode);
        }
    }

    /// <summary>
    /// 路由策略
    /// </summary>
    public RoutingStrategy Strategy => _strategy;

    /// <summary>
    /// 获取连接（根据路由键）
    /// </summary>
    public ServiceConnection? GetConnection(string routingKey)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(ServiceRouter));

        if (string.IsNullOrEmpty(routingKey))
            throw new ArgumentException("Routing key cannot be null or empty", nameof(routingKey));

        return _strategy switch
        {
            RoutingStrategy.ConsistentHash => GetConnectionByConsistentHash(routingKey),
            RoutingStrategy.RoundRobin => GetConnectionByRoundRobin(),
            RoutingStrategy.Random => GetConnectionByRandom(),
            RoutingStrategy.LeastConnections => GetConnectionByLeastLoad(),
            _ => throw new NotSupportedException($"不支持的路由策略: {_strategy}")
        };
    }

    /// <summary>
    /// 获取所有可用连接
    /// </summary>
    public List<ServiceConnection> GetAllConnections()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(ServiceRouter));

        return _connectionManager.GetAllConnections();
    }

    /// <summary>
    /// 一致性哈希路由
    /// </summary>
    private ServiceConnection? GetConnectionByConsistentHash(string routingKey)
    {
        if (_consistentHash == null)
        {
            _logger.LogError("一致性哈希未初始化");
            return null;
        }

        var connections = _connectionManager.GetAllConnections();
        if (connections.Count == 0)
        {
            _logger.LogWarning("没有可用的连接");
            return null;
        }

        // 确保哈希环包含所有当前连接
        var currentNodes = connections.Select(c => c.ServiceInfo.ServiceId).ToHashSet();
        var hashNodes = _consistentHash.GetAllNodes().ToHashSet();

        // 添加新节点
        foreach (var node in currentNodes.Except(hashNodes))
        {
            _consistentHash.AddNode(node);
        }

        // 移除失效节点
        foreach (var node in hashNodes.Except(currentNodes))
        {
            _consistentHash.RemoveNode(node);
        }

        // 根据路由键获取节点
        var selectedNode = _consistentHash.GetNode(routingKey);
        if (string.IsNullOrEmpty(selectedNode))
        {
            _logger.LogWarning("一致性哈希未找到节点: {RoutingKey}", routingKey);
            return connections.FirstOrDefault();
        }

        return connections.FirstOrDefault(c => c.ServiceInfo.ServiceId == selectedNode);
    }

    /// <summary>
    /// 轮询路由
    /// </summary>
    private ServiceConnection? GetConnectionByRoundRobin()
    {
        var connections = _connectionManager.GetAllConnections();
        if (connections.Count == 0)
        {
            _logger.LogWarning("没有可用的连接");
            return null;
        }

        lock (_roundRobinLock)
        {
            var index = _roundRobinIndex % connections.Count;
            _roundRobinIndex++;
            return connections[index];
        }
    }

    /// <summary>
    /// 随机路由
    /// </summary>
    private ServiceConnection? GetConnectionByRandom()
    {
        var connections = _connectionManager.GetAllConnections();
        if (connections.Count == 0)
        {
            _logger.LogWarning("没有可用的连接");
            return null;
        }

        var index = Random.Shared.Next(connections.Count);
        return connections[index];
    }

    /// <summary>
    /// 最少连接数路由
    /// </summary>
    private ServiceConnection? GetConnectionByLeastLoad()
    {
        var connections = _connectionManager.GetAllConnections();
        if (connections.Count == 0)
        {
            _logger.LogWarning("没有可用的连接");
            return null;
        }

        return connections.OrderBy(c => c.RequestCount).FirstOrDefault();
    }

    /// <summary>
    /// 获取路由器统计信息
    /// </summary>
    public ServiceRouterStats GetStats()
    {
        var connections = _connectionManager.GetAllConnections();

        return new ServiceRouterStats
        {
            Strategy = _strategy,
            TotalNodes = connections.Count,
            VirtualNodesPerNode = _consistentHash?.GetStats().VirtualNodesPerNode ?? 0
        };
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _consistentHash?.Clear();
    }
}

/// <summary>
/// 路由策略
/// </summary>
public enum RoutingStrategy
{
    /// <summary>
    /// 一致性哈希（推荐用于分片）
    /// </summary>
    ConsistentHash,

    /// <summary>
    /// 轮询
    /// </summary>
    RoundRobin,

    /// <summary>
    /// 随机
    /// </summary>
    Random,

    /// <summary>
    /// 最少连接数
    /// </summary>
    LeastConnections
}

/// <summary>
/// 路由器统计信息
/// </summary>
public class ServiceRouterStats
{
    /// <summary>
    /// 路由策略
    /// </summary>
    public RoutingStrategy Strategy { get; init; }

    /// <summary>
    /// 总节点数
    /// </summary>
    public int TotalNodes { get; init; }

    /// <summary>
    /// 每个物理节点的虚拟节点数
    /// </summary>
    public int VirtualNodesPerNode { get; init; }
}

