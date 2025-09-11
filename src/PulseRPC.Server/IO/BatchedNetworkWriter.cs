using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PulseRPC.Server.IO;

/// <summary>
/// 批量网络写入器 - 聚合多个写入操作到单个系统调用
/// 实现高吞吐量的网络I/O优化
/// </summary>
public sealed class BatchedNetworkWriter : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly ILogger _logger;
    private readonly BatchedWriterOptions _options;
    private readonly Channel<WriteRequest> _writeChannel;
    private readonly ChannelWriter<WriteRequest> _writer;
    private readonly ChannelReader<WriteRequest> _reader;
    private readonly Timer _flushTimer;
    private readonly SemaphoreSlim _flushSemaphore;

    private readonly List<ReadOnlyMemory<byte>> _pendingBuffers;
    private readonly List<TaskCompletionSource<bool>> _pendingCompletions;
    private readonly Lock _batchLock = new Lock();

    private long _totalBytesWritten;
    private long _totalBatchesFlushed;
    private long _totalWriteRequests;
    private volatile bool _disposed;
    private Task? _processingTask;
    private CancellationTokenSource? _cancellationTokenSource;

    /// <summary>
    /// 批量写入选项
    /// </summary>
    public sealed class BatchedWriterOptions
    {
        /// <summary>批处理阈值 - 达到此数量立即刷新</summary>
        public int BatchThreshold { get; set; } = 16;

        /// <summary>批处理字节阈值 - 达到此字节数立即刷新</summary>
        public int BatchSizeThreshold { get; set; } = 64 * 1024; // 64KB

        /// <summary>刷新间隔 - 最大等待时间</summary>
        public TimeSpan FlushInterval { get; set; } = TimeSpan.FromMilliseconds(2);

        /// <summary>通道容量 - 待处理写入请求的最大数量</summary>
        public int ChannelCapacity { get; set; } = 1000;

        /// <summary>是否启用自适应批处理</summary>
        public bool EnableAdaptiveBatching { get; set; } = true;

        /// <summary>自适应调整因子</summary>
        public double AdaptiveFactor { get; set; } = 0.1;
    }

    /// <summary>
    /// 写入请求
    /// </summary>
    private readonly struct WriteRequest
    {
        public readonly ReadOnlyMemory<byte> Data;
        public readonly TaskCompletionSource<bool> Completion;
        public readonly long Timestamp;

        public WriteRequest(ReadOnlyMemory<byte> data, TaskCompletionSource<bool> completion)
        {
            Data = data;
            Completion = completion;
            Timestamp = Environment.TickCount64;
        }
    }

    /// <summary>
    /// 构造函数
    /// </summary>
    public BatchedNetworkWriter(Stream stream, BatchedWriterOptions? options = null, ILogger? logger = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _options = options ?? new BatchedWriterOptions();
        _logger = logger ?? NullLogger.Instance;

        // 创建有界通道以支持背压
        var channelOptions = new BoundedChannelOptions(_options.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        };

        _writeChannel = Channel.CreateBounded<WriteRequest>(channelOptions);
        _writer = _writeChannel.Writer;
        _reader = _writeChannel.Reader;

        _pendingBuffers = new List<ReadOnlyMemory<byte>>(_options.BatchThreshold);
        _pendingCompletions = new List<TaskCompletionSource<bool>>(_options.BatchThreshold);
        _flushSemaphore = new SemaphoreSlim(1, 1);

        // 创建刷新定时器
        _flushTimer = new Timer(OnFlushTimer, null, _options.FlushInterval, _options.FlushInterval);

        // 启动处理任务
        _cancellationTokenSource = new CancellationTokenSource();
        _processingTask = ProcessWriteRequestsAsync(_cancellationTokenSource.Token);
    }

    /// <summary>
    /// 性能统计
    /// </summary>
    public (long TotalBytesWritten, long TotalBatchesFlushed, long TotalWriteRequests, double AverageBatchSize) Statistics
    {
        get
        {
            var bytesWritten = Interlocked.Read(ref _totalBytesWritten);
            var batchesFlushed = Interlocked.Read(ref _totalBatchesFlushed);
            var writeRequests = Interlocked.Read(ref _totalWriteRequests);
            var avgBatchSize = batchesFlushed > 0 ? (double)writeRequests / batchesFlushed : 0;

            return (bytesWritten, batchesFlushed, writeRequests, avgBatchSize);
        }
    }

    /// <summary>
    /// 异步写入数据 - 主要API
    /// </summary>
    public async ValueTask<bool> WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return false;

        if (data.IsEmpty)
            return true;

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new WriteRequest(data, completion);

        try
        {
            // 写入请求到通道
            await _writer.WriteAsync(request, cancellationToken);
            Interlocked.Increment(ref _totalWriteRequests);

            // 等待处理完成
            return await completion.Task;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetCanceled(cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            _logger.LogError(ex, "写入数据到批量写入器失败");
            return false;
        }
    }

    /// <summary>
    /// 立即刷新所有待处理的数据
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return;

        await _flushSemaphore.WaitAsync(cancellationToken);
        try
        {
            await FlushBatchAsync(cancellationToken);
        }
        finally
        {
            _flushSemaphore.Release();
        }
    }

    /// <summary>
    /// 处理写入请求的主循环
    /// </summary>
    private async Task ProcessWriteRequestsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var request in _reader.ReadAllAsync(cancellationToken))
            {
                lock (_batchLock)
                {
                    _pendingBuffers.Add(request.Data);
                    _pendingCompletions.Add(request.Completion);
                }

                // 检查是否需要立即刷新
                if (ShouldFlushBatch())
                {
                    await TryFlushBatchAsync(cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 正常关闭
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理写入请求异常");
        }
        finally
        {
            // 刷新剩余数据
            await TryFlushBatchAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// 检查是否应该刷新批次
    /// </summary>
    private bool ShouldFlushBatch()
    {
        lock (_batchLock)
        {
            // 达到批处理数量阈值
            if (_pendingBuffers.Count >= _options.BatchThreshold)
                return true;

            // 达到批处理字节阈值
            var totalBytes = 0;
            foreach (var buffer in _pendingBuffers)
            {
                totalBytes += buffer.Length;
                if (totalBytes >= _options.BatchSizeThreshold)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 尝试刷新批次（带错误处理）
    /// </summary>
    private async Task TryFlushBatchAsync(CancellationToken cancellationToken)
    {
        if (await _flushSemaphore.WaitAsync(100, cancellationToken))
        {
            try
            {
                await FlushBatchAsync(cancellationToken);
            }
            finally
            {
                _flushSemaphore.Release();
            }
        }
    }

    /// <summary>
    /// 刷新当前批次
    /// </summary>
    private async Task FlushBatchAsync(CancellationToken cancellationToken)
    {
        List<ReadOnlyMemory<byte>> buffers;
        List<TaskCompletionSource<bool>> completions;

        // 获取当前批次数据
        lock (_batchLock)
        {
            if (_pendingBuffers.Count == 0)
                return;

            buffers = new List<ReadOnlyMemory<byte>>(_pendingBuffers);
            completions = new List<TaskCompletionSource<bool>>(_pendingCompletions);

            _pendingBuffers.Clear();
            _pendingCompletions.Clear();
        }

        var success = false;
        var totalBytes = 0L;

        try
        {
            // 批量写入到流
            foreach (var buffer in buffers)
            {
                await _stream.WriteAsync(buffer, cancellationToken);
                totalBytes += buffer.Length;
            }

            // 确保数据写入
            await _stream.FlushAsync(cancellationToken);

            success = true;

            // 更新统计信息
            Interlocked.Add(ref _totalBytesWritten, totalBytes);
            Interlocked.Increment(ref _totalBatchesFlushed);

            _logger.LogDebug("刷新批次完成: {Count}个请求, {Bytes}字节",
                buffers.Count, totalBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刷新批次失败: {Count}个请求, {Bytes}字节",
                buffers.Count, totalBytes);
        }

        // 完成所有待处理的任务
        foreach (var completion in completions)
        {
            if (success)
                completion.TrySetResult(true);
            else
                completion.TrySetResult(false);
        }

        // 自适应调整（如果启用）
        if (_options.EnableAdaptiveBatching && success)
        {
            AdaptBatchParameters(buffers.Count, totalBytes);
        }
    }

    /// <summary>
    /// 自适应调整批处理参数
    /// </summary>
    private void AdaptBatchParameters(int batchCount, long batchBytes)
    {
        // 基于性能反馈调整批处理阈值
        var avgBatchSize = Statistics.AverageBatchSize;

        if (avgBatchSize > _options.BatchThreshold * 0.8)
        {
            // 批次较大，可以适当增加阈值
            var newThreshold = (int)(_options.BatchThreshold * (1 + _options.AdaptiveFactor));
            _options.BatchThreshold = Math.Min(newThreshold, 64);
        }
        else if (avgBatchSize < _options.BatchThreshold * 0.3)
        {
            // 批次较小，可以适当减少阈值
            var newThreshold = (int)(_options.BatchThreshold * (1 - _options.AdaptiveFactor));
            _options.BatchThreshold = Math.Max(newThreshold, 4);
        }

        _logger.LogTrace("自适应调整批处理阈值: {OldThreshold} -> {NewThreshold}",
            _options.BatchThreshold, _options.BatchThreshold);
    }

    /// <summary>
    /// 定时器回调 - 定期刷新
    /// </summary>
    private void OnFlushTimer(object? state)
    {
        if (_disposed)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await TryFlushBatchAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "定时刷新异常");
            }
        });
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
            // 停止接收新的写入请求
            _writer.TryComplete();

            // 取消处理任务
            _cancellationTokenSource?.Cancel();

            // 等待处理任务完成
            if (_processingTask != null)
            {
                try
                {
                    await _processingTask;
                }
                catch (OperationCanceledException)
                {
                    // 预期的取消
                }
            }

            // 最后一次刷新
            await FlushAsync(CancellationToken.None);

            _logger.LogInformation("BatchedNetworkWriter已释放 - 统计信息: {Statistics}", Statistics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "释放BatchedNetworkWriter时异常");
        }
        finally
        {
            _flushTimer?.Dispose();
            _flushSemaphore?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }
}
