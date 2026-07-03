using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using PulseRPC.Transport;

namespace PulseRPC.Abstractions.Transport.Batching;

/// <summary>
/// 批处理传输装饰器 - 包装现有 ITransport 实现，提供批处理和背压能力
/// </summary>
/// <remarks>
/// <para>
/// <strong>使用方式</strong>：
/// </para>
/// <code>
/// var tcp = new TcpClientTransport(id, options, logger);
/// var batched = new BatchedTransport(tcp, new BatchedTransportOptions
/// {
///     BatchThreshold = 16,
///     QueueCapacity = 500,
///     TransportId = id
/// });
/// </code>
/// </remarks>
public sealed class BatchedTransport : IBatchedTransport, IAsyncDisposable
{
    private readonly ITransport _innerTransport;
    private readonly BatchedTransportOptions _options;
    private readonly TransportBackpressureController _backpressureController;
    private readonly TransportMetrics? _metrics;

    // Channel 和处理
    private readonly Channel<SendRequest> _sendChannel;
    private readonly ChannelWriter<SendRequest> _writer;
    private readonly ChannelReader<SendRequest> _reader;

    // 批次累积
    private readonly List<SendRequest> _pendingRequests;
    private readonly object _batchLock = new object();
    private int _pendingBytes;

    // 定时器和任务
    private readonly Timer _flushTimer;
    private readonly CancellationTokenSource _cts;
    private Task? _processingTask;
    private volatile bool _disposed;

