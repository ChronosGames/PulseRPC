using Microsoft.Extensions.Logging;
using PulseRPC.Authentication;
using PulseRPC.Client.Core;
using PulseRPC.Transport;

namespace PulseRPC.Client.Core;

/// <summary>
/// PulseRPC 客户端核心接口 - 基于 UsageExamples.cs 设计
/// </summary>
public interface IPulseRPCClient : IDisposable
{
    /// <summary>
    /// 连接管理器
    /// </summary>
    IConnectionManager Connections { get; }

    /// <summary>
    /// 连接路由器
    /// </summary>
    IConnectionRouter Router { get; }

    /// <summary>
    /// 服务发现
    /// </summary>
    IServiceDiscovery ServiceDiscovery { get; }

    /// <summary>
    /// 连接注册表
    /// </summary>
    IConnectionRegistry Registry { get; }

    /// <summary>
    /// 连接生命周期管理器
    /// </summary>
    IConnectionLifecycleManager Lifecycle { get; }

    /// <summary>
    /// 负载均衡器
    /// </summary>
    ILoadBalancer LoadBalancer { get; }

    /// <summary>
    /// 客户端状态
    /// </summary>
    ClientState State { get; }

    /// <summary>
    /// 初始化客户端
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止客户端
    /// </summary>
    Task StopAsync(bool graceful = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 连接到服务
    /// </summary>
    Task<IConnection> ConnectAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default);

