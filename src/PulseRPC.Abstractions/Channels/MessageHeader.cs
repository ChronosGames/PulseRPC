using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using MemoryPack;
using PulseRPC.Client;
using PulseRPC.Transport;

namespace PulseRPC.Messaging;

/// <summary>
/// 扩展的连接状态枚举 - 支持更详细的状态跟踪
/// </summary>
public enum ExtendedConnectionState
{
    /// <summary>
    /// 未初始化
    /// </summary>
    Uninitialized,

    /// <summary>
    /// 初始化中
    /// </summary>
    Initializing,

    /// <summary>
    /// 连接中
    /// </summary>
    Connecting,

    /// <summary>
    /// 已连接
    /// </summary>
    Connected,

    /// <summary>
    /// 空闲中
    /// </summary>
    Idle,

    /// <summary>
    /// 活跃中
    /// </summary>
    Active,

    /// <summary>
    /// 重连中
    /// </summary>
    Reconnecting,

    /// <summary>
    /// 断开中
    /// </summary>
    Disconnecting,

    /// <summary>
    /// 已断开
    /// </summary>
    Disconnected,

    /// <summary>
    /// 失败
    /// </summary>
    Failed,

    /// <summary>
    /// 已释放
    /// </summary>
    Disposed
}

/// <summary>
/// 连接状态转换辅助类
/// </summary>
public static class ConnectionStateConverter
{
    /// <summary>
    /// 将 ConnectionState 转换为 ExtendedConnectionState
    /// </summary>
    public static ExtendedConnectionState ToExtended(this ConnectionState state)
    {
        return state switch
        {
            ConnectionState.Disconnected => ExtendedConnectionState.Disconnected,
            ConnectionState.Connecting => ExtendedConnectionState.Connecting,
            ConnectionState.Connected => ExtendedConnectionState.Connected,
            ConnectionState.Disconnecting => ExtendedConnectionState.Disconnecting,
            ConnectionState.Failed => ExtendedConnectionState.Failed,
            ConnectionState.Reconnecting => ExtendedConnectionState.Reconnecting,
            _ => ExtendedConnectionState.Uninitialized
        };
    }

    /// <summary>
    /// 将 ExtendedConnectionState 转换为 ConnectionState
    /// </summary>
    public static ConnectionState ToBasic(this ExtendedConnectionState state)
    {
        return state switch
        {
            ExtendedConnectionState.Uninitialized => ConnectionState.Disconnected,
            ExtendedConnectionState.Initializing => ConnectionState.Connecting,
            ExtendedConnectionState.Connecting => ConnectionState.Connecting,
            ExtendedConnectionState.Connected => ConnectionState.Connected,
            ExtendedConnectionState.Idle => ConnectionState.Connected,
            ExtendedConnectionState.Active => ConnectionState.Connected,
            ExtendedConnectionState.Reconnecting => ConnectionState.Reconnecting,
            ExtendedConnectionState.Disconnecting => ConnectionState.Disconnecting,
            ExtendedConnectionState.Disconnected => ConnectionState.Disconnected,
            ExtendedConnectionState.Failed => ConnectionState.Failed,
            ExtendedConnectionState.Disposed => ConnectionState.Disconnected,
            _ => ConnectionState.Disconnected
        };
    }
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

    /// <summary>
    /// 协议号 - 用于高性能方法路由（替代 ServiceName+MethodName）
    /// 值为 0 表示使用传统的 ServiceName+MethodName 路由
    /// </summary>
    [MemoryPackOrder(4)]
    public ushort ProtocolId { get; set; }

    [MemoryPackOrder(5)]
    public MessageFlags Flags { get; set; }

    [MemoryPackOrder(6)]
    public long Timestamp { get; set; }

    [MemoryPackOrder(7)]
    public ushort SequenceNumber { get; set; }

    /// <summary>
    /// 目标服务实例键（Actor / keyed-hub 的实例标识，即完整 ServiceId <c>"ServiceName:BusinessId"</c> 中的 <c>BusinessId</c> 部分）。
    /// <para>
    /// 空字符串表示「无实例键」（非 keyed 服务，或由服务端自行解析实例）。
    /// </para>
    /// <para>
    /// 该字段与 <see cref="ServiceName"/>（Hub）、<see cref="ProtocolId"/>（MethodId）共同构成
    /// <strong>只读信封头地址</strong>：网关（Gateway）可仅凭信封头
    /// （Hub=<see cref="ServiceName"/> / Key=<see cref="ServiceKey"/> / MethodId=<see cref="ProtocolId"/>）
    /// 决定目标节点/实例并转发原始帧，<strong>无需反序列化消息体（body）</strong>。
    /// 参见 <see cref="EnvelopeRelay"/> 与 <see cref="ReadOnlyEnvelopeHeader"/>。
    /// </para>
    /// <para>
    /// 兼容性：作为 MemoryPack 尾部新增成员，旧版本序列化的头部（不含该字段）反序列化后此值为空字符串，
    /// 新旧两端可互操作。
    /// </para>
    /// </summary>
    [MemoryPackOrder(8)]
    public string ServiceKey { get; set; } = string.Empty;

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
    ReverseRequest = 10, // 反向请求（服务端→客户端，需应答；客户端以 Response/Error 回显 MessageId 应答）
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
    Error = 64,             // 错误响应
}

public class NetworkMessage
{
    public MessageHeader Header;
    public byte[] Body;

    /// <summary>
    /// 零拷贝消息体（优先使用此属性）
    /// </summary>
    public ReadOnlyMemory<byte> BodyMemory { get; }

    /// <summary>
    /// 是否使用零拷贝模式
    /// </summary>
    public bool IsZeroCopy { get; }

    public NetworkMessage(MessageHeader header, byte[]? body)
    {
        Header = header ?? throw new ArgumentNullException(nameof(header));
        Body = body ?? Array.Empty<byte>();
        BodyMemory = Body;
        IsZeroCopy = false;
    }

    /// <summary>
    /// 零拷贝构造函数 - 避免数组分配
    /// </summary>
    public NetworkMessage(MessageHeader header, ReadOnlyMemory<byte> bodyMemory)
    {
        Header = header ?? throw new ArgumentNullException(nameof(header));
        BodyMemory = bodyMemory;
        Body = Array.Empty<byte>(); // 保持兼容性，但不使用
        IsZeroCopy = true;
    }

    /// <summary>
    /// 获取消息体（零拷贝优先）
    /// </summary>
    public ReadOnlyMemory<byte> GetBody() => IsZeroCopy ? BodyMemory : Body;
}

/// <summary>
/// 连接状态变化事件参数 - 客户端级别
/// </summary>
public sealed class ConnectionStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 连接ID
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// 前一个状态
    /// </summary>
    public ExtendedConnectionState PreviousState { get; set; }

    /// <summary>
    /// 当前状态
    /// </summary>
    public ExtendedConnectionState CurrentState { get; set; }

    /// <summary>
    /// 状态变化时间
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 状态变化原因
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// 关联的异常（如果有）
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// 额外的上下文信息
    /// </summary>
    public Dictionary<string, object> Context { get; set; } = new();
}
