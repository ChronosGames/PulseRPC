namespace PulseRPC.Transport;

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
