using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PulseRPC.Abstractions.Transport.Batching;

/// <summary>
/// 传输层指标收集器 - 使用 System.Diagnostics.Metrics 实现
/// </summary>
/// <remarks>
/// <para>
/// <strong>与 OpenTelemetry 集成</strong>：
/// </para>
/// <para>
/// 此实现使用 .NET 内置的 <see cref="System.Diagnostics.Metrics"/> API，
/// 该 API 与 OpenTelemetry .NET SDK 完全兼容。只需添加 OpenTelemetry 包并配置
/// <c>AddMeter("PulseRPC.Shared")</c> 即可导出指标。
/// </para>
/// <para>
/// <strong>配置示例</strong>：
/// </para>
/// <code>
/// services.AddOpenTelemetry()
///     .WithMetrics(metrics =>
///     {
///         metrics.AddMeter("PulseRPC.Shared");
///         metrics.AddPrometheusExporter();
///     });
/// </code>
/// </remarks>
public sealed class TransportMetrics : IDisposable
{
    /// <summary>
    /// Meter 名称（用于 OpenTelemetry 配置）
    /// </summary>
    public const string MeterName = "PulseRPC.Shared";

    private readonly Meter _meter;
    private readonly string _transportId;

    // ═══════════════════════════════════════════════════════════════════════════
    // 计数器 (Counters) - 单调递增
    // ═══════════════════════════════════════════════════════════════════════════

    private readonly Counter<long> _bytesSentCounter;
    private readonly Counter<long> _sendRequestsCounter;
    private readonly Counter<long> _sendErrorsCounter;
    private readonly Counter<long> _sendRejectedCounter;
    private readonly Counter<long> _batchesFlushedCounter;

    // ═══════════════════════════════════════════════════════════════════════════
    // 直方图 (Histograms) - 用于延迟分布
    // ═══════════════════════════════════════════════════════════════════════════

    private readonly Histogram<double> _sendLatencyHistogram;
    private readonly Histogram<int> _batchSizeHistogram;

    // ═══════════════════════════════════════════════════════════════════════════
    // 本地计数器（避免频繁的 Interlocked 操作）
    // ═══════════════════════════════════════════════════════════════════════════

    private long _localBytesSent;
    private long _localSendRequests;
    private long _localSendErrors;
    private long _localSendRejected;
    private long _localBatchesFlushed;

    // 线程安全的累积值
    private long _totalBytesSent;
    private long _totalSendRequests;
    private long _totalSendErrors;
    private long _totalSendRejected;
    private long _totalBatchesFlushed;

    // 可观测仪表的回调函数
    private readonly Func<int>? _getPendingQueueDepth;
    private readonly Func<int>? _getBackpressureLevel;

