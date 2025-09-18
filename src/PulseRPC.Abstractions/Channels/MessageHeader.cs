using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;

namespace PulseRPC.Messaging;

/// <summary>
/// 客户端通道接口
/// </summary>
public interface IClientChannel : IDisposable
{
    /// <summary>
    /// 连接到服务器
    /// </summary>
    Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开连接
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 注册事件回调
    /// </summary>
    void RegisterEventCallback(Action<string, byte[]> callback);

    /// <summary>
    /// 发送请求
    /// </summary>
    Task<TResponse> SendRequestAsync<TRequest, TResponse>(string serviceName, string methodName, TRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送事件
    /// </summary>
    Task SendEventAsync<T>(string eventName, T eventData, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送消息
    /// </summary>
    Task SendAsync<T>(string eventName, T message, CancellationToken cancellationToken = default);

    /// <summary>
    /// 订阅事件
    /// </summary>
    ISubscriptionToken SubscribeToEvent<T>(string eventName, System.EventHandler<T> handler);
}

/// <summary>
/// 高性能消息头部 - 紧凑布局，零分配设计
/// </summary>
[MemoryPackable]
public partial class MessageHeader
{
    [MemoryPackOrder(0)]
    public MessageType Type { get; set; }

    [MemoryPackOrder(1)]
    public Guid MessageId { get; set; }

    [MemoryPackOrder(2)]
    public string ServiceName { get; set; } = string.Empty;

    [MemoryPackOrder(3)] public string MethodName { get; set; } = string.Empty;

    [MemoryPackOrder(4)] public int PayloadLength { get; set; }

    [MemoryPackOrder(5)]
    public MessageFlags Flags { get; set; }

    [MemoryPackOrder(6)]
    public long Timestamp { get; set; }

    [MemoryPackOrder(7)]
    public ushort SequenceNumber { get; set; }

    [MemoryPackConstructor]
    public MessageHeader()
    {
    }

    public MessageHeader(MessageType type, string serviceName, string methodName)
    {
        Type = type;
        MessageId = Guid.NewGuid();
        ServiceName = serviceName ?? string.Empty;
        MethodName = methodName ?? string.Empty;
        PayloadLength = 0;
        Flags = MessageFlags.None;
        Timestamp = DateTimeOffset.UtcNow.Ticks;
        SequenceNumber = 0;
    }
}

/// <summary>
/// 消息类型枚举
/// </summary>
public enum MessageType : byte
{
    Request = 1,        // 上行请求(需响应)
    Response = 2,       // 响应
    OneWay = 3,        // 单向消息(无需响应)
    Ping = 5,          // Ping
    Pong = 6,          // Pong
    Event = 7,         // 事件
    Error = 8,         // 错误响应
    Cancel = 9,        // 取消请求
}

/// <summary>
/// 消息标志位
/// </summary>
[Flags]
public enum MessageFlags : byte
{
    None = 0,
    Compressed = 1,         // 压缩
    Encrypted = 2,          // 加密
    RequireResponse = 4,    // 需要响应
    HighPriority = 8,       // 高优先级
    Reliable = 16,          // 可靠传输
    Ordered = 32,           // 有序传输
}

public class NetworkMessage
{
    public MessageHeader Header;
    public byte[] Body;
    public ReadOnlyMemory<byte> Payload;

    public NetworkMessage(MessageHeader header, byte[]? body)
    {
        Header = header ?? throw new ArgumentNullException(nameof(header));
        Body = body ?? Array.Empty<byte>();
        Payload = new ReadOnlyMemory<byte>(Body);
    }
}
