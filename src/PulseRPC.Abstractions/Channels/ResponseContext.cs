namespace PulseRPC.Channels;

/// <summary>
/// 响应上下文 - 用于管理待处理的RPC响应
/// </summary>
/// <remarks>
/// 此类在零拷贝优化场景中使用，用于跟踪待处理的响应
/// </remarks>
public class ResponseContext
{
    /// <summary>
    /// 消息ID
    /// </summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// 任务完成源
    /// </summary>
    public TaskCompletionSource<ReadOnlyMemory<byte>> Tcs { get; set; } = null!;

    /// <summary>
    /// 入队时间戳（用于超时检测）
    /// </summary>
    public long EnqueueTimestamp { get; set; }

    /// <summary>
    /// 取消注册
    /// </summary>
    public CancellationTokenRegistration CancellationRegistration { get; set; }
}

