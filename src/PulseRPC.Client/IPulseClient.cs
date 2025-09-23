using Microsoft.Extensions.Logging;
using PulseRPC.Authentication;
using PulseRPC.Client;
using PulseRPC.Client.ConnectionPool;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Transport;

namespace PulseRPC.Client;

/// <summary>
/// PulseRPC 客户端核心接口 - 统一客户端入口
/// 实现思路：
/// - 聚合所有子组件（连接管理、路由、负载均衡等）
/// - 提供高级 API 隐藏底层复杂性
/// - 管理客户端生命周期（初始化、运行、停止）
/// - 提供统一的错误处理和重试机制
/// </summary>
public interface IPulseClient : IDisposable
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
    Task<IClientChannel> ConnectAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default);

    /// <summary>
    /// 通过服务发现连接到服务
    /// </summary>
    Task<IClientChannel> ConnectToServiceAsync(string serviceName, ServiceConnectionOptions? options = null, CancellationToken cancellationToken = default);

    // 注意：GetServiceAsync<T> 和 RegisterEventListenerAsync<T> 方法现在通过
    // PulseClientFactoryExtensions 扩展方法提供，以支持源代码生成器工厂模式

    /// <summary>
    /// 断开连接
    /// </summary>
    Task DisconnectAsync(string connectionId, bool graceful = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量断开连接
    /// </summary>
    Task<int> DisconnectAsync(Func<IClientChannel, bool> predicate, bool graceful = true, CancellationToken cancellationToken = default);

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
/// 连接管理器接口 - 管理所有连接的创建、销毁和生命周期
/// 实现思路：
/// - 连接工厂职责：根据配置创建不同类型的连接
/// - 连接注册表：维护所有活跃连接的索引
/// - 生命周期管理：自动管理连接的创建和销毁
/// - 健康监控：定期检查连接健康状态
/// - 资源管理：防止连接泄漏，自动清理无用连接
/// </summary>
public interface IConnectionManager : IDisposable
{
    /// <summary>
    /// 连接到服务
    /// </summary>
    Task<IClientChannel> ConnectAsync(ConnectionConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// 通过描述符连接
    /// </summary>
    Task<IClientChannel> ConnectAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开连接
    /// </summary>
    Task DisconnectAsync(string connectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量断开连接
    /// </summary>
    Task DisconnectAsync(Func<IClientChannel, bool> predicate, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取连接
    /// </summary>
    IClientChannel? GetConnection(string connectionId);

    /// <summary>
    /// 根据标签查找连接
    /// </summary>
    IReadOnlyList<IClientChannel> GetConnectionsByTag(string key, string? value = null);

    /// <summary>
    /// 获取所有连接
    /// </summary>
    IReadOnlyList<IClientChannel> GetAllConnections();

    /// <summary>
    /// 连接数量
    /// </summary>
    int Count { get; }

    /// <summary>
    /// 获取或创建连接
    /// </summary>
    // Task<IClientChannel> GetOrCreateConnectionAsync(ConnectionConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理空闲连接
    /// </summary>
    Task<int> CleanupIdleConnectionsAsync(TimeSpan? maxAge = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 连接状态变化事件
    /// </summary>
    // event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;
}

/// <summary>
/// 连接路由器接口 - 智能路由决策引擎
/// 实现思路：
/// - 规则引擎：支持灵活的路由规则配置
/// - 多维度路由：支持基于标签、区域、用户等多维度路由
/// - 缓存优化：缓存路由决策结果，提高性能
/// - 故障转移：自动检测连接故障并切换到备用连接
/// - 负载感知：结合负载均衡器进行智能选择
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
    Task<IClientChannel> RouteAsync(string routingKey, RoutingContext? context = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有匹配的连接
    /// </summary>
    IReadOnlyList<IClientChannel> GetMatchingConnections(string routingKey, RoutingContext? context = null);
}

/// <summary>
/// 服务发现接口 - 动态服务实例发现
/// 实现思路：
/// - 多后端支持：支持Consul、Etcd、Kubernetes等服务发现后端
/// - 缓存机制：本地缓存服务实例信息，减少网络开销
/// - 变更通知：实时监听服务实例变化并通知上层
/// - 健康检查：集成健康检查，过滤不健康的实例
/// - 故障恢复：服务发现后端故障时的恢复机制
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
/// 连接注册表接口 - 连接实例的注册和查询
/// 实现思路：
/// - 索引优化：使用多种索引支持快速查询
/// - 并发安全：使用读写锁保证并发安全
/// - 事件通知：连接注册/注销时发送事件
/// - 标签查询：支持基于标签的复杂查询
/// - 内存优化：使用弱引用避免内存泄漏
/// </summary>
public interface IConnectionRegistry
{
    /// <summary>
    /// 注册连接
    /// </summary>
    void RegisterConnection(IClientChannel connection);

    /// <summary>
    /// 注销连接
    /// </summary>
    void UnregisterConnection(string connectionId, string reason = "手动注销");

    /// <summary>
    /// 根据标签获取连接
    /// </summary>
    IReadOnlyList<IClientChannel> GetConnectionsByTags(Dictionary<string, string> tags);

    /// <summary>
    /// 获取连接
    /// </summary>
    IClientChannel? GetConnection(string connectionId);

    /// <summary>
    /// 获取所有连接
    /// </summary>
    IReadOnlyList<IClientChannel> GetAllConnections();

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
/// 连接生命周期管理器接口 - 管理连接的整个生命周期
/// 实现思路：
/// - 策略模式：支持不同的生命周期管理策略
/// - 定时维护：定期执行健康检查和清理任务
/// - 事件驱动：响应连接状态变化事件
/// - 资源优化：自动清理无用连接，优化资源使用
/// - 故障恢复：自动重连和故障转移
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
/// 负载均衡器接口 - 智能负载分配
/// 实现思路：
/// - 多策略支持：支持轮询、最少连接、一致性哈希等策略
/// - 健康感知：只选择健康的连接实例
/// - 权重支持：支持基于权重的负载分配
/// - 性能监控：根据响应时间等指标进行智能选择
/// - 动态调整：根据实时负载情况动态调整策略
/// </summary>
public interface ILoadBalancer
{
    /// <summary>
    /// 选择最佳连接
    /// </summary>
    IClientChannel? SelectConnection(IReadOnlyList<IClientChannel> connections, LoadBalancingHint hint = LoadBalancingHint.None);

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
    public Func<IReadOnlyList<IClientChannel>, RoutingContext?, IClientChannel?> Selector { get; set; } = (connections, _) => connections.FirstOrDefault();

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
    public EndpointAddress? Address { get; set; }

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
    public IClientChannel Connection { get; set; } = null!;
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
/// PulseRPC 客户端构建器接口
/// 实现思路：
/// - 使用构建器模式提供流畅的配置API
/// - 延迟初始化，在Build()时创建所有组件
/// - 配置验证，确保必需的配置项都已设置
/// - 支持扩展点，允许自定义组件实现
/// </summary>
public interface IPulseClientBuilder
{
    /// <summary>
    /// 添加连接配置
    /// </summary>
    IPulseClientBuilder AddConnection(ConnectionDescriptor descriptor);

    /// <summary>
    /// 配置服务发现
    /// </summary>
    IPulseClientBuilder WithServiceDiscovery(IServiceDiscovery serviceDiscovery);

    /// <summary>
    /// 配置负载均衡策略
    /// </summary>
    IPulseClientBuilder WithLoadBalancing(LoadBalancingStrategy strategy, IReadOnlyDictionary<string, object>? options = null);

    /// <summary>
    /// 配置连接池
    /// </summary>
    IPulseClientBuilder WithConnectionPooling(ConnectionPoolOptions poolOptions);

    /// <summary>
    /// 配置重试策略
    /// </summary>
    IPulseClientBuilder WithRetryPolicy(RetryPolicy retryPolicy);

    /// <summary>
    /// 配置日志
    /// </summary>
    IPulseClientBuilder WithLogging(ILoggerFactory loggerFactory);

    /// <summary>
    /// 配置序列化器
    /// </summary>
    IPulseClientBuilder WithSerializer(ISerializerProvider serializerProvider);

    /// <summary>
    /// 配置认证提供者
    /// </summary>
    IPulseClientBuilder WithAuthentication(IAuthenticationProvider authenticationProvider);

    /// <summary>
    /// 配置传输选项
    /// </summary>
    IPulseClientBuilder WithTransportOptions(TransportType transportType, TransportOptions options);

    /// <summary>
    /// 配置客户端选项
    /// </summary>
    IPulseClientBuilder Configure(Action<ClientOptions> configure);

    /// <summary>
    /// 构建客户端
    /// </summary>
    IPulseClient Build();
}
