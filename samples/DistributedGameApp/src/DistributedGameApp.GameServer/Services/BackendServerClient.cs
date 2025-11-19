using DistributedGameApp.GameServer.Services.Backend;
using DistributedGameApp.Shared.Domain.Matchmaking;
using DistributedGameApp.Shared.Hubs;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.GameServer.Services;

/// <summary>
/// BackendServer 客户端 - 生产级实现
/// 支持从 Consul 动态发现、基于一致性哈希的分片路由、连接池管理
/// </summary>
public class BackendServerClient : IAsyncDisposable
{
    private readonly BackendServerConnectionManager _connectionManager;
    private readonly BackendServerRouter _router;
    private readonly ILogger<BackendServerClient> _logger;
    private bool _isInitialized;
    private bool _isDisposed;

    public BackendServerClient(
        BackendServerConnectionManager connectionManager,
        BackendServerRouter router,
        ILogger<BackendServerClient> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 初始化客户端
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            _logger.LogWarning("BackendServerClient 已经初始化");
            return;
        }

        try
        {
            _logger.LogInformation("正在初始化 BackendServerClient...");

            // 初始化连接管理器（自动从 Consul 发现并连接所有 BackendServer）
            await _connectionManager.InitializeAsync(cancellationToken);

            _isInitialized = true;

            var stats = _connectionManager.GetStats();
            var routerStats = _router.GetStats();

            _logger.LogInformation(
                "BackendServerClient 初始化完成 - 连接数: {Connected}/{Total}, 路由策略: {Strategy}, 虚拟节点: {VNodes}",
                stats.ConnectedCount, stats.TotalConnections, routerStats.Strategy, routerStats.VirtualNodesPerNode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BackendServerClient 初始化失败");
            throw;
        }
    }

