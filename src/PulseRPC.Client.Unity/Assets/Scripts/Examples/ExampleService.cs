using System;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC.Protocol.Attributes;

namespace PulseRPC.Examples
{
    [GameService]
    public interface IPlayerService : INetworkService
    {
        Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

        Task MoveAsync(MoveRequest request, CancellationToken cancellationToken = default);

        // 使用SubscriptionMethod特性标记订阅方法，避免仅靠命名约定
        [SubscriptionMethod("PlayerJoined")]
        ISubscriptionToken SubscribeToPlayerJoined(NetworkEventHandler<PlayerJoinedEvent> handler);

        [SubscriptionMethod("PlayerLeft")]
        ISubscriptionToken SubscribeToPlayerLeft(NetworkEventHandler<PlayerLeftEvent> handler);

        [SubscriptionMethod("PlayerMoved")]
        ISubscriptionToken SubscribeToPlayerMoved(NetworkEventHandler<PlayerMovedEvent> handler);
    }

    /// <summary>
    /// 问候请求消息
    /// </summary>
    [MemoryPackable]
    public partial class GreetingRequest
    {
        public string Name { get; set; }
    }

    /// <summary>
    /// 问候响应消息
    /// </summary>
    [MemoryPackable]
    public partial class GreetingResponse
    {
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// 计算请求消息
    /// </summary>
    [MemoryPackable]
    public partial struct CalculationRequest
    {
        public int A { get; set; }
        public int B { get; set; }
    }

    /// <summary>
    /// 计算响应消息
    /// </summary>
    [MemoryPackable]
    public partial struct CalculationResponse
    {
        public int Sum { get; set; }
        public int Product { get; set; }
        public float Division { get; set; }
    }

    /// <summary>
    /// 服务器通知消息
    /// </summary>
    [MemoryPackable]
    public partial class ServerNotification
    {
        public string Content { get; set; }
        public NotificationType Type { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// 通知类型
    /// </summary>
    public enum NotificationType
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// 服务器通知处理器
    /// </summary>
    public class ServerNotificationHandler : MessageHandler<ServerNotification>
    {
        private readonly Action<ServerNotification> _onNotificationReceived;

        public ServerNotificationHandler(Action<ServerNotification> onNotificationReceived)
        {
            _onNotificationReceived = onNotificationReceived;
        }

        public override void Handle(ServerNotification message)
        {
            // 调用回调函数处理通知
            _onNotificationReceived?.Invoke(message);
        }
    }
}
