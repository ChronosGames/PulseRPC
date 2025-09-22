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
/// 连接上下文接口 - 会话层的连接抽象，集成了IConnection的功能
/// 实现思路：
/// - 会话管理：管理连接的会话状态和属性
/// - 序列化集成：集成序列化器进行数据转换
/// - 请求-响应映射：管理RPC请求和响应的对应关系
/// - 传输抽象：封装底层传输层细节
/// - 活动跟踪：跟踪连接活动状态，用于空闲检测
/// - 统一接口：集成IConnection功能，简化传输层架构
/// </summary>
public interface IClientChannel : IDisposable
{
    string Id { get; }

    /// <summary>
    /// 连接描述符
    /// </summary>
    ConnectionDescriptor Descriptor { get; }

    /// <summary>
    /// 连接状态
    /// </summary>
    ExtendedConnectionState State { get; }

    /// <summary>
    /// 连接统计信息
    /// </summary>
    ConnectionStatistics Statistics { get; }

    /// <summary>
    /// 连接标签
    /// </summary>
    Dictionary<string, string> Tags { get; }

    /// <summary>
    /// 连接状态变更事件
    /// </summary>
    event EventHandler<TransportStateEventArgs>? ConnectionStateChanged;

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

    /// <summary>
    /// 注册接收器
    /// </summary>
    Task RegisterReceiverAsync<T>(T receiver, CancellationToken cancellationToken = default) where T : class;
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