    /// <summary>
    /// 开始匹配（使用一致性哈希路由）- 使用强类型 Hub 代理
    /// </summary>
    public async Task<MatchmakingResponse> StartMatchmakingAsync(MatchmakingRequest request)
    {
        EnsureInitialized();

        if (string.IsNullOrEmpty(request.PlayerId))
        {
            throw new ArgumentException("PlayerId cannot be null or empty", nameof(request));
        }

        try
        {
            // 使用 PlayerId 作为路由键，确保同一玩家的请求总是路由到同一个 BackendServer
            var connection = _router.GetConnection(request.PlayerId);

            if (connection == null)
            {
                _logger.LogError("无法获取 BackendServer 连接: PlayerId={PlayerId}", request.PlayerId);
                return new MatchmakingResponse
                {
                    Success = false,
                    Message = "BackendServer 不可用"
                };
            }

            _logger.LogInformation("调用 BackendServer 开始匹配: PlayerId={PlayerId}, ServiceId={ServiceId}",
                request.PlayerId, connection.ServiceInfo.ServiceId);

            // 使用 InvokeAsync 方法调用
            var response = await connection.InvokeAsync<MatchmakingRequest, MatchmakingResponse>(
                "BackendServer",
                nameof(IBackendHub.StartMatchmakingAsync),
                request);

            if (response.Success)
            {
                _logger.LogInformation("匹配请求已提交: PlayerId={PlayerId}, ServiceId={ServiceId}",
                    request.PlayerId, connection.ServiceInfo.ServiceId);
            }
            else
            {
                _logger.LogWarning("匹配请求失败: PlayerId={PlayerId}, Message={Message}",
                    request.PlayerId, response.Message);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "调用 BackendServer 匹配服务失败: PlayerId={PlayerId}", request.PlayerId);
            return new MatchmakingResponse
            {
                Success = false,
                Message = $"匹配服务异常: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// 取消匹配（需要路由到相同的 BackendServer）- 使用强类型 Hub 代理
    /// </summary>
    public async Task<bool> CancelMatchmakingAsync(string playerId)
    {
        EnsureInitialized();

        if (string.IsNullOrEmpty(playerId))
        {
            throw new ArgumentException("PlayerId cannot be null or empty", nameof(playerId));
        }

        try
        {
            // 使用相同的 PlayerId 路由，确保取消请求发送到相同的服务器
            var connection = _router.GetConnection(playerId);

            if (connection == null)
            {
                _logger.LogError("无法获取 BackendServer 连接: PlayerId={PlayerId}", playerId);
                return false;
            }

            _logger.LogInformation("调用 BackendServer 取消匹配: PlayerId={PlayerId}, ServiceId={ServiceId}",
                playerId, connection.ServiceInfo.ServiceId);

            // 使用 InvokeAsync 方法调用
            var result = await connection.InvokeAsync<object?, bool>(
                "BackendServer",
                nameof(IBackendHub.CancelMatchmakingAsync),
                null);

            if (result)
            {
                _logger.LogInformation("取消匹配成功: PlayerId={PlayerId}", playerId);
            }
            else
            {
                _logger.LogWarning("取消匹配失败: PlayerId={PlayerId}", playerId);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "调用 BackendServer 取消匹配失败: PlayerId={PlayerId}", playerId);
            return false;
        }
    }

    /// <summary>
    /// 广播消息到所有 BackendServer
    /// </summary>
    public async Task<Dictionary<string, bool>> BroadcastAsync<TRequest>(
        string hubName,
        string methodName,
        TRequest? request)
    {
        EnsureInitialized();

        var connections = _router.GetAllConnections();
        var results = new Dictionary<string, bool>();

        _logger.LogInformation("广播消息到 {Count} 个 BackendServer: {Method}", connections.Count, methodName);

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
    /// 获取客户端统计信息
    /// </summary>
    public BackendServerClientStats GetStats()
    {
        var managerStats = _connectionManager.GetStats();
        var routerStats = _router.GetStats();

        return new BackendServerClientStats
        {
            IsInitialized = _isInitialized,
            TotalConnections = managerStats.TotalConnections,
            ConnectedCount = managerStats.ConnectedCount,
            DisconnectedCount = managerStats.DisconnectedCount,
            TotalRequests = managerStats.TotalRequests,
            RoutingStrategy = routerStats.Strategy,
            VirtualNodesPerNode = routerStats.VirtualNodesPerNode
        };
    }

    /// <summary>
    /// 确保客户端已初始化
    /// </summary>
    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("BackendServerClient 未初始化，请先调用 InitializeAsync");
        }

        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(BackendServerClient));
        }
    }

    /// <summary>
    /// 检查连接状态
    /// </summary>
    public bool IsConnected => _isInitialized && !_isDisposed && _connectionManager.ConnectionCount > 0;

    /// <summary>
    /// 异步释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        _logger.LogInformation("正在释放 BackendServerClient...");

        _router.Dispose();
        await _connectionManager.DisposeAsync();

        _logger.LogInformation("BackendServerClient 已释放");
    }
}

/// <summary>
/// BackendServerClient 统计信息
/// </summary>
public class BackendServerClientStats
{
    /// <summary>
    /// 是否已初始化
    /// </summary>
    public bool IsInitialized { get; init; }

    /// <summary>
    /// 总连接数
    /// </summary>
    public int TotalConnections { get; init; }

    /// <summary>
    /// 已连接数
    /// </summary>
    public int ConnectedCount { get; init; }

    /// <summary>
    /// 断开连接数
    /// </summary>
    public int DisconnectedCount { get; init; }

    /// <summary>
    /// 总请求数
    /// </summary>
    public long TotalRequests { get; init; }

    /// <summary>
    /// 路由策略
    /// </summary>
    public RoutingStrategy RoutingStrategy { get; init; }

    /// <summary>
    /// 每个物理节点的虚拟节点数
    /// </summary>
    public int VirtualNodesPerNode { get; init; }
}
