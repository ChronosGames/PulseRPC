using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PulseRPC.Server.Services.Scheduling;

/// <summary>
/// 会话上下文 - 维护连接相关状态
/// </summary>
public sealed class SessionContext
{
    public string SessionId { get; }
    public int PreferredThreadId { get; set; }
    public DateTime LastAccessTime { get; set; }
    public long MessageCount;
    public readonly ConcurrentDictionary<string, object> State = new();

    public SessionContext(string sessionId)
    {
        SessionId = sessionId;
        LastAccessTime = DateTime.UtcNow;
        PreferredThreadId = -1;
    }

    public void UpdateAccess()
    {
        LastAccessTime = DateTime.UtcNow;
        Interlocked.Increment(ref MessageCount);
    }
}

/// <summary>
/// 消息任务包装器
/// </summary>
public readonly struct AffinityMessageTask
{
    public readonly string SessionId;
    public readonly Func<CancellationToken, ValueTask> Handler;
    public readonly MessagePriority Priority;
    public readonly DateTime ScheduledTime;
    public readonly TaskCompletionSource<object?> Completion;

    public AffinityMessageTask(string sessionId, Func<CancellationToken, ValueTask> handler,
        MessagePriority priority, TaskCompletionSource<object?> completion)
    {
        SessionId = sessionId;
        Handler = handler;
        Priority = priority;
        ScheduledTime = DateTime.UtcNow;
        Completion = completion;
    }
}

/// <summary>
/// 一致性哈希环 - 用于分配线程亲和性
/// </summary>
public sealed class ConsistentHash<T> where T : notnull
{
    private readonly SortedDictionary<uint, T> _ring = new();
    private readonly int _virtualNodes;

    public ConsistentHash(int virtualNodes = 100)
    {
        _virtualNodes = virtualNodes;
    }

    public void AddNode(T node)
    {
        for (var i = 0; i < _virtualNodes; i++)
        {
            var hash = ComputeHash($"{node}:{i}");
            _ring[hash] = node;
        }
    }

    public void RemoveNode(T node)
    {
        for (var i = 0; i < _virtualNodes; i++)
        {
            var hash = ComputeHash($"{node}:{i}");
            _ring.Remove(hash);
        }
    }

    public T GetNode(string key)
    {
        if (_ring.Count == 0)
            throw new InvalidOperationException("哈希环为空");

        var hash = ComputeHash(key);

        // 找到第一个大于等于hash的节点
        foreach (var kvp in _ring)
        {
            if (kvp.Key >= hash)
                return kvp.Value;
        }

        // 如果没有找到，返回环上的第一个节点（环形特性）
        return _ring.First().Value;
    }

    private static uint ComputeHash(string input)
    {
        // 简单的哈希函数 - 生产环境可以使用更好的哈希算法
        unchecked
        {
            uint hash = 2166136261u;
            foreach (var c in input)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return hash;
        }
    }
}

