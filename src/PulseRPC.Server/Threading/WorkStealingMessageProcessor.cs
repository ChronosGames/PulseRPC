using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Scheduling;
using PulseRPC.Transport;

namespace PulseRPC.Server.Threading;

/// <summary>
/// 工作窃取消息处理器 - 实现负载均衡的并发消息处理
///
/// 特性:
/// - 每个工作线程有独立的任务队列
/// - 空闲线程可以窃取其他线程的任务
/// - 避免全局锁竞争，提高并发性能
/// - 支持任务亲和性，提高缓存局部性
/// </summary>
public sealed class WorkStealingMessageProcessor : IAsyncDisposable
{
    private readonly string _processorId;
    private readonly ILogger<WorkStealingMessageProcessor> _logger;
    private readonly WorkStealingProcessorOptions _options;

    // 工作线程相关
    private readonly WorkStealingQueue<MessageTask>[] _workerQueues;
    private readonly Thread[] _workerThreads;
    private readonly CancellationTokenSource _cancellationTokenSource;

    // 负载均衡
    private readonly Random _random = new();
    private volatile int _nextWorkerIndex = 0;

    // 亲和性管理
    private readonly ConcurrentDictionary<string, int> _sessionAffinity;
    private readonly ConsistentHashRing _affinityHashRing;

    // 性能指标
    private readonly WorkStealingProcessorMetrics _metrics;

    // 消息处理器
    private readonly Func<MessageTask, CancellationToken, ValueTask<ProcessingResult>> _messageHandler;

    // 运行状态
    private volatile bool _isRunning = false;
    private readonly TaskCompletionSource _startupCompletion = new();

    public WorkStealingMessageProcessor(
        string processorId,
        WorkStealingProcessorOptions options,
        Func<MessageTask, CancellationToken, ValueTask<ProcessingResult>> messageHandler,
        ILogger<WorkStealingMessageProcessor> logger)
    {
        _processorId = processorId ?? throw new ArgumentNullException(nameof(processorId));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 验证配置
        _options.Validate();

        // 初始化工作队列
        _workerQueues = new WorkStealingQueue<MessageTask>[_options.WorkerThreadCount];
        for (int i = 0; i < _workerQueues.Length; i++)
        {
            _workerQueues[i] = new WorkStealingQueue<MessageTask>();
        }

        // 初始化工作线程
        _workerThreads = new Thread[_options.WorkerThreadCount];
        for (int i = 0; i < _workerThreads.Length; i++)
        {
            var workerId = i;
            _workerThreads[i] = new Thread(() => WorkerLoop(workerId))
            {
                Name = $"WorkStealingWorker-{_processorId}-{workerId}",
                IsBackground = true
            };
        }

        // 初始化亲和性管理
        _sessionAffinity = new ConcurrentDictionary<string, int>();
        _affinityHashRing = new ConsistentHashRing(_options.WorkerThreadCount);

        // 初始化指标
        _metrics = new WorkStealingProcessorMetrics(_options.WorkerThreadCount);
        _cancellationTokenSource = new CancellationTokenSource();

        _logger.LogInformation("WorkStealingMessageProcessor已初始化: ProcessorId={ProcessorId}, " +
                             "WorkerThreads={WorkerThreads}, EnableAffinity={EnableAffinity}",
            _processorId, _options.WorkerThreadCount, _options.EnableSessionAffinity);
    }

    /// <summary>
    /// 启动处理器
    /// </summary>
    public async Task StartAsync()
    {
        if (_isRunning)
            return;

        _logger.LogInformation("正在启动WorkStealingMessageProcessor: ProcessorId={ProcessorId}", _processorId);

        _isRunning = true;

        // 启动所有工作线程
        for (int i = 0; i < _workerThreads.Length; i++)
        {
            _workerThreads[i].Start();
        }

        // 等待所有线程启动完成
        await _startupCompletion.Task;

        _logger.LogInformation("WorkStealingMessageProcessor已启动: ProcessorId={ProcessorId}", _processorId);
    }

    /// <summary>
    /// 停止处理器
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        _logger.LogInformation("正在停止WorkStealingMessageProcessor: ProcessorId={ProcessorId}", _processorId);

