using DistributedGameApp.BackendServer.Services.Battle;
using DistributedGameApp.Infrastructure.Consul;
using DistributedGameApp.Infrastructure.ServiceClient;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using BattleRoutingStrategy = DistributedGameApp.BackendServer.Services.Battle.RoutingStrategy;

namespace DistributedGameApp.BackendServer.Services;

/// <summary>
/// 统一服务客户端管理器 - BackendServer 专用版本
/// </summary>
/// <remarks>
/// BackendServer 主要需要连接 BattleServer 来创建战斗房间
/// 未来可扩展支持连接其他服务类型
/// </remarks>
public class UnifiedServiceClientManager : IAsyncDisposable
{
    private readonly ConsulServiceDiscovery _serviceDiscovery;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<UnifiedServiceClientManager> _logger;
    private readonly HubTypeRegistry _hubTypeRegistry;

    // BattleServer 连接管理器和路由器
    private BattleServerConnectionManager? _battleConnectionManager;
    private BattleServerRouter? _battleRouter;

    private bool _isInitialized;
    private bool _isDisposed;

    public UnifiedServiceClientManager(
        ConsulServiceDiscovery serviceDiscovery,
        ILoggerFactory loggerFactory)
    {
        _serviceDiscovery = serviceDiscovery ?? throw new ArgumentNullException(nameof(serviceDiscovery));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
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
    /// 初始化 BattleServer 连接
    /// </summary>
    public async Task InitializeAsync(
        BattleRoutingStrategy strategy = BattleRoutingStrategy.ConsistentHash,
        CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            _logger.LogWarning("UnifiedServiceClientManager 已经初始化");
            return;
        }

        _logger.LogInformation("初始化 UnifiedServiceClientManager (BattleServer)");

        // 创建 BattleServer 连接管理器
        _battleConnectionManager = new BattleServerConnectionManager(
            _serviceDiscovery,
            _loggerFactory,
            "BattleServer");

        // 创建 BattleServer 路由器
        var routerLogger = _loggerFactory.CreateLogger<BattleServerRouter>();
        _battleRouter = new BattleServerRouter(_battleConnectionManager, routerLogger, strategy);

        // 初始化连接管理器
        await _battleConnectionManager.InitializeAsync(cancellationToken);

        _isInitialized = true;
        _logger.LogInformation("UnifiedServiceClientManager 初始化完成, BattleServer 连接数: {Count}",
            _battleConnectionManager.ConnectionCount);
    }

    /// <summary>
    /// 获取 Hub 代理 (通过房间ID/匹配ID路由)
    /// </summary>
    /// <typeparam name="THub">Hub 接口类型</typeparam>
    /// <param name="routingKey">路由键 (如 roomId, matchId 等), 为空时使用第一个可用连接</param>
    /// <returns>Hub 代理实例, 可以直接调用 Hub 方法</returns>
    public THub? GetHub<THub>(string routingKey = "") where THub : class
    {
        EnsureInitialized();

        // 从 Hub 类型获取服务名称
        var serviceName = _hubTypeRegistry.GetServiceName<THub>();
        if (string.IsNullOrEmpty(serviceName))
        {
            _logger.LogError("无法找到 Hub 类型 {HubType} 对应的服务名称", typeof(THub).Name);
            return null;
        }

        // 目前只支持 BattleServer
        if (serviceName != "BattleServer" || _battleRouter == null)
        {
            _logger.LogError("当前仅支持 BattleHub, 请求的服务: {ServiceName}", serviceName);
            return null;
        }

        // 如果 routingKey 为空, 使用类型名作为路由键
        if (string.IsNullOrEmpty(routingKey))
        {
            routingKey = $"auto-{typeof(THub).FullName}";
            _logger.LogDebug("使用自动路由: Hub={HubType}, RoutingKey={RoutingKey}",
                typeof(THub).Name, routingKey);
        }

        // 获取 BattleServer 连接
        var connection = _battleRouter.GetConnection(routingKey);
        if (connection == null)
        {
            _logger.LogWarning("无法获取 BattleServer 连接: Hub={HubType}, RoutingKey={RoutingKey}",
                typeof(THub).Name, routingKey);
            return null;
        }

        // 创建连接适配器
        var adapter = new BattleServerConnectionAdapter(connection);

        // 创建并返回 Hub 代理
        return HubProxy<THub>.Create(adapter);
    }

    /// <summary>
    /// 获取统计信息
    /// </summary>
    public ServiceClientStats GetStats()
    {
        var stats = new ServiceClientStats
        {
            IsInitialized = _isInitialized
        };

        if (_battleConnectionManager != null)
        {
            var managerStats = _battleConnectionManager.GetStats();
            stats.BattleServerStats = new ServerStats
            {
                TotalConnections = managerStats.TotalConnections,
                ConnectedCount = managerStats.ConnectedCount,
                TotalRequests = managerStats.TotalRequests
            };
        }

        return stats;
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("UnifiedServiceClientManager 未初始化, 请先调用 InitializeAsync");
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

        if (_battleRouter != null)
        {
            _battleRouter.Dispose();
            _battleRouter = null;
        }

        if (_battleConnectionManager != null)
        {
            await _battleConnectionManager.DisposeAsync();
            _battleConnectionManager = null;
        }

        _logger.LogInformation("UnifiedServiceClientManager 已释放");
    }
}

/// <summary>
/// BattleServer 连接适配器 - 将 BattleServerConnection 适配为 IRemoteInvoker
/// </summary>
internal class BattleServerConnectionAdapter : IRemoteInvoker
{
    private readonly BattleServerConnection _connection;

    public BattleServerConnectionAdapter(BattleServerConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

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
/// 服务客户端统计信息
/// </summary>
public class ServiceClientStats
{
    /// <summary>
    /// 是否已初始化
    /// </summary>
    public bool IsInitialized { get; init; }

    /// <summary>
    /// BattleServer 统计信息
    /// </summary>
    public ServerStats? BattleServerStats { get; set; }
}

/// <summary>
/// 服务器统计信息
/// </summary>
public class ServerStats
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
}
