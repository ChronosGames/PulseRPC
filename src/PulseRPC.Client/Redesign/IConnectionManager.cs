using PulseRPC.Transport;

namespace PulseRPC.Client.Redesign;

/// <summary>
/// 连接生命周期类型
/// </summary>
public enum ConnectionLifetime
{
    /// <summary>
    /// 永久连接 - 与客户端生命周期一致，通常用于核心服务
    /// </summary>
    Persistent,
    
    /// <summary>
    /// 会话连接 - 在特定会话期间保持，如地图、公会等
    /// </summary>
    Session,
    
    /// <summary>
    /// 临时连接 - 短期使用后即删除，如战斗、副本等
    /// </summary>
    Transient
}

/// <summary>
/// 连接配置
/// </summary>
public record ConnectionConfig
{
    /// <summary>
    /// 连接名称/标识
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// 服务名称（用于服务发现）
    /// </summary>
    public string? ServiceName { get; init; }
    
    /// <summary>
    /// 直接指定的主机地址（优先于服务发现）
    /// </summary>
    public string? Host { get; init; }
    
    /// <summary>
    /// 端口号
    /// </summary>
    public int? Port { get; init; }
    
    /// <summary>
    /// 传输类型
    /// </summary>
    public TransportType Transport { get; init; } = TransportType.Tcp;
    
    /// <summary>
    /// 传输选项
    /// </summary>
    public TransportOptions? Options { get; init; }
    
    /// <summary>
    /// 连接生命周期
    /// </summary>
    public ConnectionLifetime Lifetime { get; init; } = ConnectionLifetime.Session;
    
    /// <summary>
    /// 自动重连
    /// </summary>
    public bool AutoReconnect { get; init; } = true;
    
    /// <summary>
    /// 连接超时时间
    /// </summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    
    /// <summary>
    /// 空闲超时时间（用于自动清理临时连接）
    /// </summary>
    public TimeSpan? IdleTimeout { get; init; }
    
    /// <summary>
    /// 标签（用于批量管理）
    /// </summary>
    public Dictionary<string, string> Tags { get; init; } = new();
}

/// <summary>
/// 连接上下文 - 提供对特定连接的访问
/// </summary>
public interface IConnectionContext : IDisposable
{
    /// <summary>
    /// 连接名称
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// 连接配置
    /// </summary>
    ConnectionConfig Config { get; }
    
    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }
    
    /// <summary>
    /// 连接状态
    /// </summary>
    ConnectionState State { get; }
    
    /// <summary>
    /// 最后使用时间
    /// </summary>
    DateTime LastUsedAt { get; }
    
    /// <summary>
    /// 获取服务代理
    /// </summary>
    Task<T> GetServiceAsync<T>(CancellationToken cancellationToken = default) 
        where T : class, IPulseService;
    
    /// <summary>
    /// 注册事件监听器
    /// </summary>
    Task<ISubscriptionToken> RegisterEventListenerAsync<T>(T listener, CancellationToken cancellationToken = default) 
        where T : class, IPulseEventHandler;
    
    /// <summary>
    /// 连接状态变化事件
    /// </summary>
    event EventHandler<ConnectionStateChangedEventArgs> StateChanged;
}

/// <summary>
/// 统一连接管理器接口
/// </summary>
public interface IConnectionManager : IDisposable
{
    /// <summary>
    /// 连接到服务器
    /// </summary>
    Task<IConnectionContext> ConnectAsync(ConnectionConfig config, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取现有连接
    /// </summary>
    IConnectionContext? GetConnection(string name);
    
    /// <summary>
    /// 获取或创建连接
    /// </summary>
    Task<IConnectionContext> GetOrCreateConnectionAsync(ConnectionConfig config, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 断开连接
    /// </summary>
    Task DisconnectAsync(string name, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 断开多个连接
    /// </summary>
    Task DisconnectAsync(Func<IConnectionContext, bool> predicate, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取所有连接
    /// </summary>
    IReadOnlyList<IConnectionContext> GetAllConnections();
    
    /// <summary>
    /// 根据标签查找连接
    /// </summary>
    IReadOnlyList<IConnectionContext> GetConnectionsByTag(string key, string? value = null);
    
    /// <summary>
    /// 清理空闲连接
    /// </summary>
    Task<int> CleanupIdleConnectionsAsync(TimeSpan? maxAge = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 连接状态变化事件
    /// </summary>
    event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;
}

/// <summary>
/// 连接状态
/// </summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Failed
}

/// <summary>
/// 连接状态变化事件参数
/// </summary>
public class ConnectionStateChangedEventArgs : EventArgs
{
    public required string ConnectionName { get; init; }
    public required ConnectionState OldState { get; init; }
    public required ConnectionState NewState { get; init; }
    public Exception? Exception { get; init; }
}