    /// <summary>
    /// 发送请求
    /// </summary>
    private readonly struct SendRequest
    {
        public readonly ReadOnlyMemory<byte> Data;
        public readonly TaskCompletionSource<bool> Completion;
        public readonly long Timestamp;

        public SendRequest(ReadOnlyMemory<byte> data, TaskCompletionSource<bool> completion)
        {
            Data = data;
            Completion = completion;
            Timestamp = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>
    /// 创建批处理传输装饰器
    /// </summary>
    /// <param name="innerTransport">被包装的传输层</param>
    /// <param name="options">批处理配置</param>
    public BatchedTransport(ITransport innerTransport, BatchedTransportOptions? options = null)
    {
        _innerTransport = innerTransport ?? throw new ArgumentNullException(nameof(innerTransport));
        _options = options ?? new BatchedTransportOptions();
        _options.Validate();

        _backpressureController = TransportBackpressureController.FromOptions(_options);

        // 初始化 Channel
        var channelOptions = new BoundedChannelOptions(_options.QueueCapacity)
        {
            FullMode = _options.BackpressureStrategy == TransportBackpressureStrategy.Block
                ? BoundedChannelFullMode.Wait
                : BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        };

        _sendChannel = Channel.CreateBounded<SendRequest>(channelOptions);
        _writer = _sendChannel.Writer;
        _reader = _sendChannel.Reader;

        _pendingRequests = new List<SendRequest>(_options.BatchThreshold);
        _cts = new CancellationTokenSource();

        // 初始化指标
        if (_options.EnableMetrics)
        {
            _metrics = new TransportMetrics(
                _options.TransportId,
                () => _sendChannel.Reader.Count,
                () => (int)_backpressureController.CurrentLevel);
        }

        // 启动定时器
        _flushTimer = new Timer(
            OnFlushTimer,
            null,
            _options.FlushInterval,
            _options.FlushInterval);

        // 启动处理任务
        _processingTask = Task.Run(() => ProcessSendRequestsAsync(_cts.Token));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ITransport 实现（委托给内部传输）
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public string Id => _innerTransport.Id;

    /// <inheritdoc/>
    public TransportType Type => _innerTransport.Type;

    /// <inheritdoc/>
    public bool IsConnected => _innerTransport.IsConnected;

    /// <inheritdoc/>
    public ConnectionState State => _innerTransport.State;

    /// <inheritdoc/>
    public EndPoint LocalEndPoint => _innerTransport.LocalEndPoint;

    /// <inheritdoc/>
    public EndPoint RemoteEndPoint => _innerTransport.RemoteEndPoint;

    /// <inheritdoc/>
    public event EventHandler<TransportStateEventArgs>? StateChanged
    {
        add => _innerTransport.StateChanged += value;
        remove => _innerTransport.StateChanged -= value;
    }

    /// <inheritdoc/>
    public event EventHandler<TransportDataEventArgs>? DataReceived
    {
        add => _innerTransport.DataReceived += value;
        remove => _innerTransport.DataReceived -= value;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // IBatchedTransport 实现
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public BackpressureLevel BackpressureLevel => _backpressureController.CurrentLevel;

    /// <inheritdoc/>
    public int PendingSendCount => _sendChannel.Reader.Count;

    /// <inheritdoc/>
    public TransportMetricsSnapshot GetMetrics()
    {
        return _metrics?.GetSnapshot() ?? new TransportMetricsSnapshot
        {
            TransportId = _options.TransportId,
            PendingQueueDepth = PendingSendCount,
            BackpressureLevel = BackpressureLevel
        };
    }

    /// <inheritdoc/>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return;

        await FlushBatchAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return false;

        if (data.IsEmpty)
            return true;

        _metrics?.RecordSendRequest();

        // 检查背压
        var queueDepth = _sendChannel.Reader.Count;
        if (_backpressureController.ShouldReject(queueDepth, _options.BackpressureStrategy))
        {
            _metrics?.RecordSendRejected();

            if (_options.BackpressureStrategy == TransportBackpressureStrategy.Reject)
            {
                throw new BackpressureRejectedException(
                    _backpressureController.CurrentLevel,
                    queueDepth,
                    _options.QueueCapacity);
            }

            // DropNewest: 直接返回 false
            return false;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new SendRequest(data, completion);

        try
        {
            // 写入 Channel
            await _writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);

            // 等待发送完成
            return await completion.Task.ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            completion.TrySetResult(false);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetCanceled(cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            _metrics?.RecordSendError();
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 批处理逻辑
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task ProcessSendRequestsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var request in _reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                lock (_batchLock)
                {
                    _pendingRequests.Add(request);
                    _pendingBytes += request.Data.Length;
                }

                // 检查是否需要立即刷新
                if (ShouldFlushBatch())
                {
                    await FlushBatchAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 正常关闭
        }
        finally
        {
            // 刷新剩余数据
            await FlushBatchAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private bool ShouldFlushBatch()
    {
        lock (_batchLock)
        {
            // 达到消息数量阈值
            if (_pendingRequests.Count >= _options.BatchThreshold)
                return true;

            // 达到字节阈值
            if (_pendingBytes >= _options.BatchSizeThreshold)
                return true;

            return false;
        }
    }

    private async Task FlushBatchAsync(CancellationToken cancellationToken)
    {
        List<SendRequest> batch;
        int totalBytes;

        lock (_batchLock)
        {
            if (_pendingRequests.Count == 0)
                return;

            batch = new List<SendRequest>(_pendingRequests);
            totalBytes = _pendingBytes;

            _pendingRequests.Clear();
            _pendingBytes = 0;
        }

        var success = true;
        var startTime = Stopwatch.GetTimestamp();

        try
        {
            // 批量发送
            foreach (var request in batch)
            {
                var sent = await _innerTransport.SendAsync(request.Data, cancellationToken).ConfigureAwait(false);
                if (!sent)
                {
                    success = false;
                    break;
                }
            }

            // 记录指标
            if (success)
            {
                _metrics?.RecordBytesSent(totalBytes);
                _metrics?.RecordBatchFlushed(batch.Count);

                var elapsedTicks = Stopwatch.GetTimestamp() - startTime;
                var elapsed = TimeSpan.FromSeconds((double)elapsedTicks / Stopwatch.Frequency);
                _metrics?.RecordSendLatency(elapsed);
            }
            else
            {
                _metrics?.RecordSendError();
            }
        }
        catch (Exception)
        {
            success = false;
            _metrics?.RecordSendError();
        }

        // 完成所有请求
        foreach (var request in batch)
        {
            request.Completion.TrySetResult(success);
        }
    }

    private void OnFlushTimer(object? state)
    {
        if (_disposed)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await FlushBatchAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // 忽略定时器刷新错误
            }
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 资源释放
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            // 停止接收新请求
            _writer.TryComplete();

            // 取消处理任务
            _cts.Cancel();

            // 等待处理完成
            if (_processingTask != null)
            {
                try
                {
                    await _processingTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 预期的取消
                }
            }

            // 最后刷新
            await FlushBatchAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _flushTimer.Dispose();
            _cts.Dispose();
            _metrics?.Dispose();
        }
    }
}
