using DistributedGameApp.Infrastructure.ServiceClient;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.BackendServer.Services.Battle;

/// <summary>
/// BattleServer 路由器 - 使用一致性哈希进行分片路由
/// </summary>
public class BattleServerRouter : IDisposable
{
    private readonly BattleServerConnectionManager _connectionManager;
    private readonly ILogger<BattleServerRouter> _logger;
    private readonly ConsistentHash<string> _consistentHash;
    private readonly Dictionary<string, BattleServerConnection> _serviceIdToConnection = new();
    private readonly object _lock = new();
    private bool _isDisposed;

    /// <summary>
    /// 路由策略
    /// </summary>
    public RoutingStrategy Strategy { get; }

    public BattleServerRouter(
        BattleServerConnectionManager connectionManager,
        ILogger<BattleServerRouter> logger,
        RoutingStrategy strategy = RoutingStrategy.ConsistentHash)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Strategy = strategy;

        // 初始化一致性哈希环(每个物理节点 150 个虚拟节点)
        _consistentHash = new ConsistentHash<string>(virtualNodeCount: 150);

        // 订阅连接变更事件
        _connectionManager.ConnectionChanged += OnConnectionChanged;

        // 初始化哈希环
        InitializeHashRing();
    }

    /// <summary>
    /// 根据路由键获取连接(用于分片)
    /// </summary>
    /// <param name="routingKey">路由键(通常是 RoomId 或 MatchId)</param>
    public BattleServerConnection? GetConnection(string routingKey)
    {
        if (string.IsNullOrEmpty(routingKey))
            throw new ArgumentException("Routing key cannot be null or empty", nameof(routingKey));

        lock (_lock)
        {
            return Strategy switch
            {
                RoutingStrategy.ConsistentHash => GetConnectionByConsistentHash(routingKey),
                RoutingStrategy.RoundRobin => GetConnectionByRoundRobin(),
                RoutingStrategy.Random => GetConnectionByRandom(),
                RoutingStrategy.LeastConnections => GetConnectionByLeastLoad(),
                _ => throw new NotSupportedException($"Routing strategy not supported: {Strategy}")
            };
        }
    }

    /// <summary>
    /// 根据路由键获取多个连接(用于容错/备份)
    /// </summary>
    public List<BattleServerConnection> GetConnections(string routingKey, int count)
    {
        if (string.IsNullOrEmpty(routingKey))
            throw new ArgumentException("Routing key cannot be null or empty", nameof(routingKey));

        if (count <= 0)
            throw new ArgumentException("Count must be positive", nameof(count));

        lock (_lock)
        {
            if (Strategy != RoutingStrategy.ConsistentHash)
            {
                throw new NotSupportedException("Multiple connections only supported with ConsistentHash strategy");
            }

            var serviceIds = _consistentHash.GetNodes(routingKey, count);
            var connections = new List<BattleServerConnection>();

            foreach (var serviceId in serviceIds)
            {
                if (_serviceIdToConnection.TryGetValue(serviceId, out var connection) &&
                    connection.State == ConnectionState.Connected)
                {
                    connections.Add(connection);
                }
            }

            return connections;
        }
    }

    /// <summary>
    /// 获取所有可用连接
    /// </summary>
    public List<BattleServerConnection> GetAllConnections()
    {
        return _connectionManager.GetAllConnections();
    }

    /// <summary>
    /// 使用一致性哈希获取连接
    /// </summary>
    private BattleServerConnection? GetConnectionByConsistentHash(string routingKey)
    {
        var serviceId = _consistentHash.GetNode(routingKey);

        if (serviceId == null)
        {
            _logger.LogWarning("一致性哈希未找到可用节点: routingKey={RoutingKey}", routingKey);
            return null;
        }

        if (!_serviceIdToConnection.TryGetValue(serviceId, out var connection) ||
            connection.State != ConnectionState.Connected)
        {
            _logger.LogWarning("节点不可用,尝试获取备用节点: serviceId={ServiceId}", serviceId);

            // 尝试获取下一个节点
            var backupServiceIds = _consistentHash.GetNodes(routingKey, 3);
            foreach (var backupServiceId in backupServiceIds.Skip(1))
            {
                if (_serviceIdToConnection.TryGetValue(backupServiceId, out var backupConnection) &&
                    backupConnection.State == ConnectionState.Connected)
                {
                    _logger.LogInformation("使用备用节点: {BackupServiceId}", backupServiceId);
                    return backupConnection;
                }
            }

            return null;
        }

        return connection;
    }

    /// <summary>
    /// 使用轮询获取连接
    /// </summary>
    private BattleServerConnection? GetConnectionByRoundRobin()
    {
        var connections = _serviceIdToConnection.Values
            .Where(c => c.State == ConnectionState.Connected)
            .ToList();

        if (connections.Count == 0)
            return null;

        // 简单的轮询:根据请求总数取模
        var totalRequests = connections.Sum(c => c.RequestCount);
        var index = (int)(totalRequests % connections.Count);

        return connections[index];
    }

    /// <summary>
    /// 使用随机获取连接
    /// </summary>
    private BattleServerConnection? GetConnectionByRandom()
    {
        var connections = _serviceIdToConnection.Values
            .Where(c => c.State == ConnectionState.Connected)
            .ToList();

        if (connections.Count == 0)
            return null;

        var random = new Random();
        return connections[random.Next(connections.Count)];
    }

    /// <summary>
    /// 使用最少负载获取连接
    /// </summary>
    private BattleServerConnection? GetConnectionByLeastLoad()
    {
        return _serviceIdToConnection.Values
            .Where(c => c.State == ConnectionState.Connected)
            .OrderBy(c => c.RequestCount)
            .FirstOrDefault();
    }

    /// <summary>
    /// 初始化哈希环
    /// </summary>
    private void InitializeHashRing()
    {
        lock (_lock)
        {
            var connections = _connectionManager.GetAllConnections();

            _logger.LogInformation("初始化哈希环,节点数: {Count}", connections.Count);

            foreach (var connection in connections)
            {
                AddNodeToHashRing(connection);
            }

            var stats = _consistentHash.GetStats();
            _logger.LogInformation("哈希环初始化完成: 物理节点={Physical}, 虚拟节点={Virtual}",
                stats.NodeCount, stats.VirtualNodeCount);
        }
    }

    /// <summary>
    /// 将节点添加到哈希环
    /// </summary>
    private void AddNodeToHashRing(BattleServerConnection connection)
    {
        var serviceId = connection.ServiceInfo.ServiceId;

        _consistentHash.AddNode(serviceId);
        _serviceIdToConnection[serviceId] = connection;

        _logger.LogInformation("添加节点到哈希环: {ServiceId}", serviceId);
    }

    /// <summary>
    /// 从哈希环移除节点
    /// </summary>
    private void RemoveNodeFromHashRing(string serviceId)
    {
        _consistentHash.RemoveNode(serviceId);
        _serviceIdToConnection.Remove(serviceId);

        _logger.LogInformation("从哈希环移除节点: {ServiceId}", serviceId);
    }

    /// <summary>
    /// 连接变更处理
    /// </summary>
    private void OnConnectionChanged(object? sender, ConnectionChangeEvent e)
    {
        lock (_lock)
        {
            switch (e.Type)
            {
                case ConnectionChangeType.Added:
                    if (e.Connection != null)
                    {
                        _logger.LogInformation("路由器检测到新连接: {ServiceId}", e.ServiceId);
                        AddNodeToHashRing(e.Connection);
                    }
                    break;

                case ConnectionChangeType.Removed:
                    _logger.LogInformation("路由器检测到连接移除: {ServiceId}", e.ServiceId);
                    RemoveNodeFromHashRing(e.ServiceId);
                    break;

                case ConnectionChangeType.Modified:
                    _logger.LogInformation("路由器检测到连接变更: {ServiceId}", e.ServiceId);
                    RemoveNodeFromHashRing(e.ServiceId);
                    if (e.Connection != null)
                    {
                        AddNodeToHashRing(e.Connection);
                    }
                    break;
            }

            var stats = _consistentHash.GetStats();
            _logger.LogInformation("哈希环更新: 物理节点={Physical}, 虚拟节点={Virtual}",
                stats.NodeCount, stats.VirtualNodeCount);
        }
    }

    /// <summary>
    /// 获取路由统计信息
    /// </summary>
    public RouterStats GetStats()
    {
        lock (_lock)
        {
            var hashStats = _consistentHash.GetStats();
            var connections = _serviceIdToConnection.Values.ToList();

            return new RouterStats
            {
                Strategy = Strategy,
                TotalNodes = hashStats.NodeCount,
                VirtualNodesPerNode = hashStats.VirtualNodesPerNode,
                AvailableConnections = connections.Count(c => c.State == ConnectionState.Connected),
                TotalConnections = connections.Count
            };
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        _connectionManager.ConnectionChanged -= OnConnectionChanged;

        lock (_lock)
        {
            _consistentHash.Clear();
            _serviceIdToConnection.Clear();
        }

        _logger.LogInformation("BattleServerRouter 已释放");
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
    /// 最少连接
    /// </summary>
    LeastConnections
}

/// <summary>
/// 路由统计信息
/// </summary>
public class RouterStats
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

    /// <summary>
    /// 可用连接数
    /// </summary>
    public int AvailableConnections { get; init; }

    /// <summary>
    /// 总连接数
    /// </summary>
    public int TotalConnections { get; init; }
}
