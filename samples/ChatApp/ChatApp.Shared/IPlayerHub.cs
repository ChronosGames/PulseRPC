using System;
using System.Numerics;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC;

#nullable enable

namespace ChatApp
{
    /// <summary>
    /// 指定默认使用TCP通道
    /// </summary>
    [Channel("TcpChannel")]
    public interface IPlayerHub : IPulseHub
    {
        /// <summary>
        /// 玩家登录
        /// </summary>
        ValueTask<LoginResponse> LoginAsync(LoginRequest request);

        /// <summary>
        /// 玩家移动
        /// </summary>
        [Channel("KcpChannel")]
        ValueTask MoveAsync(MoveRequest request);

        /// <summary>
        /// 测试Ping方法（允许匿名访问）
        /// </summary>
        ValueTask<string> PingAsync(PingRequest request);
    }

    /// <summary>
    /// 事件接口 - 登录相关事件使用TCP
    /// </summary>
    [Channel("TcpChannel")]
    public interface IPlayerLoginEvents : IPulseEventHandler
    {
        // 下行通知: 玩家加入游戏
        void OnPlayerJoined(PlayerJoinedEvent eventData);

        // 下行通知: 玩家离开游戏
        void OnPlayerLeft(PlayerLeftEvent eventData);
    }

    /// <summary>
    /// 事件接口 - 位置更新使用KCP
    /// </summary>
    [Channel("KcpChannel")]
    public interface IPlayerMovementEvents : IPulseEventHandler
    {
        // 下行通知: 玩家移动
        void OnPlayerMoved(PlayerMovedEvent eventData);

        // 下行通知: 批量玩家移动
        void OnPlayersMovedBatch(PlayerMovedEvent[] eventData);

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
    public partial class PlayerJoinedEvent
    {
        public Guid PlayerId { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public Vector3 Position { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }

    [MemoryPackable]
    public partial class PlayerLeftEvent
    {
        public Guid PlayerId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    [MemoryPackable]
    public partial class PlayerMovedEvent
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
    public partial class PlayersBatchMovedEvent
    {
        public PlayerMovedEvent[]? Updates { get; set; }
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
