using PulseRPC.Scheduling;

namespace PulseRPC.Server.Scheduling;

/// <summary>
/// 线程亲和性调度器接口
/// </summary>
/// <remarks>
/// <para>
/// 用于将服务调用调度到固定的工作线程执行，保证同一 ServiceId 的请求始终在同一线程处理。
/// </para>
/// <para>
/// <strong>使用场景</strong>：
/// </para>
/// <list type="bullet">
/// <item><description>高频更新的有状态服务</description></item>
/// <item><description>需要利用 CPU 缓存局部性的场景</description></item>
/// <item><description>需要线程安全但不想使用锁的场景</description></item>
/// </list>
/// <para>
/// <strong>与 DedicatedQueue 的区别</strong>：
/// </para>
/// <list type="bullet">
/// <item><description>DedicatedQueue：每个服务实例有私有队列和后台线程</description></item>
/// <item><description>ThreadAffinity：多个服务实例共享固定数量的工作线程，通过一致性哈希分配</description></item>
/// </list>
/// </remarks>
public interface IThreadAffinityScheduler
{
    /// <summary>
    /// 将工作调度到指定服务的亲和线程执行
    /// </summary>
    /// <param name="key">服务调度键（ServiceType + ServiceId）</param>
    /// <param name="work">要执行的异步工作</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    /// <remarks>
    /// <para>
    /// 使用一致性哈希算法将 ServiceId 映射到固定的工作线程。
    /// 同一 ServiceId 的请求始终路由到同一线程，保证顺序执行。
    /// </para>
    /// </remarks>
    Task ScheduleAsync(
        ServiceSchedulingKey key,
        Func<Task> work,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 将工作调度到指定服务的亲和线程执行并返回结果
    /// </summary>
    /// <typeparam name="T">返回值类型</typeparam>
    /// <param name="key">服务调度键（ServiceType + ServiceId）</param>
    /// <param name="work">要执行的异步工作</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工作执行的结果</returns>
    Task<T> ScheduleAsync<T>(
        ServiceSchedulingKey key,
        Func<Task<T>> work,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 调度器是否已初始化并可接受请求
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// 获取调度器统计信息
    /// </summary>
    ThreadAffinitySchedulerStatistics GetStatistics();
}

/// <summary>
/// 线程亲和性调度器统计信息
/// </summary>
public sealed class ThreadAffinitySchedulerStatistics
{
    /// <summary>
    /// 工作线程数量
    /// </summary>
    public int WorkerThreadCount { get; init; }

    /// <summary>
    /// 当前排队的任务总数
    /// </summary>
    public int TotalQueuedTasks { get; init; }

    /// <summary>
    /// 已调度的任务总数
    /// </summary>
    public long TotalScheduledTasks { get; init; }

    /// <summary>
    /// 活跃的亲和性映射数量
    /// </summary>
    public int ActiveAffinityMappings { get; init; }
}