/// <summary>
/// 亲和性感知调度器 - 相同连接的消息在同一线程处理，优化缓存局部性
/// </summary>
public sealed class AffinityAwareScheduler : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly AffinitySchedulerOptions _options;
    private readonly ConsistentHash<int> _threadAffinity;

    // 工作线程
    private readonly WorkerThread[] _workers;
    private readonly Thread[] _workerThreads;
    private volatile bool _disposed;
    private readonly CancellationTokenSource _cancellationTokenSource;

    // 会话管理
    private readonly ConcurrentDictionary<string, SessionContext> _sessionCache;
    private readonly Timer _cleanupTimer;

    // 负载均衡
    private readonly long[] _workerMessageCounts;
    private readonly long[] _workerProcessingTimes;

    // 统计信息
    private long _totalScheduled;
    private long _totalCompleted;
    private long _totalFailed;
    private long _affinityHits;
    private long _affinityMisses;

    /// <summary>
    /// 亲和性调度选项
    /// </summary>
    public sealed class AffinitySchedulerOptions
    {
        /// <summary>工作线程数量</summary>
        public int WorkerThreadCount { get; set; } = Environment.ProcessorCount;

        /// <summary>每个工作线程的队列容量</summary>
        public int WorkerQueueCapacity { get; set; } = 1000;

        /// <summary>会话超时时间</summary>
        public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>清理间隔</summary>
        public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>是否启用工作窃取</summary>
        public bool EnableWorkStealing { get; set; } = true;

        /// <summary>工作窃取阈值 - 队列长度差异超过此值时触发</summary>
        public int WorkStealingThreshold { get; set; } = 10;

        /// <summary>工作窃取比例</summary>
        public double WorkStealingRatio { get; set; } = 0.5;

        /// <summary>亲和性强度 - 值越大越倾向于保持亲和性</summary>
        public double AffinityStrength { get; set; } = 0.8;

        /// <summary>负载均衡间隔</summary>
        public TimeSpan LoadBalancingInterval { get; set; } = TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// 工作线程
    /// </summary>
    private sealed class WorkerThread
    {
        public readonly int WorkerId;
        public readonly Channel<AffinityMessageTask> TaskQueue;
        public readonly ChannelWriter<AffinityMessageTask> Writer;
        public readonly ChannelReader<AffinityMessageTask> Reader;

        private volatile int _queueSize;
        private volatile bool _isProcessing;

        public int QueueSize => _queueSize;
        public bool IsProcessing => _isProcessing;

        public WorkerThread(int workerId, int capacity)
        {
            WorkerId = workerId;

            var options = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            };

            TaskQueue = Channel.CreateBounded<AffinityMessageTask>(options);
            Writer = TaskQueue.Writer;
            Reader = TaskQueue.Reader;
        }

        public async ValueTask<bool> TryEnqueueAsync(AffinityMessageTask task, CancellationToken cancellationToken)
        {
            try
            {
                if (Writer.TryWrite(task))
                {
                    Interlocked.Increment(ref _queueSize);
                    return true;
                }

                // 队列已满，尝试异步写入（有超时）
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromMilliseconds(100));

                await Writer.WriteAsync(task, cts.Token);
                Interlocked.Increment(ref _queueSize);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public async Task ProcessTasksAsync(AffinityAwareScheduler scheduler, CancellationToken cancellationToken)
        {
            await foreach (var task in Reader.ReadAllAsync(cancellationToken))
            {
                Interlocked.Decrement(ref _queueSize);
                _isProcessing = true;

                try
                {
                    var startTime = DateTime.UtcNow;

                    await task.Handler(cancellationToken);
                    task.Completion.TrySetResult(null);

                    var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
                    Interlocked.Add(ref scheduler._workerProcessingTimes[WorkerId], processingTime);
                    Interlocked.Increment(ref scheduler._workerMessageCounts[WorkerId]);
                    Interlocked.Increment(ref scheduler._totalCompleted);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    task.Completion.TrySetCanceled(cancellationToken);
                    break;
                }
                catch (Exception ex)
                {
                    task.Completion.TrySetException(ex);
                    Interlocked.Increment(ref scheduler._totalFailed);
                    scheduler._logger.LogError(ex, "处理消息任务异常，工作线程: {WorkerId}", WorkerId);
                }
                finally
                {
                    _isProcessing = false;
                }
            }
        }

        public void Complete()
        {
            Writer.TryComplete();
        }
    }

    /// <summary>
    /// 构造函数
    /// </summary>
    public AffinityAwareScheduler(AffinitySchedulerOptions? options = null, ILogger? logger = null)
    {
        _options = options ?? new AffinitySchedulerOptions();
        _logger = logger ?? NullLogger.Instance;
        _cancellationTokenSource = new CancellationTokenSource();

        // 初始化一致性哈希环
        _threadAffinity = new ConsistentHash<int>();

        // 初始化工作线程
        _workers = new WorkerThread[_options.WorkerThreadCount];
        _workerThreads = new Thread[_options.WorkerThreadCount];
        _workerMessageCounts = new long[_options.WorkerThreadCount];
        _workerProcessingTimes = new long[_options.WorkerThreadCount];

        for (var i = 0; i < _options.WorkerThreadCount; i++)
        {
            _workers[i] = new WorkerThread(i, _options.WorkerQueueCapacity);
            _threadAffinity.AddNode(i);

            var workerId = i; // 捕获循环变量
            _workerThreads[i] = new Thread(() => _ = ProcessWorkerTasks(workerId))
            {
                Name = $"AffinityScheduler-Worker-{i}",
                IsBackground = true
            };
            _workerThreads[i].Start();
        }

        // 初始化会话缓存
        _sessionCache = new ConcurrentDictionary<string, SessionContext>();

        // 启动清理定时器
        _cleanupTimer = new Timer(CleanupExpiredSessions, null,
            _options.CleanupInterval, _options.CleanupInterval);

        _logger.LogInformation("AffinityAwareScheduler已初始化 - 工作线程数: {ThreadCount}, 队列容量: {QueueCapacity}",
            _options.WorkerThreadCount, _options.WorkerQueueCapacity);
    }

    /// <summary>
    /// 调度消息任务 - 主要API
    /// </summary>
    public async Task<T> ScheduleAsync<T>(string sessionId, Func<CancellationToken, ValueTask<T>> handler,
        MessagePriority priority = MessagePriority.Normal, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AffinityAwareScheduler));

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        Func<CancellationToken, ValueTask> wrappedHandler = async (CancellationToken ct) =>
        {
            var result = await handler(ct);
            completion.TrySetResult(result);
        };

        var task = new AffinityMessageTask(sessionId, wrappedHandler, priority, completion);

        var success = await ScheduleTaskAsync(task, cancellationToken);
        if (!success)
        {
            throw new InvalidOperationException("无法调度任务，所有工作队列都已满");
        }

        var result = await completion.Task;
        return (T)result!;
    }

    /// <summary>
    /// 调度消息任务（内部实现）
    /// </summary>
    private async ValueTask<bool> ScheduleTaskAsync(AffinityMessageTask task, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _totalScheduled);

        // 获取或创建会话上下文
        var session = _sessionCache.GetOrAdd(task.SessionId, id => new SessionContext(id));
        session.UpdateAccess();

        // 选择目标工作线程
        var targetWorkerId = SelectWorkerThread(task.SessionId, session);
        var targetWorker = _workers[targetWorkerId];

        // 尝试将任务加入目标队列
        if (await targetWorker.TryEnqueueAsync(task, cancellationToken))
        {
            // 更新亲和性
            if (session.PreferredThreadId == targetWorkerId)
            {
                Interlocked.Increment(ref _affinityHits);
            }
            else
            {
                Interlocked.Increment(ref _affinityMisses);
                session.PreferredThreadId = targetWorkerId;
            }

            return true;
        }

        // 目标队列已满，尝试工作窃取或负载均衡
        if (_options.EnableWorkStealing)
        {
            return await TryWorkStealingSchedule(task, targetWorkerId, cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// 选择工作线程
    /// </summary>
    private int SelectWorkerThread(string sessionId, SessionContext session)
    {
        // 如果会话已有首选线程且负载不太高，优先使用
        if (session.PreferredThreadId >= 0 && session.PreferredThreadId < _workers.Length)
        {
            var preferredWorker = _workers[session.PreferredThreadId];
            var avgQueueSize = GetAverageQueueSize();

            if (preferredWorker.QueueSize <= avgQueueSize * (1 + _options.AffinityStrength))
            {
                return session.PreferredThreadId;
            }
        }

        // 使用一致性哈希选择线程
        var hashBasedWorker = _threadAffinity.GetNode(sessionId);

        // 检查哈希选择的线程负载
        if (_workers[hashBasedWorker].QueueSize <= GetAverageQueueSize() * 1.5)
        {
            return hashBasedWorker;
        }

        // 找最空闲的线程
        var minQueueSize = int.MaxValue;
        var bestWorker = 0;

        for (var i = 0; i < _workers.Length; i++)
        {
            var queueSize = _workers[i].QueueSize;
            if (queueSize < minQueueSize)
            {
                minQueueSize = queueSize;
                bestWorker = i;
            }
        }

        return bestWorker;
    }

    /// <summary>
    /// 尝试工作窃取调度
    /// </summary>
    private async ValueTask<bool> TryWorkStealingSchedule(AffinityMessageTask task, int excludeWorkerId,
        CancellationToken cancellationToken)
    {
        // 按队列大小排序，选择最空闲的队列
        var workers = _workers
            .Where((w, i) => i != excludeWorkerId)
            .OrderBy(w => w.QueueSize)
            .Take(3) // 只尝试前3个最空闲的
            .ToArray();

        foreach (var worker in workers)
        {
            if (await worker.TryEnqueueAsync(task, cancellationToken))
            {
                _logger.LogTrace("工作窃取成功，任务从线程{Original}转移到线程{Target}",
                    excludeWorkerId, worker.WorkerId);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 获取平均队列大小
    /// </summary>
    private double GetAverageQueueSize()
    {
        var total = 0;
        foreach (var worker in _workers)
        {
            total += worker.QueueSize;
        }
        return (double)total / _workers.Length;
    }

    /// <summary>
    /// 工作线程处理任务
    /// </summary>
    private async Task ProcessWorkerTasks(int workerId)
    {
        try
        {
            await _workers[workerId].ProcessTasksAsync(this, _cancellationTokenSource.Token);
        }
        catch (OperationCanceledException) when (_cancellationTokenSource.IsCancellationRequested)
        {
            // 正常关闭
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "工作线程{WorkerId}异常", workerId);
        }

        _logger.LogInformation("工作线程{WorkerId}已停止", workerId);
    }

    /// <summary>
    /// 清理过期会话
    /// </summary>
    private void CleanupExpiredSessions(object? state)
    {
        try
        {
            var expiredSessions = new List<string>();
            var cutoffTime = DateTime.UtcNow - _options.SessionTimeout;

            foreach (var kvp in _sessionCache)
            {
                if (kvp.Value.LastAccessTime < cutoffTime)
                {
                    expiredSessions.Add(kvp.Key);
                }
            }

            foreach (var sessionId in expiredSessions)
            {
                if (_sessionCache.TryRemove(sessionId, out var session))
                {
                    _logger.LogDebug("清理过期会话: {SessionId}, 消息数: {MessageCount}",
                        sessionId, session.MessageCount);
                }
            }

            if (expiredSessions.Count > 0)
            {
                _logger.LogInformation("清理了{Count}个过期会话", expiredSessions.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理过期会话异常");
        }
    }

    /// <summary>
    /// 获取调度器统计信息
    /// </summary>
    public SchedulerStatistics GetStatistics()
    {
        var workerStats = new WorkerStatistics[_workers.Length];

        for (var i = 0; i < _workers.Length; i++)
        {
            workerStats[i] = new WorkerStatistics
            {
                WorkerId = i,
                QueueSize = _workers[i].QueueSize,
                MessageCount = Interlocked.Read(ref _workerMessageCounts[i]),
                TotalProcessingTime = Interlocked.Read(ref _workerProcessingTimes[i]),
                IsProcessing = _workers[i].IsProcessing
            };
        }

        return new SchedulerStatistics
        {
            TotalScheduled = Interlocked.Read(ref _totalScheduled),
            TotalCompleted = Interlocked.Read(ref _totalCompleted),
            TotalFailed = Interlocked.Read(ref _totalFailed),
            AffinityHits = Interlocked.Read(ref _affinityHits),
            AffinityMisses = Interlocked.Read(ref _affinityMisses),
            ActiveSessions = _sessionCache.Count,
            WorkerStatistics = workerStats
        };
    }

    /// <summary>
    /// 调度器统计信息
    /// </summary>
    public sealed class SchedulerStatistics
    {
        public long TotalScheduled { get; set; }
        public long TotalCompleted { get; set; }
        public long TotalFailed { get; set; }
        public long AffinityHits { get; set; }
        public long AffinityMisses { get; set; }
        public int ActiveSessions { get; set; }
        public WorkerStatistics[] WorkerStatistics { get; set; } = Array.Empty<WorkerStatistics>();

        public double AffinityHitRate => (AffinityHits + AffinityMisses) > 0
            ? (double)AffinityHits / (AffinityHits + AffinityMisses) : 0;
        public double CompletionRate => TotalScheduled > 0 ? (double)TotalCompleted / TotalScheduled : 0;
        public double FailureRate => TotalScheduled > 0 ? (double)TotalFailed / TotalScheduled : 0;
    }

    /// <summary>
    /// 工作线程统计信息
    /// </summary>
    public sealed class WorkerStatistics
    {
        public int WorkerId { get; set; }
        public int QueueSize { get; set; }
        public long MessageCount { get; set; }
        public long TotalProcessingTime { get; set; }
        public bool IsProcessing { get; set; }

        public double AverageProcessingTime => MessageCount > 0 ? (double)TotalProcessingTime / MessageCount : 0;
    }

    /// <summary>
    /// 异步释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _cancellationTokenSource.Cancel();

            // 完成所有工作队列
            foreach (var worker in _workers)
            {
                worker.Complete();
            }

            // 等待所有工作线程完成
            var joinTasks = _workerThreads.Select(t => Task.Run(() =>
            {
                try
                {
                    t.Join(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "等待工作线程退出时异常");
                }
            }));

            await Task.WhenAll(joinTasks);

            _cleanupTimer?.Dispose();
            _cancellationTokenSource.Dispose();

            var stats = GetStatistics();
            _logger.LogInformation(
                "AffinityAwareScheduler已释放 - 统计信息: 总调度数: {Total}, 完成数: {Completed}, " +
                "失败数: {Failed}, 亲和性命中率: {AffinityRate:P2}, 活跃会话数: {Sessions}",
                stats.TotalScheduled, stats.TotalCompleted, stats.TotalFailed,
                stats.AffinityHitRate, stats.ActiveSessions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "释放AffinityAwareScheduler时异常");
        }
    }
}
