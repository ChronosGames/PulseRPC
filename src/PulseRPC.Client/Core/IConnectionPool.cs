namespace PulseRPC.Client.Core;

/// <summary>
/// 连接池策略
/// </summary>
public enum PoolingStrategy
{
    /// <summary>
    /// 固定大小池
    /// </summary>
    FixedSize,
    
    /// <summary>
    /// 动态扩展池
    /// </summary>
    Dynamic,
    
    /// <summary>
    /// 按需创建（每次都创建新连接）
    /// </summary>
    OnDemand,
    
    /// <summary>
    /// 单例模式（复用同一个连接）
    /// </summary>
    Singleton
}

/// <summary>
/// 连接池配置
/// </summary>
public sealed record ConnectionPoolOptions
{
    /// <summary>
    /// 池化策略
    /// </summary>
    public PoolingStrategy Strategy { get; init; } = PoolingStrategy.Dynamic;
    
    /// <summary>
    /// 最小连接数
    /// </summary>
    public int MinSize { get; init; } = 1;
    
    /// <summary>
    /// 最大连接数
    /// </summary>
    public int MaxSize { get; init; } = 10;
    
    /// <summary>
    /// 初始连接数
    /// </summary>
    public int InitialSize { get; init; } = 1;
    
    /// <summary>
    /// 连接获取超时时间
    /// </summary>
    public TimeSpan AcquireTimeout { get; init; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// 连接空闲超时时间
    /// </summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(10);
    
    /// <summary>
    /// 连接生存时间
    /// </summary>
    public TimeSpan? MaxLifetime { get; init; }
    
    /// <summary>
    /// 连接验证间隔
    /// </summary>
    public TimeSpan ValidationInterval { get; init; } = TimeSpan.FromMinutes(5);
    
    /// <summary>
    /// 获取时是否验证连接
    /// </summary>
    public bool ValidateOnAcquire { get; init; } = true;
    
    /// <summary>
    /// 归还时是否验证连接
    /// </summary>
    public bool ValidateOnReturn { get; init; } = false;
    
    /// <summary>
    /// 空闲时是否验证连接
    /// </summary>
    public bool ValidateWhileIdle { get; init; } = true;
    
    /// <summary>
    /// 是否启用连接预热
    /// </summary>
    public bool EnableWarmup { get; init; } = false;
    
    /// <summary>
    /// 连接池收缩检查间隔
    /// </summary>
    public TimeSpan ShrinkCheckInterval { get; init; } = TimeSpan.FromMinutes(1);
    
    /// <summary>
    /// 扩展属性
    /// </summary>
    public IReadOnlyDictionary<string, object> Properties { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// 连接租约接口 - 表示从连接池租借的连接
/// </summary>
public interface IConnectionLease : IDisposable
{
    /// <summary>
    /// 租借的连接
    /// </summary>
    IConnection Connection { get; }
    
    /// <summary>
    /// 租约ID
    /// </summary>
    string LeaseId { get; }
    
    /// <summary>
    /// 租借时间
    /// </summary>
    DateTime AcquiredAt { get; }
    
    /// <summary>
    /// 租约过期时间
    /// </summary>
    DateTime? ExpiresAt { get; }
    
    /// <summary>
    /// 是否已释放
    /// </summary>
    bool IsReleased { get; }
    
    /// <summary>
    /// 手动释放租约
    /// </summary>
    /// <param name="healthy">连接是否健康</param>
    void Release(bool healthy = true);
}

/// <summary>
/// 连接池接口
/// </summary>
public interface IConnectionPool : IDisposable
{
    /// <summary>
    /// 池名称
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// 连接描述符
    /// </summary>
    ConnectionDescriptor Descriptor { get; }
    
    /// <summary>
    /// 池配置选项
    /// </summary>
    ConnectionPoolOptions Options { get; }
    
    /// <summary>
    /// 当前池大小
    /// </summary>
    int CurrentSize { get; }
    
    /// <summary>
    /// 活跃连接数
    /// </summary>
    int ActiveConnections { get; }
    
    /// <summary>
    /// 空闲连接数
    /// </summary>
    int IdleConnections { get; }
    
    /// <summary>
    /// 等待获取连接的请求数
    /// </summary>
    int WaitingRequests { get; }
    
    /// <summary>
    /// 是否已初始化
    /// </summary>
    bool IsInitialized { get; }
    
    /// <summary>
    /// 初始化连接池
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>初始化任务</returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取连接租约
    /// </summary>
    /// <param name="timeout">获取超时时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接租约</returns>
    Task<IConnectionLease> AcquireAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 尝试立即获取连接租约
    /// </summary>
    /// <param name="lease">输出连接租约</param>
    /// <returns>是否成功获取</returns>
    bool TryAcquire(out IConnectionLease? lease);
    
    /// <summary>
    /// 预热连接池
    /// </summary>
    /// <param name="targetSize">目标大小</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>预热任务</returns>
    Task WarmupAsync(int? targetSize = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 清理池中的无效连接
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>清理的连接数</returns>
    Task<int> CleanupAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 收缩连接池到最小大小
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>收缩任务</returns>
    Task ShrinkAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 关闭连接池
    /// </summary>
    /// <param name="graceful">是否优雅关闭</param>
    /// <param name="timeout">关闭超时时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>关闭任务</returns>
    Task ShutdownAsync(bool graceful = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取连接池统计信息
    /// </summary>
    /// <returns>统计信息</returns>
    ConnectionPoolStatistics GetStatistics();
    
    /// <summary>
    /// 连接池状态变化事件
    /// </summary>
    event EventHandler<ConnectionPoolStateChangedEventArgs> StateChanged;
}

/// <summary>
/// 连接池工厂接口
/// </summary>
public interface IConnectionPoolFactory
{
    /// <summary>
    /// 创建连接池
    /// </summary>
    /// <param name="name">池名称</param>
    /// <param name="descriptor">连接描述符</param>
    /// <param name="options">池配置选项</param>
    /// <returns>连接池实例</returns>
    IConnectionPool CreatePool(string name, ConnectionDescriptor descriptor, ConnectionPoolOptions? options = null);
    
    /// <summary>
    /// 支持的池化策略
    /// </summary>
    IReadOnlySet<PoolingStrategy> SupportedStrategies { get; }
}

/// <summary>
/// 连接路由器接口 - 负责将请求路由到合适的连接
/// </summary>
public interface IConnectionRouter
{
    /// <summary>
    /// 路由到最适合的连接
    /// </summary>
    /// <param name="routingKey">路由键</param>
    /// <param name="context">路由上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接实例</returns>
    Task<IConnection> RouteAsync(string routingKey, RoutingContext? context = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 路由到连接租约
    /// </summary>
    /// <param name="routingKey">路由键</param>
    /// <param name="context">路由上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接租约</returns>
    Task<IConnectionLease> RouteToLeaseAsync(string routingKey, RoutingContext? context = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取可用的路由目标
    /// </summary>
    /// <param name="routingKey">路由键</param>
    /// <param name="context">路由上下文</param>
    /// <returns>可用连接列表</returns>
    IReadOnlyList<IConnection> GetAvailableTargets(string routingKey, RoutingContext? context = null);
    
    /// <summary>
    /// 注册路由规则
    /// </summary>
    /// <param name="rule">路由规则</param>
    void RegisterRule(RoutingRule rule);
    
    /// <summary>
    /// 移除路由规则
    /// </summary>
    /// <param name="ruleId">规则ID</param>
    /// <returns>是否成功移除</returns>
    bool RemoveRule(string ruleId);
    
    /// <summary>
    /// 获取所有路由规则
    /// </summary>
    /// <returns>路由规则列表</returns>
    IReadOnlyList<RoutingRule> GetRules();
}

/// <summary>
/// 路由上下文
/// </summary>
public sealed record RoutingContext
{
    /// <summary>
    /// 请求ID
    /// </summary>
    public string? RequestId { get; init; }
    
    /// <summary>
    /// 服务名称
    /// </summary>
    public string? ServiceName { get; init; }
    
    /// <summary>
    /// 方法名称
    /// </summary>
    public string? MethodName { get; init; }
    
    /// <summary>
    /// 用户ID
    /// </summary>
    public string? UserId { get; init; }
    
    /// <summary>
    /// 负载均衡提示
    /// </summary>
    public LoadBalancingHint LoadBalancingHint { get; init; } = LoadBalancingHint.None;
    
    /// <summary>
    /// 优先级
    /// </summary>
    public int Priority { get; init; } = 0;
    
    /// <summary>
    /// 超时时间
    /// </summary>
    public TimeSpan? Timeout { get; init; }
    
    /// <summary>
    /// 标签
    /// </summary>
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();
    
    /// <summary>
    /// 扩展属性
    /// </summary>
    public IReadOnlyDictionary<string, object> Properties { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// 负载均衡提示
/// </summary>
public enum LoadBalancingHint
{
    /// <summary>
    /// 无特殊要求
    /// </summary>
    None,
    
    /// <summary>
    /// 优先使用最少连接的节点
    /// </summary>
    LeastConnections,
    
    /// <summary>
    /// 优先使用响应时间最短的节点
    /// </summary>
    FastestResponse,
    
    /// <summary>
    /// 优先使用负载最低的节点
    /// </summary>
    LeastLoad,
    
    /// <summary>
    /// 使用一致性哈希
    /// </summary>
    ConsistentHash,
    
    /// <summary>
    /// 粘性会话（尽量使用同一个连接）
    /// </summary>
    StickySession
}

/// <summary>
/// 路由规则
/// </summary>
public sealed record RoutingRule
{
    /// <summary>
    /// 规则ID
    /// </summary>
    public required string Id { get; init; }
    
    /// <summary>
    /// 规则名称
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// 匹配条件
    /// </summary>
    public required Func<string, RoutingContext?, bool> Matcher { get; init; }
    
    /// <summary>
    /// 目标选择器
    /// </summary>
    public required Func<IReadOnlyList<IConnection>, RoutingContext?, IConnection?> Selector { get; init; }
    
    /// <summary>
    /// 规则优先级（数值越大优先级越高）
    /// </summary>
    public int Priority { get; init; } = 0;
    
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; init; } = true;
    
    /// <summary>
    /// 规则描述
    /// </summary>
    public string? Description { get; init; }
    
    /// <summary>
    /// 规则标签
    /// </summary>
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// 连接池统计信息
/// </summary>
public sealed record ConnectionPoolStatistics
{
    /// <summary>
    /// 池名称
    /// </summary>
    public required string PoolName { get; init; }
    
    /// <summary>
    /// 当前大小
    /// </summary>
    public int CurrentSize { get; init; }
    
    /// <summary>
    /// 最大大小
    /// </summary>
    public int MaxSize { get; init; }
    
    /// <summary>
    /// 活跃连接数
    /// </summary>
    public int ActiveConnections { get; init; }
    
    /// <summary>
    /// 空闲连接数
    /// </summary>
    public int IdleConnections { get; init; }
    
    /// <summary>
    /// 等待获取连接的请求数
    /// </summary>
    public int WaitingRequests { get; init; }
    
    /// <summary>
    /// 总获取次数
    /// </summary>
    public long TotalAcquires { get; init; }
    
    /// <summary>
    /// 总释放次数
    /// </summary>
    public long TotalReleases { get; init; }
    
    /// <summary>
    /// 获取超时次数
    /// </summary>
    public long AcquireTimeouts { get; init; }
    
    /// <summary>
    /// 验证失败次数
    /// </summary>
    public long ValidationFailures { get; init; }
    
    /// <summary>
    /// 平均获取时间（毫秒）
    /// </summary>
    public double AverageAcquireTimeMs { get; init; }
    
    /// <summary>
    /// 平均连接使用时间（毫秒）
    /// </summary>
    public double AverageUsageTimeMs { get; init; }
    
    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; }
    
    /// <summary>
    /// 最后活动时间
    /// </summary>
    public DateTime LastActivityAt { get; init; }
    
    /// <summary>
    /// 统计时间
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 连接池状态
/// </summary>
public enum ConnectionPoolState
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
    /// 活跃状态
    /// </summary>
    Active,
    
    /// <summary>
    /// 正在关闭
    /// </summary>
    Shutting down,
    
    /// <summary>
    /// 已关闭
    /// </summary>
    Shutdown,
    
    /// <summary>
    /// 错误状态
    /// </summary>
    Error
}

/// <summary>
/// 连接池状态变化事件参数
/// </summary>
public sealed class ConnectionPoolStateChangedEventArgs : EventArgs
{
    public required string PoolName { get; init; }
    public required ConnectionPoolState PreviousState { get; init; }
    public required ConnectionPoolState CurrentState { get; init; }
    public Exception? Exception { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}