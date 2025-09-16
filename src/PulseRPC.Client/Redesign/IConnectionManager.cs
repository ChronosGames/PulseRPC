using PulseRPC.Client.Core;
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
public class ConnectionConfig
{
    /// <summary>
    /// 连接名称/标识
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 服务名称（用于服务发现）
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// 直接指定的主机地址（优先于服务发现）
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// 端口号
    /// </summary>
    public int? Port { get; set; }

    /// <summary>
    /// 传输类型
    /// </summary>
    public TransportType Transport { get; set; } = TransportType.Tcp;

    /// <summary>
    /// 传输选项
    /// </summary>
    public TransportOptions? Options { get; set; }

    /// <summary>
    /// 连接生命周期
    /// </summary>
    public ConnectionLifetime Lifetime { get; set; } = ConnectionLifetime.Session;

    /// <summary>
    /// 自动重连
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// 连接超时时间
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 空闲超时时间（用于自动清理临时连接）
    /// </summary>
    public TimeSpan? IdleTimeout { get; set; }

    /// <summary>
    /// 标签（用于批量管理）
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();
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
    Invalid,
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
    public string ConnectionName { get; set; } = string.Empty;
    public ConnectionState OldState { get; set; } = ConnectionState.Invalid;
    public ConnectionState NewState { get; set; } = ConnectionState.Invalid;
    public Exception? Exception { get; set; }
}