        _isRunning = false;

        // 取消所有操作
        await _cancellationTokenSource.CancelAsync();

        // 等待所有工作线程停止
        var stopTasks = new Task[_workerThreads.Length];
        for (int i = 0; i < _workerThreads.Length; i++)
        {
            var thread = _workerThreads[i];
            stopTasks[i] = Task.Run(() => thread.Join(_options.ShutdownTimeoutMs));
        }

        await Task.WhenAll(stopTasks);

        _logger.LogInformation("WorkStealingMessageProcessor已停止: ProcessorId={ProcessorId}", _processorId);
    }

    /// <summary>
    /// 处理消息任务
    /// </summary>
    public bool TryScheduleTask(MessageTask task)
    {
        if (!_isRunning)
        {
            _logger.LogWarning("处理器未运行，拒绝任务: ProcessorId={ProcessorId}", _processorId);
            return false;
        }

        var workerId = SelectWorker(task);
        var queue = _workerQueues[workerId];

        if (queue.TryEnqueue(task))
        {
            _metrics.IncrementTasksScheduled();
            _metrics.IncrementWorkerTasksEnqueued(workerId);

            if (_options.EnableDetailedLogging)
            {
                _logger.LogTrace("任务已调度: ProcessorId={ProcessorId}, WorkerId={WorkerId}, TaskId={TaskId}",
                    _processorId, workerId, task.MessageId);
            }

            return true;
        }

        _metrics.IncrementTasksRejected();
        _logger.LogDebug("任务调度失败（队列满）: ProcessorId={ProcessorId}, WorkerId={WorkerId}",
            _processorId, workerId);
        return false;
    }

    /// <summary>
    /// 选择工作线程
    /// </summary>
    private int SelectWorker(MessageTask task)
    {
        if (_options.EnableSessionAffinity && task.Context is string sessionId && !string.IsNullOrEmpty(sessionId))
        {
            // 使用会话亲和性
            return _sessionAffinity.GetOrAdd(sessionId, _ => _affinityHashRing.GetNode(sessionId));
        }

        if (_options.UseRoundRobinSelection)
        {
            // 轮询选择
            return Interlocked.Increment(ref _nextWorkerIndex) % _workerQueues.Length;
        }

        // 负载最小选择
        return SelectLeastLoadedWorker();
    }

    /// <summary>
    /// 选择负载最小的工作线程
    /// </summary>
    private int SelectLeastLoadedWorker()
    {
        int bestWorker = 0;
        int minQueueSize = _workerQueues[0].Count;

        for (int i = 1; i < _workerQueues.Length; i++)
        {
            int queueSize = _workerQueues[i].Count;
            if (queueSize < minQueueSize)
            {
                minQueueSize = queueSize;
                bestWorker = i;
            }
        }

        return bestWorker;
    }

    /// <summary>
    /// 工作线程循环
    /// </summary>
    private void WorkerLoop(int workerId)
    {
        _logger.LogDebug("工作线程启动: ProcessorId={ProcessorId}, WorkerId={WorkerId}", _processorId, workerId);

        var localQueue = _workerQueues[workerId];
        var stealAttempts = 0;
        var consecutiveEmptyDequeues = 0;

        // 通知启动完成
        if (workerId == _workerThreads.Length - 1)
        {
            _startupCompletion.SetResult();
        }

        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                MessageTask task;
                bool wasStolen = false;
                bool hasTask = false;

                // 1. 尝试从本地队列获取任务
                if (localQueue.TryDequeue(out task))
                {
                    hasTask = true;
                    consecutiveEmptyDequeues = 0;
                    _metrics.IncrementWorkerTasksDequeued(workerId);
                }
                // 2. 尝试从其他队列窃取任务
                else if (consecutiveEmptyDequeues >= _options.StealThreshold)
                {
                    var stolenTask = TryStealTask(workerId, out var stolenFromWorker);
                    if (stolenTask.HasValue)
                    {
                        task = stolenTask.Value;
                        hasTask = true;
                        wasStolen = true;
                        consecutiveEmptyDequeues = 0;
                        _metrics.IncrementTasksStolen();
                        _metrics.IncrementWorkerTasksStolen(workerId);
                        _metrics.IncrementWorkerTasksStolenFrom(stolenFromWorker);
                    }
                    else
                    {
                        stealAttempts++;
                    }
                }
                else
                {
                    consecutiveEmptyDequeues++;
                }

                if (hasTask)
                {
                    // 处理任务
                    ProcessTaskSync(task, workerId, wasStolen);
                    stealAttempts = 0;
                }
                else
                {
                    // 没有任务时的等待策略
                    ApplyWaitStrategy(stealAttempts, consecutiveEmptyDequeues);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "工作线程异常: ProcessorId={ProcessorId}, WorkerId={WorkerId}",
                    _processorId, workerId);

                // 短暂等待后继续
                Thread.Sleep(100);
            }
        }

        _logger.LogDebug("工作线程停止: ProcessorId={ProcessorId}, WorkerId={WorkerId}", _processorId, workerId);
    }

    /// <summary>
    /// 尝试窃取任务
    /// </summary>
    private MessageTask? TryStealTask(int workerIdToAvoid, out int stolenFromWorker)
    {
        stolenFromWorker = -1;

        // 随机选择起始位置，避免总是从同一个队列窃取
        int startIndex = _random.Next(_workerQueues.Length);

        for (int i = 0; i < _workerQueues.Length; i++)
        {
            int queueIndex = (startIndex + i) % _workerQueues.Length;

            // 跳过自己的队列
            if (queueIndex == workerIdToAvoid)
                continue;

            var queue = _workerQueues[queueIndex];

            // 只有当队列有足够任务时才尝试窃取
            if (queue.Count > _options.MinQueueSizeForStealing && queue.TrySteal(out var task))
            {
                stolenFromWorker = queueIndex;
                return task;
            }
        }

        return null;
    }

    /// <summary>
    /// 同步处理任务
    /// </summary>
    private void ProcessTaskSync(MessageTask task, int workerId, bool wasStolen)
    {
        var startTime = Stopwatch.GetTimestamp();

        try
        {
            // 异步任务转同步（在工作线程中运行）
            var result = _messageHandler(task, _cancellationTokenSource.Token).AsTask().GetAwaiter().GetResult();

            var processingTime = TimeSpan.FromTicks(Stopwatch.GetTimestamp() - startTime);

            // 记录指标
            if (result.Success)
            {
                _metrics.IncrementTasksCompleted();
                _metrics.IncrementWorkerTasksCompleted(workerId);
                _metrics.WorkerProcessingTime[workerId].Record(processingTime.TotalMilliseconds);

                if (wasStolen)
                {
                    _metrics.IncrementStolenTasksCompleted();
                }
            }
            else
            {
                _metrics.IncrementTasksErrored();
                _metrics.IncrementWorkerTasksErrored(workerId);
            }

            if (_options.EnableDetailedLogging)
            {
                _logger.LogTrace("任务处理完成: ProcessorId={ProcessorId}, WorkerId={WorkerId}, " +
                               "TaskId={TaskId}, Success={Success}, WasStolen={WasStolen}, " +
                               "ProcessingTime={ProcessingTimeMs}ms",
                    _processorId, workerId, task.MessageId, result.Success, wasStolen,
                    processingTime.TotalMilliseconds);
            }
        }
        catch (Exception ex)
        {
            var processingTime = TimeSpan.FromTicks(Stopwatch.GetTimestamp() - startTime);

            _metrics.IncrementTasksErrored();
            _metrics.IncrementWorkerTasksErrored(workerId);

            _logger.LogError(ex, "任务处理失败: ProcessorId={ProcessorId}, WorkerId={WorkerId}, " +
                            "TaskId={TaskId}, ProcessingTime={ProcessingTimeMs}ms",
                _processorId, workerId, task.MessageId, processingTime.TotalMilliseconds);
        }
    }

    /// <summary>
    /// 应用等待策略
    /// </summary>
    private void ApplyWaitStrategy(int stealAttempts, int consecutiveEmptyDequeues)
    {
        switch (_options.WaitStrategy)
        {
            case WaitStrategy.SpinWait:
                if (consecutiveEmptyDequeues < 100)
                {
                    Thread.SpinWait(10);
                }
                else
                {
                    Thread.Yield();
                }
                break;

            case WaitStrategy.YieldWait:
                Thread.Yield();
                break;

            case WaitStrategy.SleepWait:
                Thread.Sleep(1);
                break;

            case WaitStrategy.Adaptive:
                if (consecutiveEmptyDequeues < 10)
                {
                    Thread.SpinWait(10);
                }
                else if (consecutiveEmptyDequeues < 100)
                {
                    Thread.Yield();
                }
                else
                {
                    Thread.Sleep(1);
                }
                break;
        }
    }

    /// <summary>
    /// 获取处理器指标
    /// </summary>
    public WorkStealingProcessorMetrics GetMetrics() => _metrics;

    /// <summary>
    /// 获取处理器状态
    /// </summary>
    public WorkStealingProcessorStatus GetStatus()
    {
        var workerStatuses = new WorkerStatus[_workerQueues.Length];
        for (int i = 0; i < _workerQueues.Length; i++)
        {
            workerStatuses[i] = new WorkerStatus
            {
                WorkerId = i,
                QueueDepth = _workerQueues[i].Count,
                IsActive = _workerThreads[i].IsAlive,
                TasksEnqueued = _metrics.GetWorkerTasksEnqueuedCount(i),
                TasksDequeued = _metrics.GetWorkerTasksDequeuedCount(i),
                TasksCompleted = _metrics.GetWorkerTasksCompletedCount(i),
                TasksStolen = _metrics.GetWorkerTasksStolenCount(i),
                TasksStolenFrom = _metrics.GetWorkerTasksStolenFromCount(i)
            };
        }

        return new WorkStealingProcessorStatus
        {
            ProcessorId = _processorId,
            IsRunning = _isRunning,
            WorkerCount = _workerQueues.Length,
            TotalQueueDepth = GetTotalQueueDepth(),
            TotalTasksScheduled = _metrics.TasksScheduledCount,
            TotalTasksCompleted = _metrics.TasksCompletedCount,
            TotalTasksStolen = _metrics.TasksStolenCount,
            CurrentThroughput = _metrics.GetCurrentThroughput(),
            WorkerStatuses = workerStatuses
        };
    }

    /// <summary>
    /// 获取总队列深度
    /// </summary>
    private int GetTotalQueueDepth()
    {
        int total = 0;
        for (int i = 0; i < _workerQueues.Length; i++)
        {
            total += _workerQueues[i].Count;
        }
        return total;
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();

        // 清理队列中剩余的任务
        for (int i = 0; i < _workerQueues.Length; i++)
        {
            var queue = _workerQueues[i];
            while (queue.TryDequeue(out _))
            {
                // 只是清空队列，不处理任务
            }
        }

        _cancellationTokenSource.Dispose();

        _logger.LogInformation("WorkStealingMessageProcessor已释放: ProcessorId={ProcessorId}", _processorId);
    }
}

/// <summary>
/// 工作线程状态
/// </summary>
public class WorkerStatus
{
    public int WorkerId { get; set; }
    public int QueueDepth { get; set; }
    public bool IsActive { get; set; }
    public long TasksEnqueued { get; set; }
    public long TasksDequeued { get; set; }
    public long TasksCompleted { get; set; }
    public long TasksStolen { get; set; }
    public long TasksStolenFrom { get; set; }
}

/// <summary>
/// 工作窃取处理器状态
/// </summary>
public class WorkStealingProcessorStatus
{
    public required string ProcessorId { get; set; }
    public bool IsRunning { get; set; }
    public int WorkerCount { get; set; }
    public int TotalQueueDepth { get; set; }
    public long TotalTasksScheduled { get; set; }
    public long TotalTasksCompleted { get; set; }
    public long TotalTasksStolen { get; set; }
    public double CurrentThroughput { get; set; }
    public required WorkerStatus[] WorkerStatuses { get; set; }
}
