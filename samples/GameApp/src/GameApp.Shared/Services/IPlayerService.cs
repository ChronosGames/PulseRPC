using System;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC;

namespace GameApp.Shared.Services
{
    /// <summary>
    /// 玩家服务接口 - 处理玩家登录、信息查询等核心功能
    /// </summary>
    [Channel("TcpChannel")]
    public interface IPlayerService : IPulseService
    {
        /// <summary>
        /// 玩家登录游戏服务器
        /// </summary>
        Task<LoginResponse> LoginAsync(LoginRequest request);

        /// <summary>
        /// 获取玩家信息
        /// </summary>
        Task<PlayerInfo> GetPlayerInfoAsync(GetPlayerInfoRequest request);

        /// <summary>
        /// 更新玩家位置 - 使用 KCP 通道进行低延迟传输
        /// </summary>
        [Channel("KcpChannel")]
        Task UpdatePositionAsync(UpdatePositionRequest request);

        /// <summary>
        /// 玩家登出
        /// </summary>
        Task LogoutAsync(LogoutRequest request);

        /// <summary>
        /// 获取玩家统计信息
        /// </summary>
        Task<PlayerStatistics> GetStatisticsAsync(GetStatisticsRequest request);
    }

    /// <summary>
    /// 玩家事件监听器 - 接收服务器推送的玩家相关事件
    /// </summary>
    [Channel("TcpChannel")]
    public interface IPlayerEvents : IPulseEventHandler
    {
        /// <summary>
        /// 玩家状态更新事件
        /// </summary>
        void OnPlayerStatusUpdate(PlayerStatusUpdateEvent eventData);

        /// <summary>
        /// 玩家等级提升事件
        /// </summary>
        void OnPlayerLevelUp(PlayerLevelUpEvent eventData);

        /// <summary>
        /// 玩家位置更新事件 - 使用 KCP 通道
        /// </summary>
        [Channel("KcpChannel")]
        void OnPlayerMoved(PlayerMovedEvent eventData);
    }

// === 数据传输对象 ===

    [MemoryPackable]
    public partial class LoginRequest
    {
        public string GameTicket { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public ClientInfo ClientInfo { get; set; } = new();
    }

    [MemoryPackable]
    public partial class LoginResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public PlayerInfo? PlayerInfo { get; set; }
        public WorldInfo? WorldInfo { get; set; }
        public ServerInfo? ServerInfo { get; set; }
    }

    [MemoryPackable]
    public partial class GetPlayerInfoRequest
    {
        public int PlayerId { get; set; }
    }

    [MemoryPackable]
    public partial class PlayerInfo
    {
        public int PlayerId { get; set; }
        public int UserId { get; set; }
        public string CharacterName { get; set; } = string.Empty;
        public string Class { get; set; } = string.Empty;
        public int Level { get; set; }
        public long Experience { get; set; }
        public PlayerAttributes Attributes { get; set; } = new();
        public PlayerStatus Status { get; set; } = new();
        public PlayerPosition Position { get; set; } = new();
    }

    [MemoryPackable]
    public partial class PlayerAttributes
    {
        public int Strength { get; set; }
        public int Agility { get; set; }
        public int Intelligence { get; set; }
        public int Constitution { get; set; }
        public int Luck { get; set; }
    }

    [MemoryPackable]
    public partial class PlayerStatus
    {
        public int Health { get; set; }
        public int MaxHealth { get; set; }
        public int Mana { get; set; }
        public int MaxMana { get; set; }
        public int Stamina { get; set; }
        public int MaxStamina { get; set; }
    }

    [MemoryPackable]
    public partial class PlayerPosition
    {
        public string WorldId { get; set; } = string.Empty;
        public string MapId { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Rotation { get; set; }
        public DateTime LastUpdate { get; set; }
    }

    [MemoryPackable]
    public partial class UpdatePositionRequest
    {
        public int PlayerId { get; set; }
        public PlayerPosition Position { get; set; } = new();
    }

    [MemoryPackable]
    public partial class LogoutRequest
    {
        public int PlayerId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    [MemoryPackable]
    public partial class GetStatisticsRequest
    {
        public int PlayerId { get; set; }
    }

    [MemoryPackable]
    public partial class PlayerStatistics
    {
        public int PlayerId { get; set; }
        public long TotalPlayTime { get; set; }
        public int MonstersKilled { get; set; }
        public int QuestsCompleted { get; set; }
        public int DeathCount { get; set; }
        public int LoginDays { get; set; }
        public DateTime LastActiveTime { get; set; }
    }

    [MemoryPackable]
    public partial class ClientInfo
    {
        public string Version { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string UnityVersion { get; set; } = string.Empty;
    }

    [MemoryPackable]
    public partial class WorldInfo
    {
        public string WorldId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int CurrentPlayers { get; set; }
        public int MaxPlayers { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    [MemoryPackable]
    public partial class ServerInfo
    {
        public string ServerId { get; set; } = string.Empty;
        public string ServerName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public DateTime ServerTime { get; set; }
    }

// === 事件数据对象 ===

    [MemoryPackable]
    public partial class PlayerStatusUpdateEvent
    {
        public int PlayerId { get; set; }
        public PlayerStatus Status { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }

    [MemoryPackable]
    public partial class PlayerLevelUpEvent
    {
        public int PlayerId { get; set; }
        public int OldLevel { get; set; }
        public int NewLevel { get; set; }
        public long Experience { get; set; }
        public DateTime Timestamp { get; set; }
    }

    [MemoryPackable]
    public partial class PlayerMovedEvent
    {
        public int PlayerId { get; set; }
        public PlayerPosition Position { get; set; } = new();
        public float Speed { get; set; }
        public bool IsRunning { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
