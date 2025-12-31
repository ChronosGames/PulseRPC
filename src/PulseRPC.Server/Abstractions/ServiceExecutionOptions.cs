namespace PulseRPC.Server.Abstractions;

/// <summary>
/// 服务执行配置 - 统一的运行时执行参数
/// </summary>
/// <remarks>
/// <para>
/// 此类统一了原有的 <c>ServiceConfiguration</c> 和 <c>ServiceQueueOptions</c>，
/// 提供单一的配置来源，消除配置混乱。
/// </para>
/// <para>
/// <strong>使用方式</strong>：
/// </para>
/// <list type="number">
/// <item><description>通过 <see cref="PulseServiceAttribute.Scenario"/> 使用预定义配置（推荐）</description></item>
/// <item><description>通过静态属性获取预定义配置（如 <see cref="Actor"/>）</description></item>
/// <item><description>通过 <see cref="FromScenario"/> 从场景枚举创建</description></item>
/// <item><description>手动创建实例进行完全自定义</description></item>
/// </list>
/// </remarks>
public sealed class ServiceExecutionOptions
{
    /// <summary>
    /// 调度模式
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><see cref="ServiceSchedulingMode.DefaultPool"/>：使用公共线程池，最高并发</description></item>
    /// <item><description><see cref="ServiceSchedulingMode.DedicatedQueue"/>：专属队列，顺序执行</description></item>
    /// <item><description><see cref="ServiceSchedulingMode.ThreadAffinity"/>：线程亲和，CPU 缓存优化</description></item>
    /// </list>
    /// </remarks>
    public ServiceSchedulingMode SchedulingMode { get; init; } = ServiceSchedulingMode.DedicatedQueue;

    /// <summary>
    /// 最大并发度
    /// </summary>
    /// <remarks>
    /// <para>不同场景的推荐值：</para>
    /// <list type="bullet">
    /// <item><description>Actor/StatefulIO：1（单线程）</description></item>
    /// <item><description>StatelessIO：16（IO 密集）</description></item>
    /// <item><description>StatelessCPU：Environment.ProcessorCount</description></item>
    /// </list>
    /// <para>DefaultPool 模式下，0 表示不限制。</para>
    /// </remarks>
    public int MaxConcurrency { get; init; } = 1;

    /// <summary>
    /// 队列容量
    /// </summary>
    /// <remarks>
    /// 队列满时，根据 <see cref="BackpressureMode"/> 决定行为。
    /// 默认值 10000 适合大多数场景。
    /// </remarks>
    public int QueueCapacity { get; init; } = 10000;

    /// <summary>
    /// 背压模式（队列满时的行为）
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><see cref="ServiceBackpressureMode.Block"/>：阻塞等待（默认）</description></item>
    /// <item><description><see cref="ServiceBackpressureMode.DropNewest"/>：丢弃当前请求</description></item>
    /// <item><description><see cref="ServiceBackpressureMode.DropOldest"/>：丢弃最旧请求</description></item>
    /// <item><description><see cref="ServiceBackpressureMode.ThrowException"/>：抛出过载异常</description></item>
    /// </list>
    /// </remarks>
    public ServiceBackpressureMode BackpressureMode { get; init; } = ServiceBackpressureMode.Block;

    /// <summary>
    /// 是否启用让出机制（await 时让出队列）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 仅对 <see cref="ServiceSchedulingMode.DedicatedQueue"/> 模式有效。
    /// </para>
    /// <para>
    /// 启用后，await 时会自动让出队列执行权，允许其他消息处理。
    /// IO 完成后，延续会重新排队，在队列线程中恢复执行。
    /// </para>
    /// <para>
    /// 这是 <see cref="ServiceScenario.StatefulIO"/> 场景的核心机制。
    /// </para>
    /// </remarks>
    public bool EnableYielding { get; init; } = false;

    // ════════════════════════════════════════════════════════════════════════
    // 预定义配置（静态属性）
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Actor 模式 - 单线程顺序执行
    /// </summary>
    /// <remarks>
    /// <para>配置：SchedulingMode=DedicatedQueue, MaxConcurrency=1</para>
    /// <para>适用：聊天室、游戏房间、状态机</para>
    /// </remarks>
    public static ServiceExecutionOptions Actor { get; } = new()
    {
        SchedulingMode = ServiceSchedulingMode.DedicatedQueue,
        MaxConcurrency = 1,
        QueueCapacity = 10000,
        BackpressureMode = ServiceBackpressureMode.Block,
        EnableYielding = false
    };

