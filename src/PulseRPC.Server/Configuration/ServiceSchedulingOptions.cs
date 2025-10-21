namespace PulseRPC.Server.Configuration;

/// <summary>
/// 服务实例调度器配置选项
/// </summary>
/// <remarks>
/// 用于配置 ServiceThreadScheduler 的线程池大小、空闲超时和一致性哈希参数。
/// 这些配置影响服务实例的线程分配策略和资源管理。
/// </remarks>
public sealed class ServiceSchedulingOptions
{
    /// <summary>
    /// 工作线程数量
    /// </summary>
    /// <remarks>
    /// <para>默认值：Environment.ProcessorCount（CPU 核心数）</para>
    /// <para>有效范围：1 到 64</para>
    /// <para>
    /// 调优建议：
    /// - CPU 密集型负载：Environment.ProcessorCount
    /// - I/O 密集型负载：Environment.ProcessorCount * 2 到 * 4
    /// - 最大限制：64（避免上下文切换开销）
    /// </para>
    /// </remarks>
    public int WorkerThreadCount { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// 服务实例空闲超时时间
    /// </summary>
    /// <remarks>
    /// <para>默认值：5 分钟</para>
    /// <para>
    /// 服务实例在此时间内未接收请求将被视为空闲，其线程亲和性映射将被清理。
    /// 下次请求时将重新建立线程映射。
    /// </para>
    /// <para>
    /// 调优建议：
    /// - 频繁访问的服务：增加超时时间（如 10 分钟）减少重新映射开销
    /// - 短期任务服务：减少超时时间（如 1 分钟）加快资源回收
    /// </para>
    /// </remarks>
    public TimeSpan IdleInstanceTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 一致性哈希环的虚拟节点数量（每个工作线程）
    /// </summary>
    /// <remarks>
    /// <para>默认值：150</para>
    /// <para>有效范围：50 到 500</para>
    /// <para>
    /// 虚拟节点影响负载分布均匀性：
    /// - 50 节点：标准差 ~5%，内存开销最小
    /// - 150 节点：标准差 ~2.1%，平衡性能和分布质量（推荐）
    /// - 200 节点：标准差 ~1.5%，更均匀但内存开销增加
    /// </para>
    /// </remarks>
    public int VirtualNodesPerThread { get; set; } = 150;

    /// <summary>
    /// 验证配置有效性
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">当配置参数超出有效范围时抛出</exception>
    public void Validate()
    {
        if (WorkerThreadCount < 1 || WorkerThreadCount > 64)
        {
            throw new ArgumentOutOfRangeException(
                nameof(WorkerThreadCount),
                WorkerThreadCount,
                "工作线程数量必须在 1 到 64 之间");
        }

        if (IdleInstanceTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(IdleInstanceTimeout),
                IdleInstanceTimeout,
                "空闲超时时间不能为负数");
        }

        if (VirtualNodesPerThread < 50 || VirtualNodesPerThread > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(VirtualNodesPerThread),
                VirtualNodesPerThread,
                "虚拟节点数量必须在 50 到 500 之间");
        }
    }
}
