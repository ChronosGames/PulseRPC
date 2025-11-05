using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Memory;
using PulseRPC.Server.Scheduling;
using PulseRPC.Transport;

namespace PulseRPC.Server.Scheduling;

/// <summary>
/// 优先级感知调度器 - 实现三优先级队列和动态权重分配
///
/// 优先级策略:
/// - Critical(50%): 关键消息，最大延迟2ms，永不丢弃
/// - Normal(35%): 普通消息，最大延迟10ms，高负载时可丢弃
/// - Bulk(15%): 批量消息，最大延迟100ms，支持背压控制
/// </summary>
public sealed class PriorityAwareScheduler : IAsyncDisposable
{
    private readonly string _schedulerId;
    private readonly ILogger<PriorityAwareScheduler> _logger;
    private readonly PrioritySchedulerOptions _options;

    // 三优先级队列
    private readonly PriorityQueue<MessageTask, long> _criticalQueue; // 优先级：时间戳（越小越优先）
    private readonly Channel<MessageTask> _normalChannel;
    private readonly ChannelReader<MessageTask> _normalReader;
    private readonly ChannelWriter<MessageTask> _normalWriter;
    private readonly Channel<MessageTask> _bulkChannel;
    private readonly ChannelReader<MessageTask> _bulkReader;
    private readonly ChannelWriter<MessageTask> _bulkWriter;

    // 权重调度器
    private readonly WeightedRoundRobinScheduler _weightedScheduler;

    // 背压控制
    private readonly SemaphoreSlim _concurrencyLimiter;
    private readonly TokenBucket _rateLimiter;

    // 性能指标
    private readonly PrioritySchedulerMetrics _metrics;
    private readonly CancellationTokenSource _cancellationTokenSource;

    // 消息处理器
    private readonly Func<MessageTask, CancellationToken, ValueTask<ProcessingResult>> _messageHandler;

    // 调度控制
    private readonly Timer _schedulingTimer;
    private volatile bool _isRunning = false;

    public PriorityAwareScheduler(
        string schedulerId,
        PrioritySchedulerOptions options,
        Func<MessageTask, CancellationToken, ValueTask<ProcessingResult>> messageHandler,
        ILogger<PriorityAwareScheduler> logger)
    {
        _schedulerId = schedulerId ?? throw new ArgumentNullException(nameof(schedulerId));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 初始化关键消息优先队列
        _criticalQueue = new PriorityQueue<MessageTask, long>();

        // 初始化普通消息通道
        var normalOptions = new BoundedChannelOptions(_options.NormalQueueSize)
        {
            FullMode = _options.NormalDropOnFull ? BoundedChannelFullMode.DropOldest : BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        };
        var normalChannel = Channel.CreateBounded<MessageTask>(normalOptions);
        _normalChannel = normalChannel;
        _normalReader = normalChannel.Reader;
        _normalWriter = normalChannel.Writer;

        // 初始化批量消息通道
        var bulkOptions = new BoundedChannelOptions(_options.BulkQueueSize)
        {
            FullMode = _options.BulkBackpressure ? BoundedChannelFullMode.Wait : BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        };
        var bulkChannel = Channel.CreateBounded<MessageTask>(bulkOptions);
        _bulkChannel = bulkChannel;
        _bulkReader = bulkChannel.Reader;
        _bulkWriter = bulkChannel.Writer;

        // 初始化权重调度器
        _weightedScheduler = new WeightedRoundRobinScheduler(new[]
        {
            _options.CriticalWeight,
            _options.NormalWeight,
            _options.BulkWeight
        });

        // 初始化并发控制
        _concurrencyLimiter = new SemaphoreSlim(_options.MaxConcurrentTasks, _options.MaxConcurrentTasks);
        _rateLimiter = new TokenBucket(_options.MaxTasksPerSecond, _options.BurstSize);

        // 初始化指标
        _metrics = new PrioritySchedulerMetrics();
        _cancellationTokenSource = new CancellationTokenSource();

        // 初始化定时调度器（1ms间隔）
        _schedulingTimer = new Timer(SchedulingCallback, null, Timeout.Infinite, Timeout.Infinite);

        _logger.LogInformation("PriorityAwareScheduler已初始化: SchedulerId={SchedulerId}, " +
                             "CriticalWeight={CriticalWeight}%, NormalWeight={NormalWeight}%, BulkWeight={BulkWeight}%",
            _schedulerId, _options.CriticalWeight, _options.NormalWeight, _options.BulkWeight);
    }

