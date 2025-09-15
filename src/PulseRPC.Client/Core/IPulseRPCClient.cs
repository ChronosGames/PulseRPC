namespace PulseRPC.Client.Core;

/// <summary>
/// PulseRPC 客户端核心接口
/// </summary>
public interface IPulseRPCClient : IDisposable
{
    /// <summary>
    /// 连接注册表
    /// </summary>
    IConnectionRegistry Registry { get; }
    
    /// <summary>
    /// 连接生命周期管理器
    /// </summary>
    IConnectionLifecycleManager Lifecycle { get; }
    
    /// <summary>
    /// 连接路由器
    /// </summary>
    IConnectionRouter Router { get; }
    
    /// <summary>
    /// 服务发现
    /// </summary>
    IServiceDiscovery ServiceDiscovery { get; }
    
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
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>初始化任务</returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 停止客户端
    /// </summary>
    /// <param name="graceful">是否优雅停止</param>
    /// <param name="timeout">停止超时时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>停止任务</returns>
    Task StopAsync(bool graceful = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 连接到服务
    /// </summary>
    /// <param name="descriptor">连接描述符</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接实例</returns>
    Task<IConnection> ConnectAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 通过服务发现连接到服务
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="options">连接选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接实例</returns>
    Task<IConnection> ConnectToServiceAsync(string serviceName, ServiceConnectionOptions? options = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取服务代理（自动路由到最佳连接）
    /// </summary>
    /// <typeparam name="T">服务接口类型</typeparam>
    /// <param name="options">服务代理选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务代理实例</returns>
    Task<T> GetServiceAsync<T>(ServiceProxyOptions? options = null, CancellationToken cancellationToken = default) 
        where T : class, IPulseService;
    
    /// <summary>
    /// 获取指定连接的服务代理
    /// </summary>
    /// <typeparam name="T">服务接口类型</typeparam>
    /// <param name="connectionId">连接ID</param>
    /// <param name="options">服务代理选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务代理实例</returns>
    Task<T> GetServiceAsync<T>(string connectionId, ServiceProxyOptions? options = null, CancellationToken cancellationToken = default) 
        where T : class, IPulseService;
    
    /// <summary>
    /// 注册事件监听器（自动路由到最佳连接）
    /// </summary>
    /// <typeparam name="T">监听器接口类型</typeparam>
    /// <param name="listener">监听器实例</param>
    /// <param name="options">监听器选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>订阅令牌</returns>
    Task<ISubscriptionToken> RegisterEventListenerAsync<T>(T listener, EventListenerOptions? options = null, CancellationToken cancellationToken = default) 
        where T : class, IPulseEventHandler;
    
    /// <summary>
    /// 在指定连接上注册事件监听器
    /// </summary>
    /// <typeparam name="T">监听器接口类型</typeparam>
    /// <param name="connectionId">连接ID</param>
    /// <param name="listener">监听器实例</param>
    /// <param name="options">监听器选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>订阅令牌</returns>
    Task<ISubscriptionToken> RegisterEventListenerAsync<T>(string connectionId, T listener, EventListenerOptions? options = null, CancellationToken cancellationToken = default) 
        where T : class, IPulseEventHandler;
    
    /// <summary>
    /// 断开连接
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
    /// 获取客户端统计信息
    /// </summary>
    /// <returns>统计信息</returns>
    ClientStatistics GetStatistics();
    
    /// <summary>
    /// 执行健康检查
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康检查结果</returns>
    Task<ClientHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 客户端状态变化事件
    /// </summary>
    event EventHandler<ClientStateChangedEventArgs> StateChanged;
}

/// <summary>
/// 客户端构建器接口
/// </summary>
public interface IPulseRPCClientBuilder
{
    /// <summary>
    /// 添加连接配置
    /// </summary>
    /// <param name="descriptor">连接描述符</param>
    /// <returns>构建器实例</returns>
    IPulseRPCClientBuilder AddConnection(ConnectionDescriptor descriptor);
    
    /// <summary>
    /// 配置服务发现
    /// </summary>
    /// <param name="serviceDiscovery">服务发现实例</param>
    /// <returns>构建器实例</returns>
    IPulseRPCClientBuilder WithServiceDiscovery(IServiceDiscovery serviceDiscovery);
    
    /// <summary>
    /// 配置负载均衡策略
    /// </summary>
    /// <param name="strategy">负载均衡策略</param>
    /// <param name="options">策略选项</param>
    /// <returns>构建器实例</returns>
    IPulseRPCClientBuilder WithLoadBalancing(LoadBalancingStrategy strategy, IReadOnlyDictionary<string, object>? options = null);
    
    /// <summary>
    /// 配置连接池
    /// </summary>
    /// <param name="poolOptions">连接池选项</param>
    /// <returns>构建器实例</returns>
    IPulseRPCClientBuilder WithConnectionPooling(ConnectionPoolOptions poolOptions);
    
    /// <summary>
    /// 配置重试策略
    /// </summary>
    /// <param name="retryPolicy">重试策略</param>
    /// <returns>构建器实例</returns>
    IPulseRPCClientBuilder WithRetryPolicy(RetryPolicy retryPolicy);
    
    /// <summary>
    /// 配置日志
    /// </summary>
    /// <param name="loggerFactory">日志工厂</param>
    /// <returns>构建器实例</returns>
    IPulseRPCClientBuilder WithLogging(ILoggerFactory loggerFactory);
    
    /// <summary>
    /// 配置序列化器
    /// </summary>
    /// <param name="serializer">序列化器</param>
    /// <returns>构建器实例</returns>
    IPulseRPCClientBuilder WithSerializer(IPulseRPCSerializer serializer);
    
    /// <summary>
    /// 配置认证提供者
    /// </summary>
    /// <param name="authenticationProvider">认证提供者</param>
    /// <returns>构建器实例</returns>
    IPulseRPCClientBuilder WithAuthentication(IAuthenticationProvider authenticationProvider);
    
    /// <summary>
    /// 配置传输选项
    /// </summary>
    /// <param name="transportType">传输类型</param>
    /// <param name="options">传输选项</param>
    /// <returns>构建器实例</returns>
    IPulseRPCClientBuilder WithTransportOptions(TransportType transportType, TransportOptions options);
    
    /// <summary>
    /// 配置客户端选项
    /// </summary>
    /// <param name="configure">配置委托</param>
    /// <returns>构建器实例</returns>
    IPulseRPCClientBuilder Configure(Action<ClientOptions> configure);
    
    /// <summary>
    /// 构建客户端
    /// </summary>
    /// <returns>客户端实例</returns>
    IPulseRPCClient Build();
}

/// <summary>
/// 服务连接选项
/// </summary>
public sealed record ServiceConnectionOptions
{
    /// <summary>
    /// 连接策略
    /// </summary>
    public ConnectionStrategy Strategy { get; init; } = ConnectionStrategy.Session;
    
    /// <summary>
    /// 传输类型偏好
    /// </summary>
    public TransportType? PreferredTransport { get; init; }
    
    /// <summary>
    /// 负载均衡提示
    /// </summary>
    public LoadBalancingHint LoadBalancingHint { get; init; } = LoadBalancingHint.None;
    
    /// <summary>
    /// 连接超时时间
    /// </summary>
    public TimeSpan? ConnectTimeout { get; init; }
    
    /// <summary>
    /// 是否启用自动重连
    /// </summary>
    public bool? AutoReconnect { get; init; }
    
    /// <summary>
    /// 连接标签
    /// </summary>
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();
    
    /// <summary>
    /// 扩展属性
    /// </summary>
    public IReadOnlyDictionary<string, object> Properties { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// 客户端选项
/// </summary>
public sealed record ClientOptions
{
    /// <summary>
    /// 客户端名称
    /// </summary>
    public string? Name { get; init; }
    
    /// <summary>
    /// 默认超时时间
    /// </summary>
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// 最大并发连接数
    /// </summary>
    public int MaxConcurrentConnections { get; init; } = 100;
    
    /// <summary>
    /// 连接清理间隔
    /// </summary>
    public TimeSpan CleanupInterval { get; init; } = TimeSpan.FromMinutes(5);
    
    /// <summary>
    /// 健康检查间隔
    /// </summary>
    public TimeSpan HealthCheckInterval { get; init; } = TimeSpan.FromMinutes(1);
    
    /// <summary>
    /// 是否启用统计信息收集
    /// </summary>
    public bool EnableStatistics { get; init; } = true;
    
    /// <summary>
    /// 是否启用性能监控
    /// </summary>
    public bool EnablePerformanceMonitoring { get; init; } = false;
    
    /// <summary>
    /// 默认重试策略
    /// </summary>
    public RetryPolicy? DefaultRetryPolicy { get; init; }
    
    /// <summary>
    /// 扩展属性
    /// </summary>
    public IReadOnlyDictionary<string, object> Properties { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// 客户端状态
/// </summary>
public enum ClientState
{
    /// <summary>
    /// 未初始化
    /// </summary>
    Uninitialized,
    
    /// <summary>
    /// 初始化中
    /// </summary>
    Initializing,
    
    /// <summary>
    /// 运行中
    /// </summary>
    Running,
    
    /// <summary>
    /// 正在停止
    /// </summary>
    Stopping,
    
    /// <summary>
    /// 已停止
    /// </summary>
    Stopped,
    
    /// <summary>
    /// 错误状态
    /// </summary>
    Error
}

/// <summary>
/// 客户端统计信息
/// </summary>
public sealed record ClientStatistics
{
    /// <summary>
    /// 客户端名称
    /// </summary>
    public string? ClientName { get; init; }
    
    /// <summary>
    /// 启动时间
    /// </summary>
    public DateTime StartTime { get; init; }
    
    /// <summary>
    /// 运行时间
    /// </summary>
    public TimeSpan Uptime { get; init; }
    
    /// <summary>
    /// 总连接数
    /// </summary>
    public int TotalConnections { get; init; }
    
    /// <summary>
    /// 活跃连接数
    /// </summary>
    public int ActiveConnections { get; init; }
    
    /// <summary>
    /// 空闲连接数
    /// </summary>
    public int IdleConnections { get; init; }
    
    /// <summary>
    /// 失败连接数
    /// </summary>
    public int FailedConnections { get; init; }
    
    /// <summary>
    /// 总请求数
    /// </summary>
    public long TotalRequests { get; init; }
    
    /// <summary>
    /// 成功请求数
    /// </summary>
    public long SuccessfulRequests { get; init; }
    
    /// <summary>
    /// 失败请求数
    /// </summary>
    public long FailedRequests { get; init; }
    
    /// <summary>
    /// 平均响应时间（毫秒）
    /// </summary>
    public double AverageResponseTimeMs { get; init; }
    
    /// <summary>
    /// 发送的字节数
    /// </summary>
    public long BytesSent { get; init; }
    
    /// <summary>
    /// 接收的字节数
    /// </summary>
    public long BytesReceived { get; init; }
    
    /// <summary>
    /// 统计时间
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 客户端健康检查结果
/// </summary>
public sealed record ClientHealthCheckResult
{
    /// <summary>
    /// 整体健康状态
    /// </summary>
    public required ConnectionHealth OverallHealth { get; init; }
    
    /// <summary>
    /// 连接健康检查结果
    /// </summary>
    public required IReadOnlyList<HealthCheckResult> ConnectionResults { get; init; }
    
    /// <summary>
    /// 服务发现健康状态
    /// </summary>
    public ConnectionHealth ServiceDiscoveryHealth { get; init; } = ConnectionHealth.Unknown;
    
    /// <summary>
    /// 检查时间
    /// </summary>
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// 总检查时间
    /// </summary>
    public TimeSpan TotalCheckTime { get; init; }
    
    /// <summary>
    /// 详细信息
    /// </summary>
    public string? Details { get; init; }
}

/// <summary>
/// 客户端状态变化事件参数
/// </summary>
public sealed class ClientStateChangedEventArgs : EventArgs
{
    public required ClientState PreviousState { get; init; }
    public required ClientState CurrentState { get; init; }
    public Exception? Exception { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 重试策略
/// </summary>
public sealed record RetryPolicy
{
    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; init; } = 3;
    
    /// <summary>
    /// 基础延迟时间
    /// </summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(100);
    
    /// <summary>
    /// 最大延迟时间
    /// </summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// 退避算法
    /// </summary>
    public BackoffStrategy BackoffStrategy { get; init; } = BackoffStrategy.Exponential;
    
    /// <summary>
    /// 抖动因子（0-1，用于避免惊群效应）
    /// </summary>
    public double JitterFactor { get; init; } = 0.1;
    
    /// <summary>
    /// 可重试的异常类型
    /// </summary>
    public IReadOnlySet<Type> RetriableExceptions { get; init; } = new HashSet<Type>();
    
    /// <summary>
    /// 重试条件判断
    /// </summary>
    public Func<Exception, bool>? ShouldRetry { get; init; }
}

/// <summary>
/// 退避策略
/// </summary>
public enum BackoffStrategy
{
    /// <summary>
    /// 固定延迟
    /// </summary>
    Fixed,
    
    /// <summary>
    /// 线性增长
    /// </summary>
    Linear,
    
    /// <summary>
    /// 指数增长
    /// </summary>
    Exponential
}