using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace DistributedGameApp.Infrastructure.ServiceClient;

/// <summary>
/// 通用服务路由器 - 支持多种路由策略
/// </summary>
/// <remarks>
/// 使用事件驱动更新一致性哈希环，避免每次查询时同步
/// </remarks>
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

            // 订阅连接变更事件，事件驱动更新哈希环
            _connectionManager.ConnectionAdded += OnConnectionAdded;
            _connectionManager.ConnectionRemoved += OnConnectionRemoved;

            // 初始化现有连接到哈希环
            InitializeExistingConnections();
        }
    }

    /// <summary>
    /// 初始化现有连接到哈希环
    /// </summary>
    private void InitializeExistingConnections()
    {
        if (_consistentHash == null) return;

        var connections = _connectionManager.GetAllConnections();
        foreach (var connection in connections)
        {
            _consistentHash.AddNode(connection.ServiceInfo.ServiceId);
        }

        _logger.LogDebug("一致性哈希环初始化完成，节点数: {NodeCount}", connections.Count);
    }

    /// <summary>
    /// 连接添加事件处理
    /// </summary>
    private void OnConnectionAdded(object? sender, ServiceConnection connection)
    {
        if (_consistentHash == null || _isDisposed) return;

        _consistentHash.AddNode(connection.ServiceInfo.ServiceId);
        _logger.LogDebug("一致性哈希环添加节点: {ServiceId}, 当前节点数: {NodeCount}",
            connection.ServiceInfo.ServiceId, _consistentHash.NodeCount);
    }

    /// <summary>
    /// 连接移除事件处理
    /// </summary>
    private void OnConnectionRemoved(object? sender, ServiceConnection connection)
    {
        if (_consistentHash == null || _isDisposed) return;

        _consistentHash.RemoveNode(connection.ServiceInfo.ServiceId);
        _logger.LogDebug("一致性哈希环移除节点: {ServiceId}, 当前节点数: {NodeCount}",
            connection.ServiceInfo.ServiceId, _consistentHash.NodeCount);
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
    /// 一致性哈希路由（事件驱动，O(1) 复杂度）
    /// </summary>
    private ServiceConnection? GetConnectionByConsistentHash(string routingKey)
    {
        if (_consistentHash == null)
        {
            _logger.LogError("一致性哈希未初始化");
            return null;
        }

        // 快速检查：如果哈希环为空，直接返回
        if (_consistentHash.NodeCount == 0)
        {
            _logger.LogWarning("一致性哈希环为空，没有可用的连接");
            return null;
        }

        // O(log n) 查找：直接从哈希环获取节点
        var selectedNode = _consistentHash.GetNode(routingKey);
        if (string.IsNullOrEmpty(selectedNode))
        {
            _logger.LogWarning("一致性哈希未找到节点: {RoutingKey}", routingKey);
            return null;
        }

        // O(1) 查找：从连接管理器获取连接
        var connection = _connectionManager.GetConnection(selectedNode);
        if (connection == null || connection.State != ConnectionState.Connected)
        {
            // 节点存在于哈希环但连接不可用，可能是状态不一致
            // 回退到其他可用连接
            _logger.LogWarning("选中的节点连接不可用: {ServiceId}, 尝试回退", selectedNode);
            var connections = _connectionManager.GetAllConnections();
            return connections.FirstOrDefault();
        }

        return connection;
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

        // 取消事件订阅
        if (_strategy == RoutingStrategy.ConsistentHash)
        {
            _connectionManager.ConnectionAdded -= OnConnectionAdded;
            _connectionManager.ConnectionRemoved -= OnConnectionRemoved;
        }

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

