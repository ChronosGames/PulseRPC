namespace PulseRPC.Server.Abstractions;

/// <summary>
/// 服务执行场景 - 预定义的最佳配置组合
/// </summary>
/// <remarks>
/// <para>
/// 通过选择场景，自动获得针对该场景优化的配置，无需手动设置每个参数。
/// </para>
/// <para>
/// <strong>场景选择指南</strong>：
/// </para>
/// <list type="bullet">
/// <item><description><see cref="Actor"/>：有状态服务，需要顺序执行（聊天室、游戏房间）</description></item>
/// <item><description><see cref="StatelessIO"/>：无状态 IO 密集服务（数据库查询、HTTP 网关）</description></item>
/// <item><description><see cref="StatelessCPU"/>：无状态 CPU 密集服务（寻路计算、加密解密）</description></item>
/// <item><description><see cref="StatefulIO"/>：有状态但包含大量 IO 操作（玩家服务、订单处理）</description></item>
/// <item><description><see cref="HighFrequency"/>：高频更新服务，利用 CPU 缓存局部性</description></item>
/// <item><description><see cref="Custom"/>：特殊场景，手动配置所有参数</description></item>
/// </list>
/// </remarks>
public enum ServiceScenario
{
    /// <summary>
    /// 自定义配置 - 使用 Attribute 中的详细配置项
    /// </summary>
    /// <remarks>
    /// 当预定义场景无法满足需求时，选择此项并手动配置：
    /// <list type="bullet">
    /// <item><description>SchedulingMode</description></item>
    /// <item><description>MaxConcurrency</description></item>
    /// <item><description>QueueCapacity</description></item>
    /// <item><description>BackpressureMode</description></item>
    /// <item><description>EnableYielding</description></item>
    /// </list>
    /// </remarks>
    Custom = 0,

    /// <summary>
    /// Actor 模式 - 单线程顺序执行，适合有状态服务
    /// </summary>
    /// <remarks>
    /// <para><strong>配置</strong>：</para>
    /// <list type="bullet">
    /// <item><description>SchedulingMode = DedicatedQueue</description></item>
    /// <item><description>MaxConcurrency = 1</description></item>
    /// <item><description>EnableYielding = false</description></item>
    /// </list>
    /// <para><strong>适用场景</strong>：</para>
    /// <list type="bullet">
    /// <item><description>聊天室服务</description></item>
    /// <item><description>游戏房间服务</description></item>
    /// <item><description>状态机驱动的业务</description></item>
    /// <item><description>需要严格消息顺序的服务</description></item>
    /// </list>
    /// <para><strong>优点</strong>：天然线程安全，无需加锁</para>
    /// </remarks>
    Actor = 1,

    /// <summary>
    /// 无状态 IO 密集 - 高并发 IO 操作
    /// </summary>
    /// <remarks>
    /// <para><strong>配置</strong>：</para>
    /// <list type="bullet">
    /// <item><description>SchedulingMode = DefaultPool</description></item>
    /// <item><description>MaxConcurrency = 16</description></item>
    /// <item><description>EnableYielding = false（不需要，DefaultPool 本身就是并发的）</description></item>
    /// </list>
    /// <para><strong>适用场景</strong>：</para>
    /// <list type="bullet">
    /// <item><description>数据库查询服务</description></item>
    /// <item><description>HTTP API 网关</description></item>
    /// <item><description>缓存读取服务</description></item>
    /// <item><description>无状态的纯查询操作</description></item>
    /// </list>
    /// <para><strong>注意</strong>：服务实现必须是线程安全的</para>
    /// </remarks>
    StatelessIO = 2,

    /// <summary>
    /// 无状态 CPU 密集 - CPU 计算任务
    /// </summary>
    /// <remarks>
    /// <para><strong>配置</strong>：</para>
    /// <list type="bullet">
    /// <item><description>SchedulingMode = DefaultPool</description></item>
    /// <item><description>MaxConcurrency = Environment.ProcessorCount</description></item>
    /// <item><description>EnableYielding = false</description></item>
    /// </list>
    /// <para><strong>适用场景</strong>：</para>
    /// <list type="bullet">
    /// <item><description>寻路计算服务</description></item>
    /// <item><description>加密解密服务</description></item>
    /// <item><description>数据处理服务</description></item>
    /// <item><description>图像/音频处理</description></item>
    /// </list>
    /// <para><strong>注意</strong>：并发度设为 CPU 核心数，避免过度竞争</para>
    /// </remarks>
    StatelessCPU = 3,

    /// <summary>
    /// 有状态 IO 密集 - 有状态但包含大量 IO
    /// </summary>
    /// <remarks>
    /// <para><strong>配置</strong>：</para>
    /// <list type="bullet">
    /// <item><description>SchedulingMode = DedicatedQueue</description></item>
    /// <item><description>MaxConcurrency = 1</description></item>
    /// <item><description>EnableYielding = true（关键！）</description></item>
    /// </list>
    /// <para><strong>适用场景</strong>：</para>
    /// <list type="bullet">
    /// <item><description>玩家服务（有状态 + 数据库操作）</description></item>
    /// <item><description>订单处理服务</description></item>
    /// <item><description>会话管理服务</description></item>
    /// <item><description>带状态的业务流程</description></item>
    /// </list>
    /// <para><strong>核心机制</strong>：</para>
    /// <para>
    /// await 时自动让出队列执行权，IO 完成后重新排队恢复执行。
    /// 既保证了线程安全（单线程），又不阻塞其他消息的处理。
    /// </para>
    /// </remarks>
    StatefulIO = 4,

    /// <summary>
    /// 高频更新 - 利用 CPU 缓存局部性
    /// </summary>
    /// <remarks>
    /// <para><strong>配置</strong>：</para>
    /// <list type="bullet">
    /// <item><description>SchedulingMode = ThreadAffinity</description></item>
    /// <item><description>MaxConcurrency = 1</description></item>
    /// <item><description>EnableYielding = false</description></item>
    /// </list>
    /// <para><strong>适用场景</strong>：</para>
    /// <list type="bullet">
    /// <item><description>高频状态更新服务</description></item>
    /// <item><description>实时数据聚合</description></item>
    /// <item><description>热点数据处理</description></item>
    /// </list>
    /// <para><strong>优点</strong>：</para>
    /// <para>
    /// 同一 ServiceId 总是在同一线程执行，利用 CPU L1/L2 缓存，
    /// 减少缓存失效，提高性能。
    /// </para>
    /// </remarks>
    HighFrequency = 5
}
