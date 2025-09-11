namespace PulseRPC.Transport;

public class MessageResponse
{
    public long SequenceId { get; set; }
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan ProcessingTime { get; set; }
}

// 5. 客户端消息基类
public abstract class ClientMessage
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
    public long SequenceId { get; set; }
    public MessagePriority Priority { get; set; } = MessagePriority.Normal;
    public DateTime ClientTimestamp { get; set; }
}

public enum MessagePriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}
