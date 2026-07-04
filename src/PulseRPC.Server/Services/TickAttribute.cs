using System;

namespace PulseRPC.Server.Services;

/// <summary>
/// 固定帧驱动特性 - 标记在 <see cref="UnifiedPulseServiceBase"/> 派生的服务类上，
/// 让运行时以固定频率周期性地驱动该服务。
/// </summary>
/// <remarks>
/// <para>
/// 对应「契约即接口·HubActor 统一模型」§4.2 声明式注解中的 <c>[Tick(hz)]</c>：
/// 把「固定帧驱动」这一 Actor 关注点从手写定时器退化为一条声明。
/// </para>
/// <para>
/// <strong>运行语义</strong>：
/// </para>
/// <list type="bullet">
/// <item><description>服务 <see cref="UnifiedPulseServiceBase.StartAsync"/> 后，框架按 <see cref="Hz"/>
/// 指定的频率周期性地把一次回调投递到服务的串行邮箱，调用
/// <see cref="UnifiedPulseServiceBase.OnTickAsync"/>；</description></item>
/// <item><description>回调经由邮箱执行，因此与其它消息处理<b>串行</b>、天然线程安全
/// （<see cref="ServiceSchedulingMode.DedicatedQueue"/> / <see cref="ServiceSchedulingMode.ThreadAffinity"/> 模式）；</description></item>
/// <item><description>每次 tick 作为一次「系统定时器」调用执行，携带
/// <c>CallSourceType.SystemTimer</c> 上下文，接入既有权限设计（<c>RequirePermission</c>/<c>RequireRole</c>
/// 的 <c>AllowSystem</c> 绕过、<c>ClientFacingGate</c> 对非外部来源放行）；</description></item>
/// <item><description>采用「尽力而为」语义：若某次 <see cref="UnifiedPulseServiceBase.OnTickAsync"/>
/// 执行耗时超过一个周期，错过的帧不会补偿堆积，而是在下一个周期边界继续；</description></item>
/// <item><description>服务 <see cref="UnifiedPulseServiceBase.StopAsync"/> 时自动停止驱动。</description></item>
/// </list>
/// <para>
/// <strong>使用示例</strong>：
/// </para>
/// <code>
/// [PulseService(Scenario = ServiceScenario.Actor)]
/// [Tick(30)] // 每秒 30 帧
/// public class SceneService : UnifiedPulseServiceBase
/// {
///     protected override Task OnTickAsync(CancellationToken cancellationToken)
///     {
///         // 每帧推进世界状态（在串行邮箱内执行，无需加锁）
///         return Task.CompletedTask;
///     }
/// }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class TickAttribute : Attribute
{
    /// <summary>
    /// 驱动频率（赫兹，每秒帧数）。必须为正数。
    /// </summary>
    /// <remarks>
    /// 周期 = 1 / <see cref="Hz"/> 秒。例如 <c>30</c> 表示每秒 30 帧（约 33.3ms 一帧），
    /// <c>0.5</c> 表示每 2 秒一帧。
    /// </remarks>
    public double Hz { get; }

    /// <summary>
    /// 以指定频率创建固定帧驱动特性。
    /// </summary>
    /// <param name="hz">驱动频率（赫兹，每秒帧数），必须大于 0。</param>
    /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="hz"/> 不是正数时抛出。</exception>
    public TickAttribute(double hz)
    {
        if (hz <= 0 || double.IsNaN(hz) || double.IsInfinity(hz))
        {
            throw new ArgumentOutOfRangeException(nameof(hz), hz, "Tick 频率必须为正的有限数。");
        }

        Hz = hz;
    }

    /// <summary>
    /// 获取每帧的时间间隔（由 <see cref="Hz"/> 换算而来）。
    /// </summary>
    public TimeSpan Interval => TimeSpan.FromSeconds(1.0 / Hz);
}
