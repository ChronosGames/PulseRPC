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
/// 消息头部 - 使用简化的MemoryPack序列化
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

    [MemoryPackOrder(3)]
    public string MethodName { get; set; } = string.Empty;
}

// 消息类型枚举
public enum MessageType : byte
{
    Request = 1, // 上行请求(需响应)
    Response = 2, // 响应
    Ping = 5, // Ping
    Pong = 6, // Pong
    Event = 7, // 事件
}