    /// <summary>
    /// 通过服务发现连接到服务
    /// </summary>
    Task<IConnection> ConnectToServiceAsync(string serviceName, ServiceConnectionOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取服务代理（自动路由到最佳连接）
    /// </summary>
    Task<T> GetServiceAsync<T>(ServiceProxyOptions? options = null, CancellationToken cancellationToken = default)
        where T : class, IPulseHub;

    /// <summary>
    /// 获取指定连接的服务代理
    /// </summary>
    Task<T> GetServiceAsync<T>(string connectionId, ServiceProxyOptions? options = null, CancellationToken cancellationToken = default)
        where T : class, IPulseHub;

    /// <summary>
    /// 注册事件监听器（自动路由到最佳连接）
    /// </summary>
    Task<ISubscriptionToken> RegisterEventListenerAsync<T>(T listener, EventListenerOptions? options = null, CancellationToken cancellationToken = default)
        where T : class, IPulseEventHandler;

    /// <summary>
    /// 在指定连接上注册事件监听器
    /// </summary>
    Task<ISubscriptionToken> RegisterEventListenerAsync<T>(string connectionId, T listener, EventListenerOptions? options = null, CancellationToken cancellationToken = default)
        where T : class, IPulseEventHandler;

    /// <summary>
    /// 断开连接
    /// </summary>
    Task DisconnectAsync(string connectionId, bool graceful = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量断开连接
    /// </summary>
    Task<int> DisconnectAsync(Func<IConnection, bool> predicate, bool graceful = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取客户端统计信息
    /// </summary>
    ClientStatistics GetStatistics();

    /// <summary>
    /// 执行健康检查
    /// </summary>
    Task<ClientHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 客户端状态变化事件
    /// </summary>
    event EventHandler<ClientStateChangedEventArgs> StateChanged;
}

/// <summary>
/// 连接管理器接口 - 管理所有连接
/// </summary>
public interface IConnectionManager : IDisposable
{
    /// <summary>
    /// 连接到服务
    /// </summary>
    Task<IConnectionContext> ConnectAsync(ConnectionConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// 通过描述符连接
    /// </summary>
    Task<IConnectionContext> ConnectAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开连接
    /// </summary>
    Task DisconnectAsync(string connectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量断开连接
    /// </summary>
    Task DisconnectAsync(Func<IConnectionContext, bool> predicate, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取连接
    /// </summary>
    IConnectionContext? GetConnection(string connectionId);

    /// <summary>
    /// 获取所有连接
    /// </summary>
    IReadOnlyList<IConnectionContext> GetAllConnections();

    /// <summary>
    /// 连接数量
    /// </summary>
    int Count { get; }
}

/// <summary>
/// 连接上下文接口 - 表示一个活跃的连接
/// </summary>
public interface IConnectionContext : IDisposable
{
    /// <summary>
    /// 连接ID
    /// </summary>
    string Id { get; }

    /// <summary>
    /// 连接配置
    /// </summary>
    ConnectionConfig Config { get; }

    /// <summary>
    /// 连接描述符
    /// </summary>
    ConnectionDescriptor Descriptor { get; }

    /// <summary>
    /// 连接状态
    /// </summary>
    ExtendedConnectionState State { get; }

    /// <summary>
    /// 连接统计信息
    /// </summary>
    ConnectionStatistics Statistics { get; }

    /// <summary>
    /// 获取服务代理
    /// </summary>
    Task<T> GetServiceAsync<T>() where T : class, IPulseHub;

    /// <summary>
    /// 注册事件监听器
    /// </summary>
    Task<ISubscriptionToken> RegisterEventListenerAsync<T>(T listener) where T : class, IPulseEventHandler;

    /// <summary>
    /// 连接状态变化事件
    /// </summary>
    event EventHandler<ConnectionStateChangedEventArgs> StateChanged;
}

/// <summary>
/// 连接接口 - 更轻量级的连接表示
/// </summary>
public interface IConnection : IDisposable
{
    /// <summary>
    /// 连接ID
    /// </summary>
    string Id { get; }

    /// <summary>
    /// 连接描述符
    /// </summary>
    ConnectionDescriptor Descriptor { get; }

    /// <summary>
    /// 连接状态
    /// </summary>
    ExtendedConnectionState State { get; }

    /// <summary>
    /// 获取服务代理
    /// </summary>
    Task<T> GetServiceAsync<T>() where T : class, IPulseHub;
}

/// <summary>
/// 连接路由器接口 - 智能路由功能
/// </summary>
public interface IConnectionRouter
{
    /// <summary>
    /// 注册路由规则
    /// </summary>
    void RegisterRule(RoutingRule rule);

    /// <summary>
    /// 移除路由规则
    /// </summary>
    bool RemoveRule(string ruleId);

    /// <summary>
    /// 路由到最佳连接
    /// </summary>
    Task<IConnection> RouteAsync(string routingKey, RoutingContext? context = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有匹配的连接
    /// </summary>
    IReadOnlyList<IConnection> GetMatchingConnections(string routingKey, RoutingContext? context = null);
}

/// <summary>
/// 服务发现接口
/// </summary>
public interface IServiceDiscovery : IDisposable
{
    /// <summary>
    /// 发现服务实例
    /// </summary>
    Task<IReadOnlyList<ServiceEndpoint>> DiscoverAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 监听服务变化
    /// </summary>
    Task<IServiceWatcher> WatchAsync(string serviceName, Action<ServiceChangeEvent> callback, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有服务
    /// </summary>
    Task<IReadOnlyList<string>> GetServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查服务是否存在
    /// </summary>
    Task<bool> ExistsAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 刷新服务缓存
    /// </summary>
    Task RefreshAsync(string? serviceName = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// 服务监听器接口
/// </summary>
public interface IServiceWatcher : IDisposable
{
    /// <summary>
    /// 停止监听
    /// </summary>
    Task StopAsync();
}

/// <summary>
/// 连接注册表接口
/// </summary>
public interface IConnectionRegistry
{
    /// <summary>
    /// 注册连接
    /// </summary>
    void RegisterConnection(IConnection connection);

    /// <summary>
    /// 注销连接
    /// </summary>
    void UnregisterConnection(string connectionId, string reason = "手动注销");

    /// <summary>
    /// 根据标签获取连接
    /// </summary>
    IReadOnlyList<IConnection> GetConnectionsByTags(Dictionary<string, string> tags);

    /// <summary>
    /// 获取连接
    /// </summary>
    IConnection? GetConnection(string connectionId);

    /// <summary>
    /// 获取所有连接
    /// </summary>
    IReadOnlyList<IConnection> GetAllConnections();

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
    /// 执行健康检查
    /// </summary>
    Task<IReadOnlyList<HealthCheckResult>> PerformHealthChecksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理空闲连接
    /// </summary>
    Task<int> CleanupIdleConnectionsAsync(TimeSpan idleTimeout, CancellationToken cancellationToken = default);
}

/// <summary>
/// 负载均衡器接口
/// </summary>
public interface ILoadBalancer
{
    /// <summary>
    /// 选择最佳连接
    /// </summary>
    IConnection? SelectConnection(IReadOnlyList<IConnection> connections, LoadBalancingHint hint = LoadBalancingHint.None);

    /// <summary>
    /// 负载均衡策略
    /// </summary>
    LoadBalancingStrategy Strategy { get; }
}

/// <summary>
/// 路由规则
/// </summary>
public sealed class RoutingRule
{
    /// <summary>
    /// 规则ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 规则名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 匹配条件
    /// </summary>
    public Func<string, RoutingContext?, bool> Matcher { get; set; } = (_, _) => false;

    /// <summary>
    /// 连接选择器
    /// </summary>
    public Func<IReadOnlyList<IConnection>, RoutingContext?, IConnection?> Selector { get; set; } = (connections, _) => connections.FirstOrDefault();

    /// <summary>
    /// 规则优先级（数值越大优先级越高）
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// 路由上下文
/// </summary>
public sealed class RoutingContext
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 上下文标签
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>
    /// 偏好区域
    /// </summary>
    public string? PreferredRegion { get; set; }

    /// <summary>
    /// 负载均衡提示
    /// </summary>
    public LoadBalancingHint LoadBalancingHint { get; set; } = LoadBalancingHint.None;

    /// <summary>
    /// 额外属性
    /// </summary>
    public Dictionary<string, object> Properties { get; set; } = new();
}

/// <summary>
/// 服务端点
/// </summary>
public sealed class ServiceEndpoint
{
    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// 端点地址
    /// </summary>
    public EndpointAddress Address { get; set; } = new();

    /// <summary>
    /// 传输类型
    /// </summary>
    public TransportType Transport { get; set; } = TransportType.Tcp;

    /// <summary>
    /// 权重
    /// </summary>
    public int Weight { get; set; } = 1;

    /// <summary>
    /// 标签
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>
    /// 健康状态
    /// </summary>
    public bool IsHealthy { get; set; } = true;
}

/// <summary>
/// 服务变化事件
/// </summary>
public sealed class ServiceChangeEvent
{
    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// 变化类型
    /// </summary>
    public ServiceChangeType ChangeType { get; set; }

    /// <summary>
    /// 变化的端点
    /// </summary>
    public ServiceEndpoint? Endpoint { get; set; }
}

/// <summary>
/// 服务变化类型
/// </summary>
public enum ServiceChangeType
{
    EndpointAdded,
    EndpointRemoved,
    EndpointUpdated,
    ServiceRemoved
}

/// <summary>
/// 连接注册事件参数
/// </summary>
public sealed class ConnectionRegisteredEventArgs : EventArgs
{
    public IConnection Connection { get; set; } = null!;
}

/// <summary>
/// 连接注销事件参数
/// </summary>
public sealed class ConnectionUnregisteredEventArgs : EventArgs
{
    public string ConnectionId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 连接统计信息
/// </summary>
public sealed class ConnectionStatistics
{
    /// <summary>
    /// 连接ID
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 连接时间
    /// </summary>
    public DateTime? ConnectedAt { get; set; }

    /// <summary>
    /// 最后活跃时间
    /// </summary>
    public DateTime LastActiveAt { get; set; }

    /// <summary>
    /// 总请求数
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// 成功请求数
    /// </summary>
    public long SuccessfulRequests { get; set; }

    /// <summary>
    /// 失败请求数
    /// </summary>
    public long FailedRequests { get; set; }

    /// <summary>
    /// 发送字节数
    /// </summary>
    public long BytesSent { get; set; }

    /// <summary>
    /// 接收字节数
    /// </summary>
    public long BytesReceived { get; set; }

    /// <summary>
    /// 平均响应时间（毫秒）
    /// </summary>
    public double AverageResponseTimeMs { get; set; }
}

/// <summary>
/// 健康检查结果
/// </summary>
public sealed class HealthCheckResult
{
    /// <summary>
    /// 连接ID
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// 健康状态
    /// </summary>
    public ConnectionHealth Health { get; set; } = ConnectionHealth.Unknown;

    /// <summary>
    /// 检查时间
    /// </summary>
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 响应时间
    /// </summary>
    public TimeSpan ResponseTime { get; set; }

    /// <summary>
    /// 消息
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// 异常信息
    /// </summary>
    public Exception? Exception { get; set; }
}

/// <summary>
/// 连接健康状态
/// </summary>
public enum ConnectionHealth
{
    Unknown,
    Healthy,
    Degraded,
    Unhealthy
}

/// <summary>
/// PulseRPC 客户端构建器接口
/// </summary>
public interface IPulseRPCClientBuilder
{
    /// <summary>
    /// 添加连接配置
    /// </summary>
    IPulseRPCClientBuilder AddConnection(ConnectionDescriptor descriptor);

    /// <summary>
    /// 配置服务发现
    /// </summary>
    IPulseRPCClientBuilder WithServiceDiscovery(IServiceDiscovery serviceDiscovery);

    /// <summary>
    /// 配置负载均衡策略
    /// </summary>
    IPulseRPCClientBuilder WithLoadBalancing(LoadBalancingStrategy strategy, IReadOnlyDictionary<string, object>? options = null);

    /// <summary>
    /// 配置连接池
    /// </summary>
    IPulseRPCClientBuilder WithConnectionPooling(ConnectionPoolOptions poolOptions);

    /// <summary>
    /// 配置重试策略
    /// </summary>
    IPulseRPCClientBuilder WithRetryPolicy(RetryPolicy retryPolicy);

    /// <summary>
    /// 配置日志
    /// </summary>
    IPulseRPCClientBuilder WithLogging(ILoggerFactory loggerFactory);

    /// <summary>
    /// 配置序列化器
    /// </summary>
    IPulseRPCClientBuilder WithSerializer(IPulseSerializer serializer);

    /// <summary>
    /// 配置认证提供者
    /// </summary>
    IPulseRPCClientBuilder WithAuthentication(IAuthenticationProvider authenticationProvider);

    /// <summary>
    /// 配置传输选项
    /// </summary>
    IPulseRPCClientBuilder WithTransportOptions(TransportType transportType, TransportOptions options);

    /// <summary>
    /// 配置客户端选项
    /// </summary>
    IPulseRPCClientBuilder Configure(Action<ClientOptions> configure);

    /// <summary>
    /// 构建客户端
    /// </summary>
    IPulseRPCClient Build();
}