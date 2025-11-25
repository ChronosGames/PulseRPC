using DistributedGameApp.Infrastructure.Consul;
using Microsoft.Extensions.Logging;
using PulseRPC.Authentication;
using System.Collections.Concurrent;

namespace DistributedGameApp.Infrastructure.ServiceClient;

/// <summary>
/// 统一服务客户端管理器 - Infrastructure 通用版本
/// </summary>
/// <remarks>
/// 统一的服务间通信管理器，支持所有服务类型间的通信：
/// - GameServer ↔ BackendServer
/// - GameServer ↔ BattleServer
/// - BackendServer ↔ BattleServer
/// - BattleServer ↔ GameServer
/// - BattleServer ↔ BackendServer
///
/// 所有服务共享此实现，无需各自维护连接管理逻辑
/// </remarks>
public class UnifiedServiceClientManager : IAsyncDisposable
{
    private readonly ConsulServiceDiscovery _serviceDiscovery;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<UnifiedServiceClientManager> _logger;
    private readonly HubTypeRegistry _hubTypeRegistry;
    private readonly LocalServiceRegistry _localServiceRegistry;
    private readonly IAuthenticationProvider? _authenticationProvider;

    // 每种服务类型一个连接管理器 + 路由器
    private readonly ConcurrentDictionary<ServerType, (ServiceConnectionManager Manager, ServiceRouter Router)> _serverManagers = new();

    private bool _isInitialized;
    private bool _isDisposed;

    public UnifiedServiceClientManager(
        ConsulServiceDiscovery serviceDiscovery,
        ILoggerFactory loggerFactory,
        LocalServiceRegistry localServiceRegistry,
        IAuthenticationProvider? authenticationProvider = null)
    {
        _serviceDiscovery = serviceDiscovery ?? throw new ArgumentNullException(nameof(serviceDiscovery));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _localServiceRegistry = localServiceRegistry ?? throw new ArgumentNullException(nameof(localServiceRegistry));
        _authenticationProvider = authenticationProvider;
        _logger = loggerFactory.CreateLogger<UnifiedServiceClientManager>();
        _hubTypeRegistry = new HubTypeRegistry();
        InitializeHubTypeRegistry();
    }

    /// <summary>
    /// 初始化 Hub 类型注册表
    /// </summary>
    private void InitializeHubTypeRegistry()
    {
        // 注册所有 Hub 类型到服务名称的映射
        _hubTypeRegistry.Register<DistributedGameApp.Shared.Hubs.IBackendHub>("BackendServer");
        _hubTypeRegistry.Register<DistributedGameApp.Shared.Hubs.IBattleHub>("BattleServer");
        _hubTypeRegistry.Register<DistributedGameApp.Shared.Hubs.IGameHub>("GameServer");
        _hubTypeRegistry.Register<DistributedGameApp.Shared.Hubs.IGuildHub>("BackendServer");
    }

