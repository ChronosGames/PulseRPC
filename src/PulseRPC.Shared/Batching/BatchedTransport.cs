using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using PulseRPC.Shared;

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
    private readonly CancellationTokenSource _cts;
    private Task? _processingTask;
    private Task? _flushTask;
    private readonly SemaphoreSlim _flushGate = new(1, 1);
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
    /// <exception cref="NotSupportedException">选择 <see cref="TransportBackpressureStrategy.DropOldest"/> 时抛出。</exception>
    public BatchedTransport(ITransport innerTransport, BatchedTransportOptions? options = null)
    {
        _innerTransport = innerTransport ?? throw new ArgumentNullException(nameof(innerTransport));
        _options = options ?? new BatchedTransportOptions();
        _options.Validate();
        if (_options.BackpressureStrategy == TransportBackpressureStrategy.DropOldest)
        {
            throw new NotSupportedException(
                "BatchedTransport 尚不能在 DropOldest 时可靠完成被丢弃请求；请使用 Block、DropNewest 或 Reject。");
        }

        _backpressureController = TransportBackpressureController.FromOptions(_options);

        // 初始化 Channel
        var channelOptions = new BoundedChannelOptions(_options.QueueCapacity)
        {
            // 非阻塞策略由 SendAsync 显式 TryWrite，以便每个被拒绝请求都能得到确定结果。
            FullMode = BoundedChannelFullMode.Wait,
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

        // reader 与定时刷新都属于装饰器生命周期，并在 DisposeAsync 中等待完成。
        _processingTask = Task.Run(ProcessSendRequestsAsync);
        _flushTask = Task.Run(() => FlushLoopAsync(_cts.Token));
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

        if (cancellationToken.IsCancellationRequested)
            return false;

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
            if (_options.BackpressureStrategy == TransportBackpressureStrategy.Block)
            {
                await _writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
            }
            else if (!_writer.TryWrite(request))
            {
                _metrics?.RecordSendRejected();
                if (_options.BackpressureStrategy == TransportBackpressureStrategy.Reject)
                {
                    throw new BackpressureRejectedException(
                        _backpressureController.CurrentLevel,
                        _sendChannel.Reader.Count,
                        _options.QueueCapacity);
                }

                return false;
            }

            // 等待发送完成
            return await completion.Task.ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            completion.TrySetResult(false);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _cts.IsCancellationRequested)
        {
            completion.TrySetResult(false);
            return false;
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

    private async Task ProcessSendRequestsAsync()
    {
        try
        {
            await foreach (var request in _reader.ReadAllAsync().ConfigureAwait(false))
            {
                lock (_batchLock)
                {
                    _pendingRequests.Add(request);
                    _pendingBytes += request.Data.Length;
                }

                // 检查是否需要立即刷新
                if (ShouldFlushBatch())
                {
                    await FlushBatchAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
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
        await _flushGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
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

            foreach (var request in batch)
            {
                var sent = false;
                try
                {
                    sent = await _innerTransport.SendAsync(request.Data, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    sent = false;
                }
                catch
                {
                    _metrics?.RecordSendError();
                }

                success &= sent;
                request.Completion.TrySetResult(sent);
            }

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
        finally
        {
            _flushGate.Release();
        }
    }

    private async Task FlushLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(_options.FlushInterval, cancellationToken).ConfigureAwait(false);
                await FlushBatchAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
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

            // 停止周期刷新；reader 不取消，而是排空所有已接受请求。
            _cts.Cancel();

            if (_flushTask != null)
            {
                try
                {
                    await _flushTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            if (_processingTask != null)
            {
                await _processingTask.ConfigureAwait(false);
            }

            await FlushBatchAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _cts.Dispose();
            _metrics?.Dispose();
        }
    }
}
