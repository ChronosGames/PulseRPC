using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;

namespace PulseRPC.Messaging
{
    /// <summary>
    /// 消息通道接口
    /// </summary>
    public interface IClientChannel : IDisposable
    {
        Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

        Task DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 注册事件回调
        /// </summary>
        void RegisterEventCallback(Action<string, byte[]> callback);

        Task<TResponse> SendRequestAsync<TRequest, TResponse>(string serviceName, string methodName, TRequest request,
            CancellationToken cancellationToken = default);

        Task SendEventAsync<T>(string eventName, T eventData, CancellationToken cancellationToken = default);

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
}
