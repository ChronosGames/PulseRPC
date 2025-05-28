using System;
using System.Numerics;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC;

#nullable enable

namespace ChatApp.Shared
{
    /// <summary>
    /// 指定默认使用TCP通道
    /// </summary>
    [ServiceContract]
    [Channel("TcpChannel")]
    public interface IPlayerService : INetworkService
    {
        /// <summary>
        /// 玩家登录
        /// </summary>
        [Operation]
        ValueTask<LoginResponse> LoginAsync(LoginRequest request);

        /// <summary>
        /// 玩家移动
        /// </summary>
        [Operation]
        [Channel("KcpChannel")]
        ValueTask MoveAsync(MoveRequest request);

        /// <summary>
        /// 测试Ping方法（允许匿名访问）
        /// </summary>
        [Operation]
        ValueTask<string> PingAsync(PingRequest request);
    }

    /// <summary>
    /// 事件接口 - 登录相关事件使用TCP
    /// </summary>
    [EventContract]
    [Channel("TcpChannel")]
    public interface IPlayerLoginEvents : IEventSubscriber
    {
        // 下行通知: 玩家加入游戏
        [PulseRPC.Event]
        void OnPlayerJoined(PlayerJoinedEvent eventData);

        // 下行通知: 玩家离开游戏
        [PulseRPC.Event]
        void OnPlayerLeft(PlayerLeftEvent eventData);
    }

    /// <summary>
    /// 事件接口 - 位置更新使用KCP
    /// </summary>
    [EventContract]
    [Channel("KcpChannel")]
    public interface IPlayerMovementEvents : IEventSubscriber
    {
        // 下行通知: 玩家移动
        [PulseRPC.Event]
        void OnPlayerMoved(PlayerMovedEvent eventData);

        // 下行通知: 批量玩家移动
        [PulseRPC.Event]
        void OnPlayersMovedBatch(PlayerMovedEvent[] eventData);

        [PulseRPC.Event]
        void OnPlayersMovedBatch(PlayersBatchMovedEvent eventData);
    }

    // 请求/响应模型类
    [MemoryPackable]
    public partial class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    [MemoryPackable]
    public partial class LoginResponse
    {
        public bool Success { get; set; }
        public string Token { get; set; } = string.Empty;
        public PlayerInfo? Player { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    [MemoryPackable]
    public partial class PlayerInfo
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public int Level { get; set; }
        public string AvatarUrl { get; set; } = string.Empty;
    }

    [MemoryPackable]
    public partial class MoveRequest
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float RotationY { get; set; }
    }

    // 事件模型类
    [MemoryPackable]
    public partial class PlayerJoinedEvent : IEventData
    {
        public Guid PlayerId { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public Vector3 Position { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }

    [MemoryPackable]
    public partial class PlayerLeftEvent : IEventData
    {
        public Guid PlayerId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    [MemoryPackable]
    public partial class PlayerMovedEvent : IEventData
    {
        public Guid PlayerId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float RotationY { get; set; }
        public bool IsRunning { get; set; }
    }

    /// <summary>
    /// 玩家移动事件数据
    /// </summary>
    [MemoryPackable]
    public partial class PlayersBatchMovedEvent : IEventData
    {
        public PlayerMovedEvent[] Updates { get; set; } = Array.Empty<PlayerMovedEvent>();
    }

    /// <summary>
    /// Ping请求
    /// </summary>
    [MemoryPackable]
    public partial class PingRequest
    {
        public string Message { get; set; } = string.Empty;
    }
}