    /// <summary>
    /// 创建 TransportMetrics 实例
    /// </summary>
    /// <param name="transportId">传输层标识</param>
    /// <param name="getPendingQueueDepth">获取队列深度的回调</param>
    /// <param name="getBackpressureLevel">获取背压等级的回调</param>
    public TransportMetrics(
        string transportId = "",
        Func<int>? getPendingQueueDepth = null,
        Func<int>? getBackpressureLevel = null)
    {
        _transportId = transportId ?? "";
        _getPendingQueueDepth = getPendingQueueDepth;
        _getBackpressureLevel = getBackpressureLevel;

        _meter = new Meter(MeterName, "1.0.0");

        var transportTag = new KeyValuePair<string, object?>("transport_id", _transportId);

        // ═══════════════════════════════════════════════════════════════════════
        // 初始化计数器
        // ═══════════════════════════════════════════════════════════════════════

        _bytesSentCounter = _meter.CreateCounter<long>(
            name: "pulserpc_transport_bytes_sent_total",
            unit: "By",
            description: "Total bytes sent through the transport");

        _sendRequestsCounter = _meter.CreateCounter<long>(
            name: "pulserpc_transport_send_requests_total",
            unit: "{request}",
            description: "Total number of send requests");

        _sendErrorsCounter = _meter.CreateCounter<long>(
            name: "pulserpc_transport_send_errors_total",
            unit: "{error}",
            description: "Total number of send errors");

        _sendRejectedCounter = _meter.CreateCounter<long>(
            name: "pulserpc_transport_send_rejected_total",
            unit: "{request}",
            description: "Total number of sends rejected due to backpressure");

        _batchesFlushedCounter = _meter.CreateCounter<long>(
            name: "pulserpc_transport_batches_flushed_total",
            unit: "{batch}",
            description: "Total number of batches flushed");

        // ═══════════════════════════════════════════════════════════════════════
        // 初始化直方图
        // ═══════════════════════════════════════════════════════════════════════

        _sendLatencyHistogram = _meter.CreateHistogram<double>(
            name: "pulserpc_transport_send_latency_seconds",
            unit: "s",
            description: "Send latency in seconds");

        _batchSizeHistogram = _meter.CreateHistogram<int>(
            name: "pulserpc_transport_batch_size",
            unit: "{message}",
            description: "Number of messages per batch");

        // ═══════════════════════════════════════════════════════════════════════
        // 初始化可观测仪表
        // ═══════════════════════════════════════════════════════════════════════

        if (_getPendingQueueDepth != null)
        {
            _meter.CreateObservableGauge(
                name: "pulserpc_transport_pending_queue_depth",
                observeValue: () => _getPendingQueueDepth(),
                unit: "{message}",
                description: "Current number of messages pending in the send queue");
        }

        if (_getBackpressureLevel != null)
        {
            _meter.CreateObservableGauge(
                name: "pulserpc_transport_backpressure_level",
                observeValue: () => _getBackpressureLevel(),
                unit: "{level}",
                description: "Current backpressure level (0=None, 1=Throttle, 2=Reject)");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 记录方法
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 记录发送的字节数
    /// </summary>
    public void RecordBytesSent(int bytes)
    {
        _localBytesSent += bytes;
        _bytesSentCounter.Add(bytes, new KeyValuePair<string, object?>("transport_id", _transportId));
    }

    /// <summary>
    /// 记录发送请求
    /// </summary>
    public void RecordSendRequest()
    {
        _localSendRequests++;
        _sendRequestsCounter.Add(1, new KeyValuePair<string, object?>("transport_id", _transportId));
    }

    /// <summary>
    /// 记录发送错误
    /// </summary>
    public void RecordSendError()
    {
        _localSendErrors++;
        _sendErrorsCounter.Add(1, new KeyValuePair<string, object?>("transport_id", _transportId));
    }

    /// <summary>
    /// 记录背压拒绝
    /// </summary>
    public void RecordSendRejected()
    {
        _localSendRejected++;
        _sendRejectedCounter.Add(1, new KeyValuePair<string, object?>("transport_id", _transportId));
    }

    /// <summary>
    /// 记录批次刷新
    /// </summary>
    /// <param name="batchSize">批次大小（消息数）</param>
    public void RecordBatchFlushed(int batchSize)
    {
        _localBatchesFlushed++;
        _batchesFlushedCounter.Add(1, new KeyValuePair<string, object?>("transport_id", _transportId));
        _batchSizeHistogram.Record(batchSize, new KeyValuePair<string, object?>("transport_id", _transportId));
    }

    /// <summary>
    /// 记录发送延迟
    /// </summary>
    /// <param name="latency">延迟时长</param>
    public void RecordSendLatency(TimeSpan latency)
    {
        _sendLatencyHistogram.Record(latency.TotalSeconds,
            new KeyValuePair<string, object?>("transport_id", _transportId));
    }

    /// <summary>
    /// 刷新本地计数器到累积值
    /// </summary>
    public void Flush()
    {
        Interlocked.Add(ref _totalBytesSent, _localBytesSent);
        Interlocked.Add(ref _totalSendRequests, _localSendRequests);
        Interlocked.Add(ref _totalSendErrors, _localSendErrors);
        Interlocked.Add(ref _totalSendRejected, _localSendRejected);
        Interlocked.Add(ref _totalBatchesFlushed, _localBatchesFlushed);

        _localBytesSent = 0;
        _localSendRequests = 0;
        _localSendErrors = 0;
        _localSendRejected = 0;
        _localBatchesFlushed = 0;
    }

    /// <summary>
    /// 获取指标快照
    /// </summary>
    public TransportMetricsSnapshot GetSnapshot()
    {
        Flush();

        return new TransportMetricsSnapshot
        {
            TransportId = _transportId,
            BytesSent = Interlocked.Read(ref _totalBytesSent),
            SendRequests = Interlocked.Read(ref _totalSendRequests),
            SendErrors = Interlocked.Read(ref _totalSendErrors),
            SendRejected = Interlocked.Read(ref _totalSendRejected),
            BatchesFlushed = Interlocked.Read(ref _totalBatchesFlushed),
            PendingQueueDepth = _getPendingQueueDepth?.Invoke() ?? 0,
            BackpressureLevel = (BackpressureLevel)(_getBackpressureLevel?.Invoke() ?? 0)
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Flush();
        _meter.Dispose();
    }
}
