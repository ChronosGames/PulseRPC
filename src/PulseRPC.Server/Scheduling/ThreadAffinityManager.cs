using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Scheduling;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Models;

namespace PulseRPC.Server.Scheduling;

/// <summary>
/// 线程亲和性管理器，维护服务实例到工作线程的映射关系
/// </summary>
/// <remarks>
/// <para>
/// 负责管理服务实例的线程亲和性映射，确保相同 ServiceId 始终路由到同一工作线程。
/// 使用一致性哈希算法分配线程，并定时清理空闲实例以释放内存。
/// </para>
/// <para>
/// <strong>核心功能</strong>：
/// </para>
/// <list type="bullet">
/// <item><description>GetOrCreateAffinity：获取或创建服务实例的线程映射</description></item>
/// <item><description>UpdateLastAccess：更新实例最后访问时间（防止被清理）</description></item>
/// <item><description>CleanupIdleInstances：定时清理空闲实例（默认 1 分钟执行一次）</description></item>
/// </list>
/// <para>
/// <strong>并发安全</strong>：
/// 使用 <see cref="ConcurrentDictionary{TKey, TValue}"/> 存储映射关系，支持高并发读写。
/// </para>
/// </remarks>
public sealed class ThreadAffinityManager : IDisposable
{
    private readonly ConcurrentDictionary<ServiceSchedulingKey, ThreadAffinity> _affinities;
    private readonly ConsistentHashRing _hashRing;
    private readonly ILogger<ThreadAffinityManager> _logger;
    private readonly ServiceSchedulingOptions _options;
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    /// <summary>
    /// 创建线程亲和性管理器
    /// </summary>
    /// <param name="hashRing">一致性哈希环实例</param>
    /// <param name="options">调度器配置选项</param>
    /// <param name="logger">日志记录器</param>
    public ThreadAffinityManager(
        ConsistentHashRing hashRing,
        IOptions<ServiceSchedulingOptions> options,
        ILogger<ThreadAffinityManager> logger)
    {
        _hashRing = hashRing ?? throw new ArgumentNullException(nameof(hashRing));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _affinities = new ConcurrentDictionary<ServiceSchedulingKey, ThreadAffinity>();

        // 启动定时清理任务（每 1 分钟执行一次）
        _cleanupTimer = new Timer(
            CleanupIdleInstances,
            state: null,
            dueTime: TimeSpan.FromMinutes(1),
            period: TimeSpan.FromMinutes(1));

        _logger.LogInformation(
            "ThreadAffinityManager initialized with {ThreadCount} threads, {VirtualNodes} virtual nodes/thread, {IdleTimeout} idle timeout",
            _hashRing.TotalThreads,
            _hashRing.TotalVirtualNodes / _hashRing.TotalThreads,
            _options.IdleInstanceTimeout);
    }

    /// <summary>
    /// 获取或创建服务实例的线程亲和性映射
    /// </summary>
    /// <param name="key">服务调度键</param>
    /// <returns>线程亲和性记录</returns>
    public ThreadAffinity GetOrCreateAffinity(ServiceSchedulingKey key)
    {
        return _affinities.GetOrAdd(key, CreateAffinity);
    }

    /// <summary>
    /// 更新服务实例的最后访问时间
    /// </summary>
    /// <param name="key">服务调度键</param>
    public void UpdateLastAccess(ServiceSchedulingKey key)
    {
        if (_affinities.TryGetValue(key, out var affinity))
        {
            affinity.LastAccessUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 创建新的线程亲和性记录
    /// </summary>
    /// <param name="key">服务调度键</param>
    /// <returns>新的线程亲和性记录</returns>
    private ThreadAffinity CreateAffinity(ServiceSchedulingKey key)
    {
        var threadId = _hashRing.GetThread(key.ServiceId);

        var affinity = new ThreadAffinity
        {
            Key = key,
            AssignedThreadId = threadId
        };

        _logger.LogDebug(
            "Service instance {ServiceName}:{ServiceId} assigned to thread {ThreadId}",
            key.ServiceName,
            key.ServiceId,
            threadId);

        return affinity;
    }

    /// <summary>
    /// 定时清理空闲的服务实例映射
    /// </summary>
    /// <param name="state">定时器状态（未使用）</param>
    private void CleanupIdleInstances(object? state)
    {
        if (_disposed)
        {
            return;
        }

        var currentUtc = DateTime.UtcNow;
        var removedCount = 0;

        foreach (var kvp in _affinities)
        {
            var affinity = kvp.Value;
            if (affinity.IsIdle(currentUtc, _options.IdleInstanceTimeout))
            {
                if (_affinities.TryRemove(kvp.Key, out _))
                {
                    removedCount++;
                    _logger.LogDebug(
                        "Removed idle service instance {ServiceName}:{ServiceId} (idle for {IdleDuration})",
                        affinity.Key.ServiceName,
                        affinity.Key.ServiceId,
                        affinity.IdleDuration(currentUtc));
                }
            }
        }

        if (removedCount > 0)
        {
            _logger.LogInformation(
                "Cleaned up {RemovedCount} idle service instances (active: {ActiveCount})",
                removedCount,
                _affinities.Count);
        }
    }

    /// <summary>
    /// 获取当前活跃的服务实例数量
    /// </summary>
    public int ActiveInstanceCount => _affinities.Count;

    /// <summary>
    /// 获取所有线程亲和性映射（只读副本）
    /// </summary>
    /// <returns>线程亲和性映射集合</returns>
    public IReadOnlyDictionary<ServiceSchedulingKey, ThreadAffinity> GetAllAffinities()
    {
        return _affinities;
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _cleanupTimer.Dispose();
        _disposed = true;

        _logger.LogInformation("ThreadAffinityManager disposed");
    }
}