    /// <summary>
    /// 启动调度器
    /// </summary>
    public void Start()
    {
        if (_isRunning)
            return;

        _isRunning = true;

        // 启动调度工作线程
        for (int i = 0; i < _options.WorkerThreadCount; i++)
        {
            var workerId = i;
            _ = Task.Run(() => WorkerLoop(workerId), _cancellationTokenSource.Token);
        }

        // 启动定时调度
        _schedulingTimer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(1));

        _logger.LogInformation("PriorityAwareScheduler已启动: SchedulerId={SchedulerId}, WorkerThreads={WorkerThreads}",
            _schedulerId, _options.WorkerThreadCount);
    }

    /// <summary>
    /// 停止调度器
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        _logger.LogInformation("正在停止PriorityAwareScheduler: SchedulerId={SchedulerId}", _schedulerId);

        _isRunning = false;
        await _schedulingTimer.DisposeAsync();

        // 完成通道写入
        _normalWriter.TryComplete();
        _bulkWriter.TryComplete();

        // 取消所有操作
        await _cancellationTokenSource.CancelAsync();

        // 等待工作线程完成
        try
        {
            await _normalReader.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            await _bulkReader.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("PriorityAwareScheduler停止超时: SchedulerId={SchedulerId}", _schedulerId);
        }

        _logger.LogInformation("PriorityAwareScheduler已停止: SchedulerId={SchedulerId}", _schedulerId);
    }

    /// <summary>
    /// 调度消息任务
    /// </summary>
    public async ValueTask<bool> ScheduleAsync(MessageTask task, CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            _logger.LogWarning("调度器未运行，拒绝消息: SchedulerId={SchedulerId}", _schedulerId);
            return false;
        }

        // 检查速率限制
        if (!_rateLimiter.TryConsume())
        {
            _metrics.RateLimitedTasks.Add(1);
            _logger.LogDebug("速率限制触发，拒绝消息: SchedulerId={SchedulerId}, Priority={Priority}",
                _schedulerId, task.Priority);
            return false;
        }

        // 设置调度时间戳
        task.ScheduleTime = Stopwatch.GetTimestamp();

        try
        {
            switch (task.Priority)
            {
                case MessagePriority.Critical:
                    return ScheduleCriticalTask(task);

                case MessagePriority.Normal:
                    return await ScheduleNormalTaskAsync(task, cancellationToken);

                case MessagePriority.Low:
                    return await ScheduleBulkTaskAsync(task, cancellationToken);

                default:
                    _logger.LogWarning("未知消息优先级: Priority={Priority}", task.Priority);
                    return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "消息调度失败: SchedulerId={SchedulerId}, Priority={Priority}",
                _schedulerId, task.Priority);
            return false;
        }
    }

    /// <summary>
    /// 调度关键消息（同步，永不阻塞）
    /// </summary>
    private bool ScheduleCriticalTask(MessageTask task)
    {
        lock (_criticalQueue)
        {
            // 检查关键队列是否已满
            if (_criticalQueue.Count >= _options.CriticalQueueSize)
            {
                _metrics.CriticalQueueOverflows.Add(1);
                _logger.LogWarning("关键消息队列溢出: SchedulerId={SchedulerId}, QueueSize={QueueSize}",
                    _schedulerId, _criticalQueue.Count);
                return false;
            }

            // 使用时间戳作为优先级（越早越优先）
            _criticalQueue.Enqueue(task, task.ScheduleTime);
            _metrics.IncrementCriticalTasksScheduled();

            if (_options.EnableDetailedLogging)
            {
                _logger.LogTrace("关键消息已调度: SchedulerId={SchedulerId}, TaskId={TaskId}",
                    _schedulerId, task.MessageId);
            }

            return true;
        }
    }

    /// <summary>
    /// 调度普通消息
    /// </summary>
    private async ValueTask<bool> ScheduleNormalTaskAsync(MessageTask task, CancellationToken cancellationToken)
    {
        try
        {
            await _normalWriter.WriteAsync(task, cancellationToken);
            _metrics.IncrementNormalTasksScheduled();

            if (_options.EnableDetailedLogging)
            {
                _logger.LogTrace("普通消息已调度: SchedulerId={SchedulerId}, TaskId={TaskId}",
                    _schedulerId, task.MessageId);
            }

            return true;
        }
        catch (ChannelClosedException)
        {
            _logger.LogDebug("普通消息通道已关闭: SchedulerId={SchedulerId}", _schedulerId);
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("普通消息调度被取消: SchedulerId={SchedulerId}", _schedulerId);
            return false;
        }
    }

    /// <summary>
    /// 调度批量消息
    /// </summary>
    private async ValueTask<bool> ScheduleBulkTaskAsync(MessageTask task, CancellationToken cancellationToken)
    {
        try
        {
            await _bulkWriter.WriteAsync(task, cancellationToken);
            _metrics.IncrementBulkTasksScheduled();

            if (_options.EnableDetailedLogging)
            {
                _logger.LogTrace("批量消息已调度: SchedulerId={SchedulerId}, TaskId={TaskId}",
                    _schedulerId, task.MessageId);
            }

            return true;
        }
        catch (ChannelClosedException)
        {
            _logger.LogDebug("批量消息通道已关闭: SchedulerId={SchedulerId}", _schedulerId);
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("批量消息调度被取消: SchedulerId={SchedulerId}", _schedulerId);
            return false;
        }
    }

    /// <summary>
    /// 定时调度回调（处理关键消息的SLA检查）
    /// </summary>
    private void SchedulingCallback(object? state)
    {
        if (!_isRunning)
            return;

        try
        {
            CheckCriticalMessageSLA();
            UpdateSchedulingMetrics();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "定时调度回调失败: SchedulerId={SchedulerId}", _schedulerId);
        }
    }

    /// <summary>
    /// 检查关键消息SLA
    /// </summary>
    private void CheckCriticalMessageSLA()
    {
        if (_criticalQueue.Count == 0)
            return;

        var now = Stopwatch.GetTimestamp();
        var slaThresholdTicks = TimeSpan.FromMilliseconds(_options.CriticalMaxLatencyMs).Ticks;

        lock (_criticalQueue)
        {
            // 检查队列头部消息是否超时
            if (_criticalQueue.TryPeek(out var task, out var scheduleTime))
            {
                var waitTime = TimeSpan.FromTicks(now - scheduleTime);
                if (waitTime.TotalMilliseconds > _options.CriticalMaxLatencyMs)
                {
                    _metrics.CriticalSLAViolations.Add(1);
                    _logger.LogWarning("关键消息SLA违规: SchedulerId={SchedulerId}, TaskId={TaskId}, " +
                                     "WaitTime={WaitTimeMs}ms, SLA={SLAMs}ms",
                        _schedulerId, task.MessageId, waitTime.TotalMilliseconds, _options.CriticalMaxLatencyMs);
                }
            }
        }
    }

    /// <summary>
    /// 更新调度指标
    /// </summary>
    private void UpdateSchedulingMetrics()
    {
        lock (_criticalQueue)
        {
            _metrics.SetCriticalQueueDepth(_criticalQueue.Count);
        }

        _metrics.SetNormalQueueDepth(_normalReader.CanCount ? _normalReader.Count : 0);
        _metrics.SetBulkQueueDepth(_bulkReader.CanCount ? _bulkReader.Count : 0);
        _metrics.SetConcurrencyUtilization(1.0 - (double)_concurrencyLimiter.CurrentCount / _options.MaxConcurrentTasks);
    }

    /// <summary>
    /// 工作线程循环
    /// </summary>
    private async Task WorkerLoop(int workerId)
    {
        _logger.LogDebug("工作线程启动: SchedulerId={SchedulerId}, WorkerId={WorkerId}", _schedulerId, workerId);

        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                // 获取下一个要处理的优先级
                var priorityToProcess = _weightedScheduler.GetNext();
                MessageTask? task = null;

                // 根据优先级获取任务
                switch (priorityToProcess)
                {
                    case 0: // Critical
                        task = TryDequeueCriticalTask();
                        break;

                    case 1: // Normal
                        task = await TryDequeueNormalTaskAsync();
                        break;

                    case 2: // Bulk
                        task = await TryDequeBulkTaskAsync();
                        break;
                }

                if (task.HasValue)
                {
                    await ProcessTask(task.Value, workerId);
                }
                else
                {
                    // 没有任务时短暂等待
                    await Task.Delay(1, _cancellationTokenSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "工作线程异常: SchedulerId={SchedulerId}, WorkerId={WorkerId}",
                    _schedulerId, workerId);
                await Task.Delay(100, _cancellationTokenSource.Token);
            }
        }

        _logger.LogDebug("工作线程停止: SchedulerId={SchedulerId}, WorkerId={WorkerId}", _schedulerId, workerId);
    }

    /// <summary>
    /// 尝试出队关键任务
    /// </summary>
    private MessageTask? TryDequeueCriticalTask()
    {
        lock (_criticalQueue)
        {
            if (_criticalQueue.TryDequeue(out var task, out _))
            {
                _metrics.CriticalTasksDequeued.Add(1);
                return task;
            }
        }
        return null;
    }

    /// <summary>
    /// 尝试出队普通任务
    /// </summary>
    private async ValueTask<MessageTask?> TryDequeueNormalTaskAsync()
    {
        if (_normalReader.TryRead(out var task))
        {
            _metrics.NormalTasksDequeued.Add(1);
            return task;
        }
        return null;
    }

    /// <summary>
    /// 尝试出队批量任务
    /// </summary>
    private async ValueTask<MessageTask?> TryDequeBulkTaskAsync()
    {
        if (_bulkReader.TryRead(out var task))
        {
            _metrics.BulkTasksDequeued.Add(1);
            return task;
        }
        return null;
    }

    /// <summary>
    /// 处理任务
    /// </summary>
    private async Task ProcessTask(MessageTask task, int workerId)
    {
        // 等待并发许可
        await _concurrencyLimiter.WaitAsync(_cancellationTokenSource.Token);

        try
        {
            var startTime = Stopwatch.GetTimestamp();
            var scheduleLatency = TimeSpan.FromTicks(startTime - task.ScheduleTime);

            // 检查SLA
            var maxLatency = GetMaxLatencyForPriority(task.Priority);
            if (scheduleLatency.TotalMilliseconds > maxLatency)
            {
                RecordSLAViolation(task.Priority);
                _logger.LogWarning("任务SLA违规: SchedulerId={SchedulerId}, Priority={Priority}, " +
                                 "ScheduleLatency={ScheduleLatencyMs}ms, MaxLatency={MaxLatencyMs}ms",
                    _schedulerId, task.Priority, scheduleLatency.TotalMilliseconds, maxLatency);
            }

            // 处理任务
            var result = await _messageHandler(task, _cancellationTokenSource.Token);
            var processingTime = TimeSpan.FromTicks(Stopwatch.GetTimestamp() - startTime);

            // 记录指标
            RecordTaskCompletion(task.Priority, scheduleLatency, processingTime, result.Success);

            if (_options.EnableDetailedLogging)
            {
                _logger.LogTrace("任务处理完成: SchedulerId={SchedulerId}, WorkerId={WorkerId}, " +
                               "Priority={Priority}, TaskId={TaskId}, Success={Success}, " +
                               "ScheduleLatency={ScheduleLatencyMs}ms, ProcessingTime={ProcessingTimeMs}ms",
                    _schedulerId, workerId, task.Priority, task.MessageId, result.Success,
                    scheduleLatency.TotalMilliseconds, processingTime.TotalMilliseconds);
            }
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    /// <summary>
    /// 获取优先级对应的最大延迟
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double GetMaxLatencyForPriority(MessagePriority priority) => priority switch
    {
        MessagePriority.Critical => _options.CriticalMaxLatencyMs,
        MessagePriority.Normal => _options.NormalMaxLatencyMs,
        MessagePriority.Low => _options.BulkMaxLatencyMs,
        _ => _options.NormalMaxLatencyMs
    };

    /// <summary>
    /// 记录SLA违规
    /// </summary>
    private void RecordSLAViolation(MessagePriority priority)
    {
        switch (priority)
        {
            case MessagePriority.Critical:
                _metrics.CriticalSLAViolations.Add(1);
                break;
            case MessagePriority.Normal:
                _metrics.NormalSLAViolations.Add(1);
                break;
            case MessagePriority.Low:
                _metrics.BulkSLAViolations.Add(1);
                break;
        }
    }

    /// <summary>
    /// 记录任务完成指标
    /// </summary>
    private void RecordTaskCompletion(MessagePriority priority, TimeSpan scheduleLatency,
        TimeSpan processingTime, bool success)
    {
        if (success)
        {
            switch (priority)
            {
                case MessagePriority.Critical:
                    _metrics.IncrementCriticalTasksCompleted();
                    _metrics.CriticalScheduleLatency.Record(scheduleLatency.TotalMilliseconds);
                    _metrics.CriticalProcessingTime.Record(processingTime.TotalMilliseconds);
                    break;
                case MessagePriority.Normal:
                    _metrics.IncrementNormalTasksCompleted();
                    _metrics.NormalScheduleLatency.Record(scheduleLatency.TotalMilliseconds);
                    _metrics.NormalProcessingTime.Record(processingTime.TotalMilliseconds);
                    break;
                case MessagePriority.Low:
                    _metrics.IncrementBulkTasksCompleted();
                    _metrics.BulkScheduleLatency.Record(scheduleLatency.TotalMilliseconds);
                    _metrics.BulkProcessingTime.Record(processingTime.TotalMilliseconds);
                    break;
            }
        }
        else
        {
            switch (priority)
            {
                case MessagePriority.Critical:
                    _metrics.CriticalTasksErrored.Add(1);
                    break;
                case MessagePriority.Normal:
                    _metrics.NormalTasksErrored.Add(1);
                    break;
                case MessagePriority.Low:
                    _metrics.BulkTasksErrored.Add(1);
                    break;
            }
        }
    }

    /// <summary>
    /// 获取调度器指标
    /// </summary>
    public PrioritySchedulerMetrics GetMetrics() => _metrics;

    /// <summary>
    /// 获取调度器状态
    /// </summary>
    public SchedulerStatus GetStatus()
    {
        lock (_criticalQueue)
        {
            return new SchedulerStatus
            {
                SchedulerId = _schedulerId,
                IsRunning = _isRunning,
                CriticalQueueDepth = _criticalQueue.Count,
                NormalQueueDepth = _normalReader.CanCount ? _normalReader.Count : 0,
                BulkQueueDepth = _bulkReader.CanCount ? _bulkReader.Count : 0,
                ConcurrencyUtilization = 1.0 - (double)_concurrencyLimiter.CurrentCount / _options.MaxConcurrentTasks,
                TotalTasksScheduled = _metrics.GetTotalTasksScheduled(),
                TotalTasksCompleted = _metrics.GetTotalTasksCompleted(),
                CurrentThroughput = _metrics.GetCurrentThroughput()
            };
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();

        await _schedulingTimer.DisposeAsync();
        _concurrencyLimiter.Dispose();
        _cancellationTokenSource.Dispose();

        _logger.LogInformation("PriorityAwareScheduler已释放: SchedulerId={SchedulerId}", _schedulerId);
    }
}

/// <summary>
/// 消息任务
/// </summary>
public struct MessageTask
{
    public Guid MessageId { get; set; }
    public MessagePriority Priority { get; set; }
    public ReadOnlyMemory<byte> Data { get; set; }
    public long ScheduleTime { get; set; }
    public object? Context { get; set; }
}

/// <summary>
/// 调度器状态
/// </summary>
public class SchedulerStatus
{
    public required string SchedulerId { get; set; }
    public bool IsRunning { get; set; }
    public int CriticalQueueDepth { get; set; }
    public int NormalQueueDepth { get; set; }
    public int BulkQueueDepth { get; set; }
    public double ConcurrencyUtilization { get; set; }
    public long TotalTasksScheduled { get; set; }
    public long TotalTasksCompleted { get; set; }
    public double CurrentThroughput { get; set; }
}
