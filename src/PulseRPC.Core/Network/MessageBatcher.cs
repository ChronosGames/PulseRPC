using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Pipelines;
using System.Linq;
using System.Threading.Channels;
using PulseRPC.Protocol.Compression;

namespace PulseRPC.Protocol.Network;

/// <summary>
/// 消息优先级
/// </summary>
public enum MessagePriority
{
    /// <summary>
    /// 低优先级
    /// </summary>
    Low = 0,

    /// <summary>
    /// 普通优先级
    /// </summary>
    Normal = 1,

    /// <summary>
    /// 高优先级
    /// </summary>
    High = 2,

    /// <summary>
    /// 系统消息优先级
    /// </summary>
    System = 3
}

/// <summary>
/// 消息批处理器配置选项
/// </summary>
public class MessageBatcherOptions
{
    /// <summary>
    /// 批处理大小阈值（字节）
    /// </summary>
    public int BatchSizeThreshold { get; set; } = 64 * 1024; // 64KB

    /// <summary>
    /// 批处理延迟（毫秒）
    /// </summary>
    public int BatchDelay { get; set; } = 50;

    /// <summary>
    /// 最大分片大小（字节）
    /// </summary>
    public int MaxFragmentSize { get; set; } = 16 * 1024; // 16KB

    /// <summary>
    /// 每个优先级的队列容量
    /// </summary>
    public int QueueCapacity { get; set; } = 1024;
}

/// <summary>
/// 消息批处理器
/// </summary>
public class MessageBatcher : IDisposable
{
    private readonly MessageBatcherOptions _options;
    private readonly CancellationTokenSource _cts;
    private readonly Dictionary<MessagePriority, Channel<MessageBatch>> _priorityQueues;
    private readonly Task _batchingTask;
    private readonly BatchSender _batchSender;
    private readonly PerformanceMetrics _metrics;

    public MessageBatcher(PipeWriter writer, MessageBatcherOptions? options = null, MessageCompressorOptions? compressorOptions = null)
    {
        _options = options ?? new MessageBatcherOptions();
        _cts = new CancellationTokenSource();
        _metrics = new PerformanceMetrics();
        _batchSender = new BatchSender(writer, compressorOptions);

        // 初始化优先级队列
        _priorityQueues = new Dictionary<MessagePriority, Channel<MessageBatch>>();
        foreach (MessagePriority priority in Enum.GetValues(typeof(MessagePriority)))
        {
            var channelOptions = new BoundedChannelOptions(_options.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            };
            _priorityQueues[priority] = Channel.CreateBounded<MessageBatch>(channelOptions);
        }

        // 启动批处理任务
        _batchingTask = StartBatchingAsync(_cts.Token);
    }

    /// <summary>
    /// 获取性能指标
    /// </summary>
    public PerformanceMetrics Metrics => _metrics;

    /// <summary>
    /// 添加消息到批处理队列
    /// </summary>
    public async ValueTask EnqueueAsync(MessageBatch batch, MessagePriority priority = MessagePriority.Normal, CancellationToken cancellationToken = default)
    {
        var queue = _priorityQueues[priority];
        await queue.Writer.WriteAsync(batch, cancellationToken).ConfigureAwait(false);
        _metrics.IncrementEnqueuedMessages();
        _metrics.AddMessageSize(batch.Data.Length);
    }

