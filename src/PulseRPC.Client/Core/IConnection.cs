using PulseRPC.Transport;

namespace PulseRPC.Client.Core;

/// <summary>
/// 连接引用接口 - 表示一个具体的网络连接实例
/// 现在继承ISessionChannel，融入三层抽象架构的应用层
/// 提供客户端特有的业务功能，同时享有传输层和会话层的所有能力
/// </summary>
public interface IConnection : ISessionChannel
{
    /// <summary>
    /// 连接描述符 (业务层专有)
    /// </summary>
    ConnectionDescriptor Descriptor { get; }

    /// <summary>
    /// 当前健康状态 (业务层专有)
    /// </summary>
    ConnectionHealth Health { get; }

    /// <summary>
    /// 连接统计信息 (业务层专有)
    /// </summary>
    ConnectionStatistics Statistics { get; }

    /// <summary>
    /// 是否可用 (综合连接状态和健康状态)
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 获取服务代理 (业务层核心功能)
    /// </summary>
    /// <typeparam name="T">服务接口类型</typeparam>
    /// <param name="options">获取选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务代理实例</returns>
    Task<T> GetServiceAsync<T>(ServiceProxyOptions? options = null, CancellationToken cancellationToken = default)
        where T : class, IPulseHub;

    /// <summary>
    /// 注册事件监听器 (业务层核心功能)
    /// </summary>
    /// <typeparam name="T">监听器接口类型</typeparam>
    /// <param name="listener">监听器实例</param>
    /// <param name="options">注册选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>订阅令牌</returns>
    Task<ISubscriptionToken> RegisterEventListenerAsync<T>(T listener, EventListenerOptions? options = null, CancellationToken cancellationToken = default)
        where T : class, IPulseEventHandler;

    /// <summary>
    /// 执行健康检查 (业务层功能)
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康检查结果</returns>
    Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 手动触发重连 (业务层功能)
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>重连任务</returns>
    Task ReconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 健康状态变化事件 (业务层事件)
    /// </summary>
    event EventHandler<ConnectionHealthChangedEventArgs> HealthChanged;
}

/// <summary>
/// 连接工厂接口
/// </summary>
public interface IConnectionFactory
{
    /// <summary>
    /// 创建连接
    /// </summary>
    /// <param name="descriptor">连接描述符</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接实例</returns>
    Task<IConnection> CreateConnectionAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证连接描述符
    /// </summary>
    /// <param name="descriptor">连接描述符</param>
    /// <returns>验证结果</returns>
    ValidationResult ValidateDescriptor(ConnectionDescriptor descriptor);

    /// <summary>
    /// 支持的传输类型
    /// </summary>
    ISet<TransportType> SupportedTransports { get; }
}

/// <summary>
/// 连接注册表接口 - 管理所有连接的注册和查询
/// </summary>
public interface IConnectionRegistry
{
    /// <summary>
    /// 注册连接
    /// </summary>
    /// <param name="connection">连接实例</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>注册任务</returns>
    Task RegisterAsync(IConnection connection, CancellationToken cancellationToken = default);