    /// <summary>
    /// 注册服务类型
    /// </summary>
    /// <param name="serverType">服务器类型</param>
    /// <param name="strategy">路由策略</param>
    /// <param name="maxRetries">最大重试次数（默认3次）</param>
    /// <param name="retryDelayMs">重试延迟（默认1000ms）</param>
    /// <param name="allowEmpty">是否允许服务列表为空（默认true，允许稍后动态发现）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task RegisterServerTypeAsync(
        ServerType serverType,
        RoutingStrategy strategy = RoutingStrategy.ConsistentHash,
        int maxRetries = 3,
        int retryDelayMs = 1000,
        bool allowEmpty = true,
        CancellationToken cancellationToken = default)
    {
        if (_serverManagers.ContainsKey(serverType))
        {
            _logger.LogWarning("服务类型已注册: {ServerType}", serverType);
            return;
        }

        _logger.LogInformation("注册服务类型: {ServerType}, 策略: {Strategy}", serverType, strategy);

        // 获取服务类型名称（用于 Consul 服务发现）
        var serviceName = serverType.GetServiceName();

        // 创建连接管理器
        var manager = new ServiceConnectionManager(
            _serviceDiscovery,
            _loggerFactory,
            serviceName,
            _authenticationProvider);

        // 创建路由器
        var routerLogger = _loggerFactory.CreateLogger<ServiceRouter>();
        var router = new ServiceRouter(manager, routerLogger, strategy);

        // 初始化连接管理器（带重试）
        await manager.InitializeAsync(maxRetries, retryDelayMs, allowEmpty, cancellationToken);

        _serverManagers[serverType] = (manager, router);

        // ✅ 标记为已初始化（允许单独注册服务类型而不调用 InitializeAsync）
        _isInitialized = true;

        _logger.LogInformation("服务类型注册完成: {ServerType}, 连接数: {Count}",
            serverType, manager.ConnectionCount);
    }

    /// <summary>
    /// 初始化（注册所有服务类型）
    /// </summary>
    /// <param name="serverTypes">要注册的服务类型数组</param>
    /// <param name="strategy">路由策略</param>
    /// <param name="maxRetries">最大重试次数（默认3次）</param>
    /// <param name="retryDelayMs">重试延迟（默认1000ms）</param>
    /// <param name="allowEmpty">是否允许服务列表为空（默认true）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task InitializeAsync(
        ServerType[] serverTypes,
        RoutingStrategy strategy = RoutingStrategy.ConsistentHash,
        int maxRetries = 3,
        int retryDelayMs = 1000,
        bool allowEmpty = true,
        CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            _logger.LogWarning("UnifiedServiceClientManager 已经初始化");
            return;
        }

        _logger.LogInformation("初始化 UnifiedServiceClientManager，服务类型: {Types}", string.Join(", ", serverTypes));

        // 并行注册所有服务类型
        var tasks = serverTypes.Select(st =>
            RegisterServerTypeAsync(st, strategy, maxRetries, retryDelayMs, allowEmpty, cancellationToken));
        await Task.WhenAll(tasks);

        _isInitialized = true;
        _logger.LogInformation("UnifiedServiceClientManager 初始化完成");
    }

    /// <summary>
    /// 获取服务连接（通用路由接口）- 带自动重试和刷新
    /// </summary>
    /// <param name="serverType">服务器类型</param>
    /// <param name="shardId">分片ID（通常是 UserId/PlayerId/MatchId）</param>
    /// <param name="autoRefresh">如果没有连接，是否自动刷新服务列表（默认true）</param>
    public IServiceConnection? GetServer(ServerType serverType, string shardId, bool autoRefresh = true)
    {
        EnsureInitialized();

        if (string.IsNullOrEmpty(shardId))
            throw new ArgumentException("Shard ID cannot be null or empty", nameof(shardId));

        if (!_serverManagers.TryGetValue(serverType, out var managerAndRouter))
        {
            _logger.LogError("服务类型未注册: {ServerType}", serverType);
            return null;
        }

        var (manager, router) = managerAndRouter;

        // 使用路由器获取连接
        var connection = router.GetConnection(shardId);

        // 如果没有连接且允许自动刷新，尝试刷新服务列表
        if (connection == null && autoRefresh && manager.ConnectionCount == 0)
        {
            _logger.LogInformation("未找到可用连接，尝试刷新服务列表: ServerType={ServerType}", serverType);

            // 使用 Task.Run 避免阻塞当前线程
            _ = Task.Run(async () =>
            {
                try
                {
                    await manager.RefreshServicesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "自动刷新服务列表失败: ServerType={ServerType}", serverType);
                }
            });

            _logger.LogWarning("无法获取服务连接（已触发后台刷新）: ServerType={ServerType}, ShardId={ShardId}",
                serverType, shardId);
            return null;
        }

        if (connection == null)
        {
            _logger.LogWarning("无法获取服务连接: ServerType={ServerType}, ShardId={ShardId}",
                serverType, shardId);
            return null;
        }

        return new ServiceConnectionAdapter(connection);
    }

    /// <summary>
    /// 手动刷新指定服务类型的服务列表
    /// </summary>
    public async Task RefreshServerTypeAsync(ServerType serverType, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        if (!_serverManagers.TryGetValue(serverType, out var managerAndRouter))
        {
            _logger.LogError("服务类型未注册: {ServerType}", serverType);
            return;
        }

        var (manager, _) = managerAndRouter;
        await manager.RefreshServicesAsync(cancellationToken);
    }

    /// <summary>
    /// 获取所有可用连接
    /// </summary>
    public List<IServiceConnection> GetAllServers(ServerType serverType)
    {
        EnsureInitialized();

        if (!_serverManagers.TryGetValue(serverType, out var managerAndRouter))
        {
            _logger.LogError("服务类型未注册: {ServerType}", serverType);
            return new List<IServiceConnection>();
        }

        var (manager, router) = managerAndRouter;
        var connections = router.GetAllConnections();

        return connections.Select(c => (IServiceConnection)new ServiceConnectionAdapter(c)).ToList();
    }

    /// <summary>
    /// 广播消息到指定服务类型的所有实例
    /// </summary>
    public async Task<Dictionary<string, bool>> BroadcastAsync<TRequest>(
        ServerType serverType,
        string hubName,
        string methodName,
        TRequest? request)
    {
        var connections = GetAllServers(serverType);
        var results = new Dictionary<string, bool>();

        _logger.LogInformation("广播消息到 {ServerType} ({Count} 个实例): {Method}",
            serverType, connections.Count, methodName);

        var tasks = connections.Select(async connection =>
        {
            try
            {
                await connection.InvokeAsync<TRequest, object>(hubName, methodName, request);
                return (connection.ServiceInfo.ServiceId, Success: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "广播失败: ServiceId={ServiceId}, Method={Method}",
                    connection.ServiceInfo.ServiceId, methodName);
                return (connection.ServiceInfo.ServiceId, Success: false);
            }
        });

        var taskResults = await Task.WhenAll(tasks);

        foreach (var (serviceId, success) in taskResults)
        {
            results[serviceId] = success;
        }

        return results;
    }

    /// <summary>
    /// 获取 Hub 代理（通过服务ID/分片键路由）
    /// </summary>
    /// <typeparam name="THub">Hub 接口类型</typeparam>
    /// <param name="serviceId">服务ID/分片键 (如 userId, roomId, matchId 等)，为空时使用自动路由</param>
    /// <returns>Hub 代理实例，可以直接调用 Hub 方法</returns>
    public THub? GetHub<THub>(string serviceId = "") where THub : class
    {
        EnsureInitialized();

        // 从 Hub 类型获取服务名称
        var serviceName = _hubTypeRegistry.GetServiceName<THub>();
        if (string.IsNullOrEmpty(serviceName))
        {
            _logger.LogError("无法找到 Hub 类型 {HubType} 对应的服务名称", typeof(THub).Name);
            return null;
        }

        // 将服务名称转换为 ServerType
        var serverType = GetServerTypeFromServiceName(serviceName);
        if (serverType == null)
        {
            _logger.LogError("无法将服务名称 {ServiceName} 转换为 ServerType", serviceName);
            return null;
        }

        // 如果 serviceId 为空，使用自动路由（基于 Hub 类型的哈希）
        if (string.IsNullOrEmpty(serviceId))
        {
            // 先尝试本地查询
            var localServiceId = TryGetLocalServiceId<THub>();
            if (!string.IsNullOrEmpty(localServiceId))
            {
                serviceId = localServiceId;
                _logger.LogDebug("使用本地服务: Hub={HubType}, ServiceId={ServiceId}",
                    typeof(THub).Name, serviceId);
            }
            else
            {
                // 使用 Hub 类型名作为默认路由键（确保同类型 Hub 路由到同一节点）
                serviceId = $"auto-{typeof(THub).FullName}";
                _logger.LogDebug("使用自动路由: Hub={HubType}, RoutingKey={RoutingKey}",
                    typeof(THub).Name, serviceId);
            }
        }

        // 获取服务连接
        var connection = GetServer(serverType.Value, serviceId);
        if (connection == null)
        {
            _logger.LogWarning("无法获取服务连接: Hub={HubType}, ServiceId={ServiceId}",
                typeof(THub).Name, serviceId);
            return null;
        }

        // ServiceConnectionAdapter 实现了 IRemoteInvoker 接口
        if (connection is not IRemoteInvoker invoker)
        {
            _logger.LogError("服务连接不支持 IRemoteInvoker 接口: Hub={HubType}", typeof(THub).Name);
            return null;
        }

        // 创建并返回 Hub 代理
        return HubProxy<THub>.Create(invoker);
    }

    /// <summary>
    /// 尝试从本地服务注册表获取 ServiceId
    /// </summary>
    private string? TryGetLocalServiceId<THub>() where THub : class
    {
        // 获取当前进程ID
        var currentPid = System.Environment.ProcessId;

        // 从本地注册表查询
        var serviceId = _localServiceRegistry.GetServiceId(currentPid);

        if (!string.IsNullOrEmpty(serviceId))
        {
            var metadata = _localServiceRegistry.GetMetadata(serviceId);

            // 只有启用一致性哈希的服务才能通过本地查询
            if (metadata?.EnableConsistentHash == true)
            {
                return serviceId;
            }
        }

        return null;
    }

    /// <summary>
    /// 获取 Hub 代理（通过节点ID路由到特定节点）
    /// </summary>
    /// <typeparam name="THub">Hub 接口类型</typeparam>
    /// <param name="nodeId">节点ID</param>
    /// <returns>Hub 代理实例，可以直接调用 Hub 方法</returns>
    public THub? GetHub<THub>(int nodeId) where THub : class
    {
        // 使用节点ID作为分片键
        var serviceId = $"node-{nodeId}";
        return GetHub<THub>(serviceId);
    }

    /// <summary>
    /// 注册本地服务（PID 与 ServiceId 的映射）
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="enableConsistentHash">是否启用一致性哈希</param>
    /// <param name="syncToConsul">是否同步到 Consul</param>
    public void RegisterLocalService(
        string serviceId,
        bool enableConsistentHash = true,
        bool syncToConsul = true)
    {
        var pid = System.Environment.ProcessId;

        var metadata = new ServiceMetadata
        {
            ServiceType = "Local",
            EnableConsistentHash = enableConsistentHash,
            SyncToConsul = syncToConsul,
            RegisteredAt = DateTime.UtcNow
        };

        _localServiceRegistry.Register(pid, serviceId, metadata);

        _logger.LogInformation(
            "本地服务已注册: PID={Pid}, ServiceId={ServiceId}, ConsistentHash={ConsistentHash}, SyncToConsul={SyncToConsul}",
            pid, serviceId, enableConsistentHash, syncToConsul);
    }

    /// <summary>
    /// 注销本地服务
    /// </summary>
    public void UnregisterLocalService(string serviceId)
    {
        _localServiceRegistry.Unregister(serviceId);
        _logger.LogInformation("本地服务已注销: ServiceId={ServiceId}", serviceId);
    }

    /// <summary>
    /// 将服务名称转换为 ServerType
    /// </summary>
    private ServerType? GetServerTypeFromServiceName(string serviceName)
    {
        return serviceName switch
        {
            "BackendServer" => ServerType.Backend,
            "BattleServer" => ServerType.Battle,
            "GameServer" => ServerType.Game,
            "ChatServer" => ServerType.Chat,
            "MailServer" => ServerType.Mail,
            _ => null
        };
    }

    /// <summary>
    /// 获取统计信息
    /// </summary>
    public UnifiedServiceClientStats GetStats()
    {
        var serverStats = new Dictionary<ServerType, ServerTypeStats>();

        foreach (var (serverType, (manager, router)) in _serverManagers)
        {
            var managerStats = manager.GetStats();
            var routerStats = router.GetStats();

            serverStats[serverType] = new ServerTypeStats
            {
                TotalConnections = managerStats.TotalConnections,
                ConnectedCount = managerStats.ConnectedCount,
                TotalRequests = managerStats.TotalRequests,
                RoutingStrategy = routerStats.Strategy
            };
        }

        return new UnifiedServiceClientStats
        {
            IsInitialized = _isInitialized,
            RegisteredServerTypes = _serverManagers.Keys.ToList(),
            ServerStats = serverStats
        };
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("UnifiedServiceClientManager 未初始化，请先调用 InitializeAsync");
        }

        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(UnifiedServiceClientManager));
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        _logger.LogInformation("正在释放 UnifiedServiceClientManager...");

        foreach (var (serverType, (manager, router)) in _serverManagers)
        {
            _logger.LogInformation("释放服务类型: {ServerType}", serverType);

            router.Dispose();
            await manager.DisposeAsync();
        }

        _serverManagers.Clear();

        _logger.LogInformation("UnifiedServiceClientManager 已释放");
    }
}

