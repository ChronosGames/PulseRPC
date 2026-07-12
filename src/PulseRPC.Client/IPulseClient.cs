using Microsoft.Extensions.Logging;
using PulseRPC.Authentication;
using PulseRPC.Client;
using PulseRPC.Client.Configuration;
using PulseRPC.Client.Health;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Shared;

namespace PulseRPC.Client;

/// <summary>
/// PulseRPC 客户端核心接口 - 统一客户端入口
/// 实现思路：
/// - 聚合连接管理和负载均衡组件
/// - 提供高级 API 隐藏底层复杂性
/// - 管理客户端生命周期（初始化、运行、停止）
/// - 提供统一的错误处理和重试机制
/// </summary>
public interface IPulseClient : IDisposable
{
    /// <summary>
    /// 连接管理器（统一入口，包含路由、查询和生命周期管理功能）
    /// </summary>
    IConnectionManager Connections { get; }

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
    /// <remarks><paramref name="graceful"/> 为 <c>false</c> 时会立即取消后台任务并关闭 socket，不排空待发队列。</remarks>
    Task StopAsync(bool graceful = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 连接到服务
    /// </summary>
    Task<IClientChannel> ConnectAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default);

    /// <summary>
    /// 运行时动态连接到服务器
    /// </summary>
    Task<IClientChannel> ConnectToServerAsync(
        string host,
        int port,
        string? serverId = null,
        string? name = null,
        TransportType transport = TransportType.TCP,
        ConnectionStrategy strategy = ConnectionStrategy.Session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开连接
    /// </summary>
    /// <remarks><paramref name="graceful"/> 为 <c>false</c> 时不发送优雅断开帧，直接关闭底层 socket。</remarks>
    Task DisconnectAsync(string connectionId, bool graceful = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量断开连接
    /// </summary>
    /// <remarks><paramref name="graceful"/> 为 <c>false</c> 时不发送优雅断开帧，直接关闭匹配连接的底层 socket。</remarks>
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
/// 连接管理器接口 - 统一管理连接的创建、销毁、路由和生命周期
/// 实现思路：
/// - 连接工厂职责：根据配置创建不同类型的连接
/// - 连接注册表：维护所有活跃连接的索引（合并原 IConnectionRegistry）
/// - 路由功能：根据服务名选择最佳连接（合并原 IConnectionRouter，简化规则引擎）
/// - 生命周期管理：健康检查和空闲清理（合并原 IConnectionLifecycleManager）
/// - 资源管理：防止连接泄漏，自动清理无用连接
/// </summary>
public interface IConnectionManager : IDisposable
{
    #region 连接创建/销毁

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

    #endregion

    #region 连接查询（原 IConnectionRegistry 功能）

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

    #endregion

    #region 路由功能（原 IConnectionRouter 功能，简化版）

    /// <summary>
    /// 根据服务名称路由到最佳连接
    /// </summary>
    /// <param name="serviceName">服务名称（通常是接口名，如 "IGameHub"）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>最佳连接，如果没有可用连接则返回 null</returns>
    Task<IClientChannel?> RouteAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定服务的所有可用连接
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <returns>匹配的连接列表</returns>
    IReadOnlyList<IClientChannel> GetServiceConnections(string serviceName);

    #endregion

    #region 生命周期管理（原 IConnectionLifecycleManager 功能）

    /// <summary>
    /// 执行健康检查
    /// </summary>
    Task<IReadOnlyList<HealthCheckResult>> PerformHealthChecksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理空闲连接
    /// </summary>
    Task<int> CleanupIdleConnectionsAsync(TimeSpan? maxAge = null, CancellationToken cancellationToken = default);

    #endregion
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
/// Companion interface for load balancers that consume a stable per-selection routing context.
/// </summary>
public interface IContextualLoadBalancer : ILoadBalancer
{
    /// <summary>Selects a healthy connection using the supplied routing context.</summary>
    IClientChannel? SelectConnection(
        IReadOnlyList<IClientChannel> connections,
        LoadBalancingContext context);
}

/// <summary>
/// Companion interface for connection managers that preserve per-call load-balancing context.
/// </summary>
public interface IContextualConnectionManager : IConnectionManager
{
    /// <summary>Routes a service request using the supplied routing context.</summary>
    Task<IClientChannel?> RouteAsync(
        string serviceName,
        LoadBalancingContext context,
        CancellationToken cancellationToken = default);
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
    public HealthStatus Health { get; set; } = HealthStatus.Unknown;

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
