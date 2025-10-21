namespace PulseRPC.Server.Scheduling;

/// <summary>
/// 服务实例调度器接口
/// </summary>
/// <remarks>
/// 负责将服务实例的工作项调度到专用线程，确保同一 ServiceId 的所有请求
/// 在同一线程上顺序执行，实现线程亲和性和故障隔离。
///
/// 此接口为契约定义，实际实现位于 src/PulseRPC.Server/Scheduling/ServiceThreadScheduler.cs
/// </remarks>
public interface IServiceScheduler
{
    /// <summary>
    /// 启动调度器并初始化工作线程池
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>启动任务</returns>
    /// <exception cref="InvalidOperationException">调度器已启动</exception>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止调度器并优雅关闭所有工作线程
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>停止任务</returns>
    /// <remarks>
    /// 等待所有进行中的工作项完成后再返回
    /// </remarks>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 调度服务实例的工作项到专用线程
    /// </summary>
    /// <param name="key">服务调度键（ServiceName + ServiceId）</param>
    /// <param name="work">要执行的异步工作项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>调度任务</returns>
    /// <remarks>
    /// 保证相同 ServiceSchedulingKey 的所有工作项在同一线程上顺序执行。
    /// 使用一致性哈希算法将服务实例映射到工作线程。
    /// </remarks>
    /// <exception cref="InvalidOperationException">调度器未启动</exception>
    /// <exception cref="ArgumentNullException">key 或 work 为 null</exception>
    /// <exception cref="ArgumentException">ServiceId 为空或无效</exception>
    Task ScheduleAsync(
        ServiceSchedulingKey key,
        Func<Task> work,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取调度器运行时指标
    /// </summary>
    /// <returns>调度器指标对象</returns>
    /// <remarks>
    /// 包含以下信息:
    /// - 活跃服务实例数量
    /// - 每个线程的工作负载
    /// - 队列深度
    /// - 总调度次数
    /// </remarks>
    SchedulerMetrics GetMetrics();

    /// <summary>
    /// 获取调度器是否正在运行
    /// </summary>
    bool IsRunning { get; }
}

/// <summary>
/// 调度器运行时指标
/// </summary>
public sealed class SchedulerMetrics
{
    /// <summary>
    /// 活跃服务实例总数
    /// </summary>
    public int ActiveInstances { get; init; }

    /// <summary>
    /// 工作线程总数
    /// </summary>
    public int TotalThreads { get; init; }

    /// <summary>
    /// 每个线程的工作负载（实例数）
    /// </summary>
    public IReadOnlyDictionary<int, int> ThreadWorkloads { get; init; } = new Dictionary<int, int>();

    /// <summary>
    /// 总队列深度（所有线程的待处理工作项总数）
    /// </summary>
    public int TotalQueueDepth { get; init; }

    /// <summary>
    /// 自启动以来的总调度次数
    /// </summary>
    public long TotalScheduledCount { get; init; }

    /// <summary>
    /// 平均每线程负载
    /// </summary>
    public double AverageLoadPerThread => TotalThreads > 0
        ? (double)ActiveInstances / TotalThreads
        : 0.0;
}