    /// <summary>
    /// 注销连接
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>注销任务</returns>
    Task UnregisterAsync(string connectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据ID获取连接
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <returns>连接实例，如果不存在则返回null</returns>
    IConnection? GetConnection(string connectionId);

    /// <summary>
    /// 根据名称获取连接
    /// </summary>
    /// <param name="name">连接名称</param>
    /// <param name="predicate">额外过滤条件</param>
    /// <returns>连接实例列表</returns>
    IReadOnlyList<IConnection> GetConnectionsByName(string name, Func<IConnection, bool>? predicate = null);

    /// <summary>
    /// 根据标签查询连接
    /// </summary>
    /// <param name="tags">标签条件</param>
    /// <param name="predicate">额外过滤条件</param>
    /// <returns>连接实例列表</returns>
    IReadOnlyList<IConnection> GetConnectionsByTags(IReadOnlyDictionary<string, string> tags, Func<IConnection, bool>? predicate = null);

    /// <summary>
    /// 根据状态查询连接
    /// </summary>
    /// <param name="states">状态列表</param>
    /// <param name="predicate">额外过滤条件</param>
    /// <returns>连接实例列表</returns>
    IReadOnlyList<IConnection> GetConnectionsByState(ISet<ConnectionState> states, Func<IConnection, bool>? predicate = null);

    /// <summary>
    /// 获取所有连接
    /// </summary>
    /// <param name="predicate">过滤条件</param>
    /// <returns>连接实例列表</returns>
    IReadOnlyList<IConnection> GetAllConnections(Func<IConnection, bool>? predicate = null);

    /// <summary>
    /// 连接注册事件
    /// </summary>
    event EventHandler<ConnectionRegisteredEventArgs> ConnectionRegistered;

    /// <summary>
    /// 连接注销事件
    /// </summary>
    event EventHandler<ConnectionUnregisteredEventArgs> ConnectionUnregistered;
}

/// <summary>
/// 连接生命周期管理器接口
/// </summary>
public interface IConnectionLifecycleManager
{
    /// <summary>
    /// 连接到指定端点
    /// </summary>
    /// <param name="descriptor">连接描述符</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接实例</returns>
    Task<IConnection> ConnectAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开指定连接
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <param name="graceful">是否优雅断开</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>断开任务</returns>
    Task DisconnectAsync(string connectionId, bool graceful = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量断开连接
    /// </summary>
    /// <param name="predicate">断开条件</param>
    /// <param name="graceful">是否优雅断开</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>断开的连接数量</returns>
    Task<int> DisconnectAsync(Func<IConnection, bool> predicate, bool graceful = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// 重连指定连接
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>重连任务</returns>
    Task ReconnectAsync(string connectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理空闲连接
    /// </summary>
    /// <param name="maxIdleTime">最大空闲时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>清理的连接数量</returns>
    Task<int> CleanupIdleConnectionsAsync(TimeSpan maxIdleTime, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理过期连接
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>清理的连接数量</returns>
    Task<int> CleanupExpiredConnectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行连接健康检查
    /// </summary>
    /// <param name="connectionId">连接ID，为null时检查所有连接</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康检查结果</returns>
    Task<IReadOnlyList<HealthCheckResult>> PerformHealthChecksAsync(string? connectionId = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// 服务代理获取选项
/// </summary>
public sealed class ServiceProxyOptions
{
    /// <summary>
    /// 调用超时时间
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// 重试策略
    /// </summary>
    public RetryPolicy? RetryPolicy { get; set; }

    /// <summary>
    /// 自定义属性
    /// </summary>
    public IReadOnlyDictionary<string, object>? Properties { get; set; }
}

/// <summary>
/// 事件监听器注册选项
/// </summary>
public sealed class EventListenerOptions
{
    /// <summary>
    /// 事件过滤器
    /// </summary>
    public Func<object, bool>? EventFilter { get; set; }

    /// <summary>
    /// 错误处理策略
    /// </summary>
    public EventErrorHandlingStrategy ErrorHandling { get; set; } = EventErrorHandlingStrategy.Log;

    /// <summary>
    /// 自定义属性
    /// </summary>
    public IReadOnlyDictionary<string, object>? Properties { get; set; }
}

/// <summary>
/// 事件错误处理策略
/// </summary>
public enum EventErrorHandlingStrategy
{
    /// <summary>
    /// 忽略错误
    /// </summary>
    Ignore,

    /// <summary>
    /// 记录日志
    /// </summary>
    Log,

    /// <summary>
    /// 抛出异常
    /// </summary>
    Throw,

    /// <summary>
    /// 自动取消订阅
    /// </summary>
    Unsubscribe
}

/// <summary>
/// 健康检查结果
/// </summary>
public sealed class HealthCheckResult
{
    public string ConnectionId { get; set; } = string.Empty;
    public ConnectionHealth Health { get; set; }
    public TimeSpan ResponseTime { get; set; }
    public string? Message { get; set; }
    public Exception? Exception { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    public IReadOnlyDictionary<string, object>? Data { get; set; }
}

/// <summary>
/// 验证结果
/// </summary>
public sealed class ValidationResult
{
    public bool IsValid { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

/// <summary>
/// 连接注册事件参数
/// </summary>
public sealed class ConnectionRegisteredEventArgs : EventArgs
{
    public IConnection Connection { get; set; } = null!;
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 连接注销事件参数
/// </summary>
public sealed class ConnectionUnregisteredEventArgs : EventArgs
{
    public string ConnectionId { get; set; } = string.Empty;
    public ConnectionDescriptor Descriptor { get; set; } = null!;
    public DateTime UnregisteredAt { get; set; } = DateTime.UtcNow;
    public string? Reason { get; set; }
}