    /// <summary>
    /// 默认配置（Actor 模式）
    /// </summary>
    public static ServiceExecutionOptions Default { get; } = Actor;

    /// <summary>
    /// 无状态 IO 密集
    /// </summary>
    /// <remarks>
    /// <para>配置：SchedulingMode=DefaultPool, MaxConcurrency=16</para>
    /// <para>适用：数据库查询、HTTP 网关、缓存服务</para>
    /// </remarks>
    public static ServiceExecutionOptions StatelessIO { get; } = new()
    {
        SchedulingMode = ServiceSchedulingMode.DefaultPool,
        MaxConcurrency = 16,
        QueueCapacity = 10000,
        BackpressureMode = ServiceBackpressureMode.Block,
        EnableYielding = false
    };

    /// <summary>
    /// 无状态 CPU 密集
    /// </summary>
    /// <remarks>
    /// <para>配置：SchedulingMode=DefaultPool, MaxConcurrency=CPU cores</para>
    /// <para>适用：寻路计算、加密解密、数据处理</para>
    /// </remarks>
    public static ServiceExecutionOptions StatelessCPU { get; } = new()
    {
        SchedulingMode = ServiceSchedulingMode.DefaultPool,
        MaxConcurrency = Environment.ProcessorCount,
        QueueCapacity = 5000,
        BackpressureMode = ServiceBackpressureMode.Block,
        EnableYielding = false
    };

    /// <summary>
    /// 有状态 IO 密集（启用让出）
    /// </summary>
    /// <remarks>
    /// <para>配置：SchedulingMode=DedicatedQueue, EnableYielding=true</para>
    /// <para>适用：玩家服务、订单处理、会话管理</para>
    /// <para>核心：await 时让出队列，IO 完成后恢复</para>
    /// </remarks>
    public static ServiceExecutionOptions StatefulIO { get; } = new()
    {
        SchedulingMode = ServiceSchedulingMode.DedicatedQueue,
        MaxConcurrency = 1,
        QueueCapacity = 10000,
        BackpressureMode = ServiceBackpressureMode.Block,
        EnableYielding = true
    };

    /// <summary>
    /// 高频更新（线程亲和）
    /// </summary>
    /// <remarks>
    /// <para>配置：SchedulingMode=ThreadAffinity, MaxConcurrency=1</para>
    /// <para>适用：高频状态更新、实时数据聚合</para>
    /// <para>优点：利用 CPU 缓存局部性</para>
    /// </remarks>
    public static ServiceExecutionOptions HighFrequency { get; } = new()
    {
        SchedulingMode = ServiceSchedulingMode.ThreadAffinity,
        MaxConcurrency = 1,
        QueueCapacity = 10000,
        BackpressureMode = ServiceBackpressureMode.Block,
        EnableYielding = false
    };

    /// <summary>
    /// 日志收集（丢弃旧消息）
    /// </summary>
    /// <remarks>
    /// <para>配置：BackpressureMode=DropOldest</para>
    /// <para>适用：日志收集、监控数据、缓存更新通知</para>
    /// <para>特点：队列满时丢弃最旧消息，保证最新数据</para>
    /// </remarks>
    public static ServiceExecutionOptions LogCollector { get; } = new()
    {
        SchedulingMode = ServiceSchedulingMode.DedicatedQueue,
        MaxConcurrency = 1,
        QueueCapacity = 5000,
        BackpressureMode = ServiceBackpressureMode.DropOldest,
        EnableYielding = false
    };

    /// <summary>
    /// 关键业务（拒绝过载）
    /// </summary>
    /// <remarks>
    /// <para>配置：BackpressureMode=ThrowException, QueueCapacity=100</para>
    /// <para>适用：支付服务、关键业务操作</para>
    /// <para>特点：队列满时抛出异常，让调用者重试或降级</para>
    /// </remarks>
    public static ServiceExecutionOptions Critical { get; } = new()
    {
        SchedulingMode = ServiceSchedulingMode.DedicatedQueue,
        MaxConcurrency = 1,
        QueueCapacity = 100,
        BackpressureMode = ServiceBackpressureMode.ThrowException,
        EnableYielding = false
    };

