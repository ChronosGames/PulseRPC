using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Scheduling;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Services;

namespace PulseRPC.Server.Services.Scheduling;

/// <summary>
/// 线程亲和性调度器实现
/// </summary>
/// <remarks>
/// <para>
/// 使用一致性哈希算法将服务调用分配到固定的工作线程，保证同一 ServiceId 的请求
/// 始终在同一线程执行，利用 CPU 缓存局部性提高性能。
/// </para>
/// <para>
/// <strong>核心组件</strong>：
/// </para>
/// <list type="bullet">
/// <item><description><see cref="ConsistentHashRing"/>：一致性哈希环，将 ServiceId 映射到线程</description></item>
/// <item><description><see cref="ServiceThreadPool"/>：工作线程池，管理专属工作线程</description></item>
/// <item><description><see cref="ThreadAffinityManager"/>：亲和性管理器，维护映射缓存和空闲清理</description></item>
/// </list>
/// </remarks>
public sealed class ThreadAffinityScheduler : IThreadAffinityScheduler, IHostedService, IAsyncDisposable
{
    private readonly ConsistentHashRing _hashRing;
    private readonly ThreadAffinityManager _affinityManager;
    private readonly ServiceSchedulingOptions _options;
    private readonly ILogger<ThreadAffinityScheduler> _logger;

    private ServiceThreadPool? _threadPool;
    private bool _isInitialized;
    private bool _isDisposed;
    private long _totalScheduledTasks;

    /// <summary>
    /// 创建线程亲和性调度器
    /// </summary>
    /// <param name="hashRing">一致性哈希环</param>
    /// <param name="affinityManager">线程亲和性管理器</param>
    /// <param name="options">服务调度选项</param>
    /// <param name="logger">日志记录器</param>
    public ThreadAffinityScheduler(
        ConsistentHashRing hashRing,
        ThreadAffinityManager affinityManager,
        IOptions<ServiceSchedulingOptions> options,
        ILogger<ThreadAffinityScheduler> logger)
    {
        _hashRing = hashRing ?? throw new ArgumentNullException(nameof(hashRing));
        _affinityManager = affinityManager ?? throw new ArgumentNullException(nameof(affinityManager));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public bool IsInitialized => _isInitialized;

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized)
        {
            _logger.LogWarning("ThreadAffinityScheduler is already initialized");
            return;
        }

        _logger.LogInformation(
            "Starting ThreadAffinityScheduler with {ThreadCount} worker threads",
            _options.WorkerThreadCount);

        // 创建调度器配置
        var schedulerConfig = new SchedulerConfiguration
        {
            InitialThreadCount = _options.WorkerThreadCount,
            MaxThreadCount = _options.WorkerThreadCount,
            ChannelCapacity = 1024,
            EnableMetrics = true
        };

        // 创建并初始化线程池
        _threadPool = new ServiceThreadPool(schedulerConfig, _logger);
        await _threadPool.InitializeAsync(cancellationToken);

        _isInitialized = true;

        _logger.LogInformation("ThreadAffinityScheduler started successfully");
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_isInitialized)
        {
            return;
        }

        _logger.LogInformation("Stopping ThreadAffinityScheduler");

        _isInitialized = false;

        if (_threadPool != null)
        {
            await _threadPool.DisposeAsync();
            _threadPool = null;
        }

        _logger.LogInformation(
            "ThreadAffinityScheduler stopped. Total scheduled tasks: {TotalTasks}",
            Interlocked.Read(ref _totalScheduledTasks));
    }

    /// <inheritdoc/>
    public async Task ScheduleAsync(
        ServiceSchedulingKey key,
        Func<Task> work,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        // 更新亲和性访问时间
        _affinityManager.UpdateLastAccess(key);

        // 使用 PooledValueTaskSource 等待任务完成
        var completionSource = PooledValueTaskSource.Create();

        var workItem = new WorkItem(key, async () =>
        {
            try
            {
                await work();
                completionSource.TrySetResult();
            }
            catch (Exception ex)
            {
                completionSource.TrySetException(ex);
            }
        });

        await _threadPool!.EnqueueWorkAsync(workItem, cancellationToken);

        Interlocked.Increment(ref _totalScheduledTasks);

        // 等待任务完成
        await completionSource.GetValueTask();
    }

    /// <inheritdoc/>
    public async Task<T> ScheduleAsync<T>(
        ServiceSchedulingKey key,
        Func<Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        // 更新亲和性访问时间
        _affinityManager.UpdateLastAccess(key);

        // 使用 PooledValueTaskSource 等待任务完成
        var completionSource = PooledValueTaskSource<T>.Create();

        var workItem = new WorkItem(key, async () =>
        {
            try
            {
                var result = await work();
                completionSource.TrySetResult(result);
            }
            catch (Exception ex)
            {
                completionSource.TrySetException(ex);
            }
        });

        await _threadPool!.EnqueueWorkAsync(workItem, cancellationToken);

        Interlocked.Increment(ref _totalScheduledTasks);

        // 等待任务完成并返回结果
        return await completionSource.GetValueTask();
    }

    /// <inheritdoc/>
    public ThreadAffinitySchedulerStatistics GetStatistics()
    {
        var metrics = _threadPool?.GetAggregatedMetrics();

        return new ThreadAffinitySchedulerStatistics
        {
            WorkerThreadCount = _threadPool?.ThreadCount ?? 0,
            TotalQueuedTasks = metrics?.TotalQueuedMessages ?? 0,
            TotalScheduledTasks = Interlocked.Read(ref _totalScheduledTasks),
            ActiveAffinityMappings = _affinityManager.ActiveInstanceCount
        };
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        await StopAsync(CancellationToken.None);
        _isDisposed = true;
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized || _threadPool == null)
        {
            throw new InvalidOperationException(
                "ThreadAffinityScheduler is not initialized. " +
                "Ensure the scheduler is started before scheduling work.");
        }

        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(ThreadAffinityScheduler));
        }
    }
}
