using DistributedGameApp.Shared.Hubs;
using DistributedGameApp.Shared.Messages;
using Microsoft.Extensions.Logging;
using PulseRPC;
using PulseRPC.Client;
using PulseRPC.Transport;
using PulseRPC.Channels;
using PulseRPC.Client.Configuration;

namespace DistributedGameApp.Client;

/// <summary>
/// 通用服务器连接管理器 - 使用单一 IPulseClient 管理所有服务器连接
/// </summary>
public class ServerConnectionManager : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ServerConnectionManager> _logger;
    private readonly IPulseClient _client;
    private readonly Dictionary<string, ServerConnection> _connections = new();
    private bool _disposed;

    public ServerConnectionManager(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<ServerConnectionManager>();

        // 使用预设配置创建 PulseClient 实例
        _client = new PulseClientBuilder()
            .UseGameClientPreset()              // 使用游戏客户端预设（低延迟优化）
            .WithLogging(loggerFactory)
            .Configure(options => options.Name = "DistributedGameClient")
            .Build();

        // 初始化客户端
        _client.InitializeAsync().GetAwaiter().GetResult();

        _logger.LogInformation("ServerConnectionManager 已初始化，使用单一 IPulseClient");
    }

    /// <summary>
    /// 获取所有连接
    /// </summary>
    public IReadOnlyDictionary<string, ServerConnection> AllConnections => _connections;

    /// <summary>
    /// 获取统一的 PulseClient 实例
    /// </summary>
    public IPulseClient Client => _client;

    /// <summary>
    /// 连接到指定服务器（通用方法）
    /// </summary>
    /// <param name="serverId">服务器唯一标识</param>
    /// <param name="serverName">服务器名称</param>
    /// <param name="host">服务器主机地址</param>
    /// <param name="port">服务器端口</param>
    /// <param name="jwtToken">JWT Access Token（用于 Connection-level 认证）</param>
    /// <param name="transportType">传输协议类型（默认 TCP）</param>
    /// <param name="connectionStrategy">连接策略（默认持久连接）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务器连接信息</returns>
    public async Task<ServerConnection> ConnectToServerAsync(
        string serverId,
        string serverName,
        string host,
        int port,
        string? jwtToken = null,
        TransportType transportType = TransportType.TCP,
        ConnectionStrategy connectionStrategy = ConnectionStrategy.Persistent,
        CancellationToken cancellationToken = default)
    {
        if (_connections.TryGetValue(serverId, out var existingConnection))
        {
            _logger.LogWarning("服务器 {ServerId} ({ServerName}) 已经连接", serverId, serverName);
            return existingConnection;
        }

        _logger.LogInformation("正在连接到服务器: {ServerName} ({Host}:{Port})", serverName, host, port);

        // 使用 ConnectToServerAsync API 建立连接
        var channel = await _client.ConnectToServerAsync(
            host,
            port,
            serverId,
            serverName,
            transportType,
            connectionStrategy,
            cancellationToken);

        var connection = new ServerConnection
        {
            ServerId = serverId,
            ServerName = serverName,
            Host = host,
            Port = port,
            Channel = channel,
            IsConnected = true
        };

        _connections[serverId] = connection;

        // ✅ Connection-level 认证（如果提供了 JWT Token）
        if (!string.IsNullOrEmpty(jwtToken))
        {
            _logger.LogInformation("正在进行 Connection-level 认证: {ServerName}", serverName);

            try
            {
                // 获取 ConnectionAuthenticationService 代理
                var authService = await _client.GetServiceAsync<IConnectionAuthenticationService>(
                    serverId, cancellationToken: cancellationToken);

                if (authService == null)
                {
                    _logger.LogError("无法获取 ConnectionAuthenticationService，认证失败");
                    throw new InvalidOperationException("Unable to get ConnectionAuthenticationService");
                }

                // 发送认证请求
                var authRequest = new ConnectionAuthRequest
                {
                    AccessToken = jwtToken,
                    Platform = "Windows", // 可以从环境变量获取
                    ClientVersion = "1.0.0" // 可以从程序集版本获取
                };

                var authResponse = await authService.AuthenticateAsync(authRequest);

                if (!authResponse.Success)
                {
                    _logger.LogError(
                        "Connection 认证失败 - ErrorCode: {ErrorCode}, Message: {ErrorMessage}",
                        authResponse.ErrorCode, authResponse.ErrorMessage);

                    // 认证失败，断开连接
                    await DisconnectServerAsync(serverId);

                    throw new InvalidOperationException(
                        $"Authentication failed: {authResponse.ErrorMessage} (Code: {authResponse.ErrorCode})");
                }

                _logger.LogInformation(
                    "Connection 认证成功 - UserId: {UserId}, Username: {Username}, Roles: {Roles}",
                    authResponse.UserId, authResponse.Username, string.Join(", ", authResponse.Roles));

                // 保存认证信息到连接对象
                connection.UserId = authResponse.UserId;
                connection.Username = authResponse.Username;
                connection.Roles = authResponse.Roles;
                connection.Permissions = authResponse.Permissions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection 认证过程中发生异常");
                await DisconnectServerAsync(serverId);
                throw;
            }
        }

        _logger.LogInformation("成功连接到服务器: {ServerName}", serverName);
        return connection;
    }

    /// <summary>
    /// 获取指定服务器的服务代理（Hub）
    /// </summary>
    /// <typeparam name="THub">服务接口类型</typeparam>
    /// <param name="targetServerId">服务器ID，如果为空则使用当前活动的服务器</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务代理实例</returns>
    public async ValueTask<THub?> GetHubAsync<THub>(
        string targetServerId,
        CancellationToken cancellationToken = default)
        where THub : class, IPulseHub
    {
        if (!_connections.TryGetValue(targetServerId, out var connection))
        {
            throw new InvalidOperationException($"服务器 {targetServerId} 未连接");
        }

        if (!connection.IsConnected)
        {
            throw new InvalidOperationException($"服务器 {targetServerId} 已断开连接");
        }

        return await _client.GetServiceAsync<THub>(targetServerId, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 切换当前活动的服务器
    /// </summary>
    public bool SwitchToServer(string serverId)
    {
        if (!_connections.TryGetValue(serverId, out var connection))
        {
            _logger.LogWarning("服务器 {ServerId} 不存在", serverId);
            return false;
        }

        if (!connection.IsConnected)
        {
            _logger.LogWarning("服务器 {ServerId} 未连接", serverId);
            return false;
        }

        _logger.LogInformation("切换到服务器: {ServerName}", connection.ServerName);
        return true;
    }

    /// <summary>
    /// 断开指定服务器连接
    /// </summary>
    public async Task DisconnectServerAsync(string serverId)
    {
        if (!_connections.TryGetValue(serverId, out var connection))
        {
            _logger.LogWarning("服务器 {ServerId} 不存在", serverId);
            return;
        }

        _logger.LogInformation("正在断开服务器: {ServerName}", connection.ServerName);

        // 清理所有订阅
        foreach (var token in connection.EventSubscriptions)
        {
            token.Dispose();
        }
        connection.EventSubscriptions.Clear();

        await _client.DisconnectAsync(serverId);
        connection.IsConnected = false;

        _connections.Remove(serverId);

        _logger.LogInformation("已断开服务器: {ServerName}", connection.ServerName);
    }

    /// <summary>
    /// 获取指定服务器的客户端通道
    /// </summary>
    public IClientChannel GetClientChannel(string serverId)
    {
        if (!_connections.TryGetValue(serverId, out var connection))
        {
            throw new InvalidOperationException($"服务器 {serverId} 不存在");
        }
        return connection.Channel;
    }

    /// <summary>
    /// 自动注册对象实现的所有 IPulseReceiver 接口
    /// </summary>
    /// <typeparam name="T">实现了一个或多个 IPulseReceiver 接口的类型</typeparam>
    /// <param name="serverId">服务器ID</param>
    /// <param name="receiver">接收器实现对象</param>
    /// <remarks>
    /// <para>
    /// 自动检测并注册 receiver 实现的所有 IPulseReceiver 接口（无反射，编译时生成）。
    /// </para>
    /// <code>
    /// // GameEventHandler 实现了 IPlayerReceiver, IChatRoomReceiver 等多个接口
    /// var handler = new GameEventHandler(this, logger);
    ///
    /// // 一行代码自动注册所有实现的 IPulseReceiver 接口
    /// _connectionManager.RegisterReceivers(serverId, handler);
    /// </code>
    /// </remarks>
    public void RegisterReceivers<T>(string serverId, T receiver) where T : class, IPulseReceiver
    {
        if (!_connections.TryGetValue(serverId, out var connection))
        {
            throw new InvalidOperationException($"服务器 {serverId} 不存在");
        }

        // 调用源代码生成的 RegisterAllReceivers 扩展方法
        // 自动注册 receiver 实现的所有 IPulseReceiver 接口
        var tokens = connection.Channel.RegisterReceiver(receiver);
        connection.EventSubscriptions.AddRange(tokens);

        _logger.LogInformation("已注册 {Count} 个接收器到服务器: {ServerName}",
            tokens, connection.ServerName);
    }

    /// <summary>
    /// 清除指定服务器的所有订阅
    /// </summary>
    public void ClearSubscriptions(string serverId)
    {
        if (_connections.TryGetValue(serverId, out var connection))
        {
            foreach (var token in connection.EventSubscriptions)
            {
                token.Dispose();
            }
            connection.EventSubscriptions.Clear();

            _logger.LogDebug("已清除服务器 {ServerName} 的所有订阅", connection.ServerName);
        }
    }

    /// <summary>
    /// 获取指定服务器的连接
    /// </summary>
    public ServerConnection? GetConnection(string serverId)
    {
        return _connections.GetValueOrDefault(serverId);
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var connection in _connections.Values)
        {
            foreach (var token in connection.EventSubscriptions)
            {
                token.Dispose();
            }
        }

        _connections.Clear();
        _client.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// 服务器连接信息
/// </summary>
public class ServerConnection
{
    /// <summary>
    /// 服务器唯一标识
    /// </summary>
    public required string ServerId { get; init; }

    /// <summary>
    /// 服务器名称
    /// </summary>
    public required string ServerName { get; init; }

    /// <summary>
    /// 服务器主机地址
    /// </summary>
    public required string Host { get; init; }

    /// <summary>
    /// 服务器端口
    /// </summary>
    public required int Port { get; init; }

    /// <summary>
    /// 客户端通道
    /// </summary>
    public required IClientChannel Channel { get; init; }

    /// <summary>
    /// 连接状态
    /// </summary>
    public bool IsConnected { get; set; }

    /// <summary>
    /// 事件订阅令牌列表（支持多个 IPulseReceiver 接口）
    /// </summary>
    public List<ISubscriptionToken> EventSubscriptions { get; } = new();

    /// <summary>
    /// 连接建立时间
    /// </summary>
    public DateTime ConnectedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 用于存储额外的元数据信息（如 BattleId、GuildId 等）
    /// </summary>
    public Dictionary<string, object> Metadata { get; } = new();

    // ========== Connection-level 认证信息 ==========

    /// <summary>
    /// 用户ID（Connection 认证成功后设置）
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 用户名（Connection 认证成功后设置）
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// 用户角色列表（Connection 认证成功后设置）
    /// </summary>
    public string[]? Roles { get; set; }

    /// <summary>
    /// 用户权限列表（Connection 认证成功后设置）
    /// </summary>
    public string[]? Permissions { get; set; }

    /// <summary>
    /// 是否已完成 Connection 认证
    /// </summary>
    public bool IsAuthenticated => !string.IsNullOrEmpty(UserId);
}