/// <summary>
/// 服务连接适配器 - 将 ServiceConnection 适配为 IServiceConnection / IRemoteInvoker
/// </summary>
internal class ServiceConnectionAdapter : IServiceConnection
{
    private readonly ServiceConnection _connection;

    public ServiceConnectionAdapter(ServiceConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public ServiceRegistration ServiceInfo => _connection.ServiceInfo;

    public long RequestCount => _connection.RequestCount;

    public bool IsConnected => _connection.State == ConnectionState.Connected;

    public Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string hubName,
        string methodName,
        TRequest? request,
        CancellationToken cancellationToken = default)
    {
        return _connection.InvokeAsync<TRequest, TResponse>(hubName, methodName, request, cancellationToken);
    }
}

/// <summary>
/// 统一服务客户端统计信息
/// </summary>
public class UnifiedServiceClientStats
{
    /// <summary>
    /// 是否已初始化
    /// </summary>
    public bool IsInitialized { get; init; }

    /// <summary>
    /// 已注册的服务类型
    /// </summary>
    public List<ServerType> RegisteredServerTypes { get; init; } = new();

    /// <summary>
    /// 各服务类型的统计信息
    /// </summary>
    public Dictionary<ServerType, ServerTypeStats> ServerStats { get; init; } = new();
}

/// <summary>
/// 服务类型统计信息
/// </summary>
public class ServerTypeStats
{
    /// <summary>
    /// 总连接数
    /// </summary>
    public int TotalConnections { get; init; }

    /// <summary>
    /// 已连接数
    /// </summary>
    public int ConnectedCount { get; init; }

    /// <summary>
    /// 总请求数
    /// </summary>
    public long TotalRequests { get; init; }

    /// <summary>
    /// 路由策略
    /// </summary>
    public RoutingStrategy RoutingStrategy { get; init; }
}