    // ════════════════════════════════════════════════════════════════════════
    // 工厂方法
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 从场景枚举创建配置
    /// </summary>
    /// <param name="scenario">服务场景</param>
    /// <returns>对应场景的执行配置</returns>
    /// <exception cref="ArgumentOutOfRangeException">未知的场景枚举值</exception>
    public static ServiceExecutionOptions FromScenario(ServiceScenario scenario) => scenario switch
    {
        ServiceScenario.Actor => Actor,
        ServiceScenario.StatelessIO => StatelessIO,
        ServiceScenario.StatelessCPU => StatelessCPU,
        ServiceScenario.StatefulIO => StatefulIO,
        ServiceScenario.HighFrequency => HighFrequency,
        ServiceScenario.Custom => new ServiceExecutionOptions(),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown service scenario")
    };

    /// <summary>
    /// 从 Attribute 创建配置
    /// </summary>
    /// <param name="attribute">服务特性</param>
    /// <returns>执行配置</returns>
    public static ServiceExecutionOptions FromAttribute(PulseServiceAttribute? attribute)
    {
        if (attribute == null)
            return Actor;

        // 如果指定了场景（非 Custom），使用预定义配置
        if (attribute.Scenario != ServiceScenario.Custom)
            return FromScenario(attribute.Scenario);

        // 自定义配置：从 Attribute 读取各个参数
        return new ServiceExecutionOptions
        {
            SchedulingMode = attribute.SchedulingMode,
            MaxConcurrency = attribute.MaxConcurrency > 0 ? attribute.MaxConcurrency : 1,
            QueueCapacity = attribute.QueueCapacity > 0 ? attribute.QueueCapacity : 10000,
            BackpressureMode = attribute.BackpressureMode,
            EnableYielding = attribute.EnableYielding
        };
    }

    /// <summary>
    /// 验证配置有效性
    /// </summary>
    /// <exception cref="ArgumentException">配置无效</exception>
    public void Validate()
    {
        if (MaxConcurrency < 0)
            throw new ArgumentException("MaxConcurrency cannot be negative", nameof(MaxConcurrency));

        if (MaxConcurrency > 1000)
            throw new ArgumentException("MaxConcurrency should not exceed 1000", nameof(MaxConcurrency));

        if (QueueCapacity < 1)
            throw new ArgumentException("QueueCapacity must be at least 1", nameof(QueueCapacity));

        if (EnableYielding && SchedulingMode != ServiceSchedulingMode.DedicatedQueue)
        {
            throw new ArgumentException(
                "EnableYielding is only valid for DedicatedQueue scheduling mode",
                nameof(EnableYielding));
        }
    }

    /// <summary>
    /// 克隆配置
    /// </summary>
    public ServiceExecutionOptions Clone() => new()
    {
        SchedulingMode = SchedulingMode,
        MaxConcurrency = MaxConcurrency,
        QueueCapacity = QueueCapacity,
        BackpressureMode = BackpressureMode,
        EnableYielding = EnableYielding
    };

    /// <summary>
    /// 使用修改后的值创建新配置
    /// </summary>
    public ServiceExecutionOptions With(
        ServiceSchedulingMode? schedulingMode = null,
        int? maxConcurrency = null,
        int? queueCapacity = null,
        ServiceBackpressureMode? backpressureMode = null,
        bool? enableYielding = null) => new()
    {
        SchedulingMode = schedulingMode ?? SchedulingMode,
        MaxConcurrency = maxConcurrency ?? MaxConcurrency,
        QueueCapacity = queueCapacity ?? QueueCapacity,
        BackpressureMode = backpressureMode ?? BackpressureMode,
        EnableYielding = enableYielding ?? EnableYielding
    };

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"ServiceExecutionOptions {{ " +
               $"SchedulingMode={SchedulingMode}, " +
               $"MaxConcurrency={MaxConcurrency}, " +
               $"QueueCapacity={QueueCapacity}, " +
               $"BackpressureMode={BackpressureMode}, " +
               $"EnableYielding={EnableYielding} }}";
    }
}