    private async Task StartBatchingAsync(CancellationToken cancellationToken)
    {
        var currentBatch = new List<MessageBatch>();
        var currentSize = 0;
        var lastBatchTime = DateTime.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // 按优先级从高到低处理消息
                foreach (var priority in _priorityQueues.Keys.OrderByDescending(p => p))
                {
                    var queue = _priorityQueues[priority];
                    while (queue.Reader.TryRead(out var batch))
                    {
                        // 如果当前批次大小超过阈值，立即发送
                        if (currentSize + batch.Data.Length > _options.BatchSizeThreshold)
                        {
                            await SendBatchAsync(currentBatch, cancellationToken).ConfigureAwait(false);
                            currentBatch.Clear();
                            currentSize = 0;
                            lastBatchTime = DateTime.UtcNow;
                        }

                        // 添加到当前批次
                        currentBatch.Add(batch);
                        currentSize += batch.Data.Length;
                    }
                }

                // 如果达到批处理延迟时间，发送当前批次
                if (currentBatch.Count > 0 && (DateTime.UtcNow - lastBatchTime).TotalMilliseconds >= _options.BatchDelay)
                {
                    await SendBatchAsync(currentBatch, cancellationToken).ConfigureAwait(false);
                    currentBatch.Clear();
                    currentSize = 0;
                    lastBatchTime = DateTime.UtcNow;
                }

                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                _metrics.IncrementFailedBatches();
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task SendBatchAsync(List<MessageBatch> batches, CancellationToken cancellationToken)
    {
        if (batches.Count == 0)
            return;

        try
        {
            await _batchSender.SendBatchAsync(batches, cancellationToken).ConfigureAwait(false);
            _metrics.IncrementSentBatches();
            var totalSize = 0;
            foreach (var batch in batches)
            {
                totalSize += batch.Data.Length;
            }
            _metrics.AddBatchSize(totalSize);
        }
        catch (Exception)
        {
            _metrics.IncrementFailedBatches();
            throw;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 消息批次
/// </summary>
public class MessageBatch
{
    /// <summary>
    /// 消息数据
    /// </summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// 消息标识符
    /// </summary>
    public long MessageId { get; set; }

    /// <summary>
    /// 分片索引（如果是分片消息）
    /// </summary>
    public int FragmentIndex { get; set; }

    /// <summary>
    /// 分片总数（如果是分片消息）
    /// </summary>
    public int TotalFragments { get; set; }

    /// <summary>
    /// 是否是分片消息
    /// </summary>
    public bool IsFragment => TotalFragments > 1;
}

/// <summary>
/// 性能指标
/// </summary>
public class PerformanceMetrics
{
    private long _enqueuedMessages;
    private long _sentBatches;
    private long _failedBatches;
    private long _totalMessageSize;
    private long _totalBatchSize;
    private readonly ConcurrentQueue<(DateTime Time, int Size)> _messageHistory;
    private readonly ConcurrentQueue<(DateTime Time, int Size)> _batchHistory;

    public PerformanceMetrics()
    {
        _messageHistory = new ConcurrentQueue<(DateTime, int)>();
        _batchHistory = new ConcurrentQueue<(DateTime, int)>();
    }

    public void IncrementEnqueuedMessages() => Interlocked.Increment(ref _enqueuedMessages);
    public void IncrementSentBatches() => Interlocked.Increment(ref _sentBatches);
    public void IncrementFailedBatches() => Interlocked.Increment(ref _failedBatches);

    public void AddMessageSize(int size)
    {
        Interlocked.Add(ref _totalMessageSize, size);
        _messageHistory.Enqueue((DateTime.UtcNow, size));
        while (_messageHistory.Count > 1000)
        {
            _messageHistory.TryDequeue(out _);
        }
    }

    public void AddBatchSize(int size)
    {
        Interlocked.Add(ref _totalBatchSize, size);
        _batchHistory.Enqueue((DateTime.UtcNow, size));
        while (_batchHistory.Count > 1000)
        {
            _batchHistory.TryDequeue(out _);
        }
    }

    public (long EnqueuedMessages, long SentBatches, long FailedBatches) GetCounters()
    {
        return (
            Interlocked.Read(ref _enqueuedMessages),
            Interlocked.Read(ref _sentBatches),
            Interlocked.Read(ref _failedBatches)
        );
    }

    public (double AverageMessageSize, double AverageBatchSize) GetAverageSizes(TimeSpan window)
    {
        var cutoff = DateTime.UtcNow - window;
        var messages = _messageHistory.Where(x => x.Time >= cutoff).Select(x => x.Size).ToList();
        var batches = _batchHistory.Where(x => x.Time >= cutoff).Select(x => x.Size).ToList();

        return (
            messages.Any() ? messages.Average() : 0,
            batches.Any() ? batches.Average() : 0
        );
    }

    public (double MessagesPerSecond, double BatchesPerSecond) GetRates(TimeSpan window)
    {
        var cutoff = DateTime.UtcNow - window;
        var messageCount = _messageHistory.Count(x => x.Time >= cutoff);
        var batchCount = _batchHistory.Count(x => x.Time >= cutoff);

        return (
            messageCount / window.TotalSeconds,
            batchCount / window.TotalSeconds
        );
    }
}
