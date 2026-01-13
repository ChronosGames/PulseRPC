namespace PulseRPC.Server.Services;

/// <summary>
/// 标记 IUnifiedPulseService 实现类的服务特性
/// </summary>
/// <remarks>
/// <para>
/// 通过此特性声明服务的元数据和执行配置。
/// </para>
/// <para>
/// <strong>推荐使用方式</strong>：通过 <see cref="Scenario"/> 属性选择预定义场景，
/// 自动获得针对该场景优化的配置。
/// </para>
/// <para>
/// <strong>使用示例</strong>：
/// </para>
/// <code>
/// // 推荐：使用场景预设配置
/// [PulseService(
///     Scenario = ServiceScenario.Actor,
///     InstanceScope = ServiceInstanceScope.MultiInstance)]
/// public class ChatRoomService : UnifiedPulseServiceBase, IChatRoomHub
/// {
/// }
///
/// // 有状态 IO 服务（自动启用 Yielding）
/// [PulseService(
///     Scenario = ServiceScenario.StatefulIO,
///     InstanceScope = ServiceInstanceScope.MultiInstance)]
/// public class PlayerService : UnifiedPulseServiceBase, IPlayerHub
/// {
/// }
///
/// // 无状态查询服务
/// [PulseService(
///     Scenario = ServiceScenario.StatelessIO,
///     InstanceScope = ServiceInstanceScope.Singleton,
///     StartupType = ServiceStartupType.AutoStart)]
/// public class QueryService : UnifiedPulseServiceBase
/// {
/// }
///
/// // 自定义配置（特殊场景）
/// [PulseService(
///     Scenario = ServiceScenario.Custom,
///     SchedulingMode = ServiceSchedulingMode.DedicatedQueue,
///     MaxConcurrency = 4,
///     BackpressureMode = ServiceBackpressureMode.DropOldest)]
/// public class CustomService : UnifiedPulseServiceBase
/// {
/// }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class PulseServiceAttribute : Attribute
{
    // ════════════════════════════════════════════════════════════════════════
    // 执行场景（推荐使用）
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 执行场景（推荐使用）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 通过选择场景，自动获得针对该场景优化的配置，无需手动设置各个参数。
    /// </para>
    /// <para>
    /// <strong>场景选择指南</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description><see cref="ServiceScenario.Actor"/>：有状态服务，单线程顺序执行（聊天室、游戏房间）</description></item>
    /// <item><description><see cref="ServiceScenario.StatelessIO"/>：无状态 IO 密集服务（数据库查询、HTTP 网关）</description></item>
    /// <item><description><see cref="ServiceScenario.StatelessCPU"/>：无状态 CPU 密集服务（寻路计算）</description></item>
    /// <item><description><see cref="ServiceScenario.StatefulIO"/>：有状态 + IO 密集，启用 Yielding（玩家服务）</description></item>
    /// <item><description><see cref="ServiceScenario.HighFrequency"/>：高频更新，利用 CPU 缓存局部性</description></item>
    /// <item><description><see cref="ServiceScenario.Custom"/>：特殊场景，使用下方的详细配置项</description></item>
    /// </list>
    /// </remarks>
    public ServiceScenario Scenario { get; set; } = ServiceScenario.Actor;

    // ════════════════════════════════════════════════════════════════════════
    // 身份元数据
    // ════════════════════════════════════════════════════════════════════════

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

    // ════════════════════════════════════════════════════════════════════════
    // 生命周期管理
    // ════════════════════════════════════════════════════════════════════════

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

    // ════════════════════════════════════════════════════════════════════════
    // 执行配置（仅当 Scenario = Custom 时有效）
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 服务调度模式
    /// </summary>
    /// <remarks>
    /// <para><strong>仅当 <see cref="Scenario"/> = <see cref="ServiceScenario.Custom"/> 时有效</strong></para>
    /// <para>默认值：<see cref="ServiceSchedulingMode.DedicatedQueue"/>（专属队列）</para>
    /// <list type="bullet">
    /// <item><description><see cref="ServiceSchedulingMode.DefaultPool"/>：使用公共线程池，最高并发</description></item>
    /// <item><description><see cref="ServiceSchedulingMode.DedicatedQueue"/>：专属队列，顺序执行</description></item>
    /// <item><description><see cref="ServiceSchedulingMode.ThreadAffinity"/>：线程亲和，CPU 缓存优化</description></item>
    /// </list>
    /// </remarks>
    public ServiceSchedulingMode SchedulingMode { get; set; } = ServiceSchedulingMode.DedicatedQueue;

    /// <summary>
    /// 最大并发度
    /// </summary>
    /// <remarks>
    /// <para><strong>仅当 <see cref="Scenario"/> = <see cref="ServiceScenario.Custom"/> 时有效</strong></para>
    /// <para>默认值：1（单线程）</para>
    /// <para>推荐值：</para>
    /// <list type="bullet">
    /// <item><description>Actor 模式：1</description></item>
    /// <item><description>IO 密集：8-16</description></item>
    /// <item><description>CPU 密集：Environment.ProcessorCount</description></item>
    /// </list>
    /// </remarks>
    public int MaxConcurrency { get; set; } = 1;

    /// <summary>
    /// 队列容量
    /// </summary>
    /// <remarks>
    /// <para><strong>仅当 <see cref="Scenario"/> = <see cref="ServiceScenario.Custom"/> 时有效</strong></para>
    /// <para>默认值：10000</para>
    /// <para>当队列满时，根据 <see cref="BackpressureMode"/> 决定行为。</para>
    /// </remarks>
    public int QueueCapacity { get; set; } = 10000;

    /// <summary>
    /// 背压模式（队列满时的行为）
    /// </summary>
    /// <remarks>
    /// <para><strong>仅当 <see cref="Scenario"/> = <see cref="ServiceScenario.Custom"/> 时有效</strong></para>
    /// <para>默认值：<see cref="ServiceBackpressureMode.Block"/>（阻塞等待）</para>
    /// </remarks>
    public ServiceBackpressureMode BackpressureMode { get; set; } = ServiceBackpressureMode.Block;

    /// <summary>
    /// 是否启用让出机制（await 时让出队列）
    /// </summary>
    /// <remarks>
    /// <para><strong>仅当 <see cref="Scenario"/> = <see cref="ServiceScenario.Custom"/> 时有效</strong></para>
    /// <para>默认值：false</para>
    /// <para>
    /// 仅对 <see cref="ServiceSchedulingMode.DedicatedQueue"/> 模式有效。
    /// 启用后，await 时会自动让出队列执行权，允许其他消息处理。
    /// IO 完成后，延续会重新排队，在队列线程中恢复执行。
    /// </para>
    /// <para>
    /// 这是 <see cref="ServiceScenario.StatefulIO"/> 场景的核心机制，
    /// 如果使用该场景，此属性会自动设为 true。
    /// </para>
    /// </remarks>
    public bool EnableYielding { get; set; } = false;
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

