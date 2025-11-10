using DistributedGameApp.Shared.Hubs;
using Microsoft.Extensions.Logging;
using PulseRPC;
using PulseRPC.Client;
using PulseRPC.Transport;

namespace DistributedGameApp.Client;

/// <summary>
/// 服务器连接管理器 - 使用单一 IPulseClient 管理所有连接
/// </summary>
public class ServerConnectionManager : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ServerConnectionManager> _logger;
    private readonly IPulseClient _client;
    private readonly Dictionary<string, ServerConnection> _connections = new();
    private string? _currentServerId;
    private bool _disposed;

    public ServerConnectionManager(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<ServerConnectionManager>();

        // 创建单一的 PulseClient 实例
        _client = new PulseClientBuilder()
            .WithLogging(loggerFactory)
            .Configure(options =>
            {
                options.Name = "DistributedGameClient";
                options.DefaultTimeout = TimeSpan.FromSeconds(30);
            })
            // 注意：不添加任何初始连接，完全动态管理
            // .AddConnection(ConnectionDescriptor.CreateTcp(
            //     "placeholder",
            //     "Placeholder",
            //     "127.0.0.1",
            //     1,
            //     ConnectionStrategy.Transient))
            .Build();

        // 初始化客户端
        _client.InitializeAsync().GetAwaiter().GetResult();

        // 断开占位符连接
        // _client.DisconnectAsync("placeholder").GetAwaiter().GetResult();

        _logger.LogInformation("ServerConnectionManager 已初始化，使用单一 IPulseClient");
    }

    /// <summary>
    /// 当前活动的服务器ID
    /// </summary>
    public string? CurrentServerId => _currentServerId;

    /// <summary>
    /// 获取当前活动的连接
    /// </summary>
    public ServerConnection? CurrentConnection =>
        _currentServerId != null && _connections.TryGetValue(_currentServerId, out var conn) ? conn : null;

    /// <summary>
    /// 获取所有连接
    /// </summary>
    public IReadOnlyDictionary<string, ServerConnection> AllConnections => _connections;

    /// <summary>
    /// 获取统一的 PulseClient 实例
    /// </summary>
    public IPulseClient Client => _client;

    /// <summary>
    /// 添加并连接到 GameServer
    /// </summary>
    public async Task<ServerConnection> ConnectToGameServerAsync(
        string serverId,
        string serverName,
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        if (_connections.TryGetValue(serverId, out var connection))
        {
            _logger.LogWarning("服务器 {ServerId} 已经连接", serverId);
            return connection;
        }

        _logger.LogInformation("正在连接到 GameServer: {ServerName} ({Host}:{Port})", serverName, host, port);

        // 使用新的 ConnectToServerAsync API
        var channel = await _client.ConnectToServerAsync(
            host,
            port,
            serverId,
            serverName,
            TransportType.TCP,
            ConnectionStrategy.Persistent,
            cancellationToken);

        // 使用简化 API 获取服务代理（具象方法将由源代码生成器生成）
        var gameHub = await _client.GetServiceAsync<IGameHub>(serverId, cancellationToken: cancellationToken);

        connection = new ServerConnection
        {
            ServerId = serverId,
            ServerName = serverName,
            ServerType = ServerType.GameServer,
            Host = host,
            Port = port,
            Channel = channel,
            GameHub = gameHub,
            IsConnected = true
        };

        _connections[serverId] = connection;
        _currentServerId ??= serverId; // 如果是第一个连接，设为当前连接

        _logger.LogInformation("成功连接到 GameServer: {ServerName}", serverName);
        return connection;
    }

    /// <summary>
    /// 添加并连接到 BattleServer
    /// </summary>
    public async Task<ServerConnection> ConnectToBattleServerAsync(
        string serverId,
        string battleId,
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        if (_connections.ContainsKey(serverId))
        {
            _logger.LogWarning("服务器 {ServerId} 已经连接", serverId);
            return _connections[serverId];
        }

        _logger.LogInformation("正在连接到 BattleServer: {BattleId} ({Host}:{Port})", battleId, host, port);

        // 使用新的 ConnectToServerAsync API
        var channel = await _client.ConnectToServerAsync(
            host,
            port,
            serverId,
            $"BattleServer-{battleId}",
            TransportType.TCP,
            ConnectionStrategy.Persistent,
            cancellationToken);

        // 使用简化 API 获取服务代理（具象方法将由源代码生成器生成）
        var battleHub = await _client.GetServiceAsync<IBattleHub>(serverId, cancellationToken: cancellationToken);

        var connection = new ServerConnection
        {
            ServerId = serverId,
            ServerName = $"BattleServer-{battleId}",
            ServerType = ServerType.BattleServer,
            Host = host,
            Port = port,
            Channel = channel,
            BattleHub = battleHub,
            IsConnected = true,
            BattleId = battleId
        };

        _connections[serverId] = connection;

        _logger.LogInformation("成功连接到 BattleServer: {BattleId}", battleId);
        return connection;
    }

    /// <summary>
    /// 切换当前活动的服务器
    /// </summary>
    public bool SwitchToServer(string serverId)
    {
        if (!_connections.ContainsKey(serverId))
        {
            _logger.LogWarning("服务器 {ServerId} 不存在", serverId);
            return false;
        }

        var connection = _connections[serverId];
        if (!connection.IsConnected)
        {
            _logger.LogWarning("服务器 {ServerId} 未连接", serverId);
            return false;
        }

        _currentServerId = serverId;
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

        connection.EventSubscription?.Dispose();
        await _client.DisconnectAsync(serverId);
        connection.IsConnected = false;

        _connections.Remove(serverId);

        // 如果断开的是当前服务器，切换到其他服务器
        if (_currentServerId == serverId)
        {
            _currentServerId = _connections.Keys.FirstOrDefault();
        }

        _logger.LogInformation("已断开服务器: {ServerName}", connection.ServerName);
    }

    /// <summary>
    /// 注册事件监听器到指定连接
    /// </summary>
    public async Task RegisterEventListenerAsync<T>(T eventHandler, string? serverId = null)
        where T : class, IPulseReceiver
    {
        var targetServerId = serverId ?? _currentServerId;
        if (targetServerId == null)
        {
            throw new InvalidOperationException("没有活动的服务器连接");
        }

        if (!_connections.TryGetValue(targetServerId, out var connection))
        {
            throw new InvalidOperationException($"服务器 {targetServerId} 不存在");
        }

        // 使用新的简化 API 注册事件监听器
        connection.EventSubscription?.Dispose();

        // 注意：这里需要判断 eventHandler 实现了哪些接口
        // 为了简化示例，暂时使用泛型方法
        connection.EventSubscription = await _client.RegisterEventListenerAsync(
            eventHandler, options: new EventListenerOptions() { Name = targetServerId});

        _logger.LogInformation("已注册事件监听器到服务器: {ServerName}", connection.ServerName);
    }

    /// <summary>
    /// 获取指定服务器的连接
    /// </summary>
    public ServerConnection? GetConnection(string serverId)
    {
        return _connections.TryGetValue(serverId, out var connection) ? connection : null;
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var connection in _connections.Values)
        {
            connection.EventSubscription?.Dispose();
        }

        _connections.Clear();
        _client?.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// 服务器连接信息
/// </summary>
public class ServerConnection
{
    public required string ServerId { get; init; }
    public required string ServerName { get; init; }
    public required ServerType ServerType { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required IClientChannel Channel { get; init; }
    public bool IsConnected { get; set; }
    public ISubscriptionToken? EventSubscription { get; set; }

    // GameServer 相关
    public IGameHub? GameHub { get; init; }

    // BattleServer 相关
    public IBattleHub? BattleHub { get; init; }
    public string? BattleId { get; init; }

    // 连接时间
    public DateTime ConnectedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 服务器类型
/// </summary>
public enum ServerType
{
    GameServer,
    BattleServer
}
