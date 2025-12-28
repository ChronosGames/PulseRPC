namespace PulseRPC.Server.Abstractions;

/// <summary>
/// 标记 IUnifiedPulseService 实现类的服务特性
/// </summary>
/// <remarks>
/// <para>
/// 通过此特性声明服务的元数据，包括启动类型、实例范围和调度模式。
/// </para>
/// <para>
/// <strong>使用示例</strong>：
/// </para>
/// <code>
/// // 单例匹配服务，自动启动，使用专属队列
/// [PulseService(
///     StartupType = ServiceStartupType.AutoStart,
///     InstanceScope = ServiceInstanceScope.Singleton,
///     SchedulingMode = ServiceSchedulingMode.DedicatedQueue)]
/// public class MatchmakingService : IUnifiedPulseService, IMatchmakingHub
/// {
///     public string ServiceType => "Matchmaking";
///     public string ServiceId => "default";
///     // ...
/// }
///
/// // 多实例的聊天室服务，按需创建，使用线程亲和性
/// [PulseService(
///     StartupType = ServiceStartupType.OnDemand,
///     InstanceScope = ServiceInstanceScope.MultiInstance,
///     SchedulingMode = ServiceSchedulingMode.ThreadAffinity)]
/// public class ChatRoomService : IUnifiedPulseService, IChatRoomHub
/// {
///     public string ServiceType => "ChatRoom";
///     public string ServiceId { get; }
///
///     public ChatRoomService(string roomId)
///     {
///         ServiceId = roomId;
///     }
///     // ...
/// }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class PulseServiceAttribute : Attribute
{
    /// <summary>
    /// 服务启动类型
    /// </summary>
    /// <remarks>
    /// <para>默认值：<see cref="ServiceStartupType.OnDemand"/>（按需启动）</para>
    /// <para>
    /// 对于 <see cref="ServiceInstanceScope.Singleton"/> 服务，推荐设置为
    /// <see cref="ServiceStartupType.AutoStart"/>。
    /// </para>
    /// </remarks>
    public ServiceStartupType StartupType { get; set; } = ServiceStartupType.OnDemand;

    /// <summary>
    /// 服务实例范围
    /// </summary>
    /// <remarks>
    /// <para>默认值：<see cref="ServiceInstanceScope.MultiInstance"/>（多实例）</para>
    /// <para>
    /// 此属性决定了 ServiceId 的生成策略：
    /// </para>
    /// <list type="bullet">
    /// <item><description><see cref="ServiceInstanceScope.Singleton"/>：ServiceId = "{ServiceName}:default"</description></item>
    /// <item><description><see cref="ServiceInstanceScope.MultiInstance"/>：ServiceId = "{ServiceName}:{businessId}"</description></item>
    /// </list>
    /// <para>
    /// 注意：如需跨进程/集群单例语义，请在业务层通过服务发现+分布式锁实现。
    /// </para>
    /// </remarks>
    public ServiceInstanceScope InstanceScope { get; set; } = ServiceInstanceScope.MultiInstance;

    /// <summary>
    /// 服务调度模式
    /// </summary>
    /// <remarks>
    /// <para>默认值：<see cref="ServiceSchedulingMode.DedicatedQueue"/>（专属队列）</para>
    /// <para>
    /// 推荐配置：
    /// </para>
    /// <list type="bullet">
    /// <item><description>有状态服务：<see cref="ServiceSchedulingMode.DedicatedQueue"/> 或 <see cref="ServiceSchedulingMode.ThreadAffinity"/></description></item>
    /// <item><description>无状态服务：<see cref="ServiceSchedulingMode.DefaultPool"/></description></item>
    /// </list>
    /// </remarks>
    public ServiceSchedulingMode SchedulingMode { get; set; } = ServiceSchedulingMode.DedicatedQueue;

    /// <summary>
    /// 服务显示名称（可选）
    /// </summary>
    /// <remarks>
    /// 如果未指定，则使用类名作为服务名称。
    /// </remarks>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 服务描述（可选）
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 最大并发请求数（仅对 DefaultPool 模式有效）
    /// </summary>
    /// <remarks>
    /// <para>默认值：0（不限制）</para>
    /// <para>设置此值可以限制服务的并发处理能力，防止过载。</para>
    /// </remarks>
    public int MaxConcurrency { get; set; } = 0;

    /// <summary>
    /// 队列容量（仅对 DedicatedQueue 和 ThreadAffinity 模式有效）
    /// </summary>
    /// <remarks>
    /// <para>默认值：1000</para>
    /// <para>当队列满时，根据 <see cref="BackpressureMode"/> 决定行为。</para>
    /// </remarks>
    public int QueueCapacity { get; set; } = 1000;

    /// <summary>
    /// 背压模式（队列满时的行为）
    /// </summary>
    /// <remarks>
    /// <para>默认值：<see cref="ServiceBackpressureMode.Block"/>（阻塞等待）</para>
    /// </remarks>
    public ServiceBackpressureMode BackpressureMode { get; set; } = ServiceBackpressureMode.Block;

    /// <summary>
    /// 空闲超时时间（秒），超时后服务实例可能被回收
    /// </summary>
    /// <remarks>
    /// <para>默认值：300（5分钟）</para>
    /// <para>设置为 0 表示永不回收。</para>
    /// <para>仅对 <see cref="ServiceStartupType.OnDemand"/> 服务有效。</para>
    /// </remarks>
    public int IdleTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// 是否启用健康检查
    /// </summary>
    /// <remarks>
    /// <para>默认值：true</para>
    /// <para>启用后，需要实现 <see cref="IUnifiedServiceHealthCheck.CheckHealthAsync"/>。</para>
    /// </remarks>
    public bool EnableHealthCheck { get; set; } = true;

    /// <summary>
    /// 健康检查间隔（秒）
    /// </summary>
    /// <remarks>
    /// <para>默认值：60（1分钟）</para>
    /// </remarks>
    public int HealthCheckIntervalSeconds { get; set; } = 60;
}

/// <summary>
/// 服务背压模式 - 当队列满时的行为
/// </summary>
public enum ServiceBackpressureMode
{
    /// <summary>
    /// 阻塞等待 - 等待队列有空间
    /// </summary>
    Block = 0,

    /// <summary>
    /// 丢弃最新 - 丢弃当前请求
    /// </summary>
    DropNewest = 1,

    /// <summary>
    /// 丢弃最旧 - 丢弃队列中最旧的请求
    /// </summary>
    DropOldest = 2,

    /// <summary>
    /// 抛出异常 - 返回服务过载错误
    /// </summary>
    ThrowException = 3
}

