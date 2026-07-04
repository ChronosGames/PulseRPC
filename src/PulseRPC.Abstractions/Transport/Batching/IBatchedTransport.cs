using PulseRPC.Shared;

namespace PulseRPC.Abstractions.Transport.Batching;

/// <summary>
/// 批处理传输接口 - 扩展 ITransport 提供批处理和背压能力
/// </summary>
public interface IBatchedTransport : ITransport
{
    /// <summary>
    /// 当前背压等级
    /// </summary>
    BackpressureLevel BackpressureLevel { get; }

    /// <summary>
    /// 待发送消息数量
    /// </summary>
    int PendingSendCount { get; }

    /// <summary>
    /// 获取传输层指标快照
    /// </summary>
    TransportMetricsSnapshot GetMetrics();

    /// <summary>
    /// 强制刷新所有待发送消息
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 传输层指标快照
/// </summary>
public sealed class TransportMetricsSnapshot
{
    /// <summary>
    /// 传输层标识
    /// </summary>
    public string TransportId { get; set; } = "";

    /// <summary>
    /// 发送的字节总数
    /// </summary>
    public long BytesSent { get; set; }

    /// <summary>
    /// 发送请求总数
    /// </summary>
    public long SendRequests { get; set; }

    /// <summary>
    /// 发送错误数
    /// </summary>
    public long SendErrors { get; set; }

    /// <summary>
    /// 背压拒绝数
    /// </summary>
    public long SendRejected { get; set; }

    /// <summary>
    /// 批次刷新数
    /// </summary>
    public long BatchesFlushed { get; set; }

    /// <summary>
    /// 当前队列深度
    /// </summary>
    public int PendingQueueDepth { get; set; }

    /// <summary>
    /// 当前背压等级
    /// </summary>
    public BackpressureLevel BackpressureLevel { get; set; }
}
