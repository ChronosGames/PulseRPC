using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using MemoryPack;
using PulseRPC;

namespace GameApp.Shared.Services;

/// <summary>
/// 世界服务接口 - 处理游戏世界管理、玩家进出世界等功能
/// </summary>
[Channel("TcpChannel")]
public interface IWorldService : IPulseService
{
    /// <summary>
    /// 加入世界
    /// </summary>
    ValueTask<JoinWorldResponse> JoinWorldAsync(JoinWorldRequest request);

    /// <summary>
    /// 离开世界
    /// </summary>
    ValueTask LeaveWorldAsync(LeaveWorldRequest request);

    /// <summary>
    /// 获取世界状态
    /// </summary>
    ValueTask<WorldState> GetWorldStateAsync(GetWorldStateRequest request);

    /// <summary>
    /// 世界聊天
    /// </summary>
    ValueTask SendWorldChatAsync(WorldChatRequest request);

    /// <summary>
    /// 获取附近玩家
    /// </summary>
    ValueTask<NearbyPlayersResponse> GetNearbyPlayersAsync(NearbyPlayersRequest request);
}

/// <summary>
/// 世界事件推送接口 - 接收服务器推送的世界相关事件
/// </summary>
[Channel("TcpChannel")]
public interface IWorldEvents : IPulseEventHandler
{
    /// <summary>
    /// 世界状态更新
    /// </summary>
    void OnWorldUpdate(WorldUpdateEvent eventData);

    /// <summary>
    /// 玩家加入世界
    /// </summary>
    void OnPlayerJoined(PlayerJoinedEvent eventData);

    /// <summary>
    /// 玩家离开世界
    /// </summary>
    void OnPlayerLeft(PlayerLeftEvent eventData);

    /// <summary>
    /// 世界聊天消息
    /// </summary>
    void OnWorldChatMessage(WorldChatMessageEvent eventData);

    /// <summary>
    /// 世界事件通知
    /// </summary>
    void OnWorldEventNotification(WorldEventNotificationEvent eventData);
}

// === 数据传输对象 ===

[MemoryPackable]
public partial class JoinWorldRequest
{
    public int PlayerId { get; set; }
    public string WorldId { get; set; } = string.Empty;
    public PlayerPosition SpawnPosition { get; set; } = new();
}

[MemoryPackable]
public partial class JoinWorldResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public WorldState? WorldState { get; set; }
    public List<PlayerInfo> NearbyPlayers { get; set; } = new();
}

[MemoryPackable]
public partial class LeaveWorldRequest
{
    public int PlayerId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class GetWorldStateRequest
{
    public string WorldId { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class WorldState
{
    public string WorldId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int CurrentPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<WorldEvent> ActiveEvents { get; set; } = new();
    public WeatherInfo Weather { get; set; } = new();
    public Dictionary<string, object> Properties { get; set; } = new();
}

[MemoryPackable]
public partial class WorldChatRequest
{
    public int PlayerId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ChatType { get; set; } = "world"; // world, zone, guild
    public Dictionary<string, object>? Metadata { get; set; }
}

[MemoryPackable]
public partial class NearbyPlayersRequest
{
    public int PlayerId { get; set; }
    public PlayerPosition Position { get; set; } = new();
    public float Radius { get; set; } = 100.0f;
}

[MemoryPackable]
public partial class NearbyPlayersResponse
{
    public List<PlayerInfo> Players { get; set; } = new();
    public int TotalCount { get; set; }
}

[MemoryPackable]
public partial class WorldEvent
{
    public string EventId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
}

[MemoryPackable]
public partial class WeatherInfo
{
    public string Current { get; set; } = "sunny";
    public int Temperature { get; set; }
    public int Humidity { get; set; }
    public DateTime NextChange { get; set; }
}

// === 事件数据对象 ===

[MemoryPackable]
public partial class WorldUpdateEvent
{
    public string WorldId { get; set; } = string.Empty;
    public string UpdateType { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

[MemoryPackable]
public partial class PlayerJoinedEvent
{
    public string WorldId { get; set; } = string.Empty;
    public PlayerInfo Player { get; set; } = new();
    public PlayerPosition Position { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

[MemoryPackable]
public partial class PlayerLeftEvent
{
    public string WorldId { get; set; } = string.Empty;
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

[MemoryPackable]
public partial class WorldChatMessageEvent
{
    public string WorldId { get; set; } = string.Empty;
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ChatType { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

[MemoryPackable]
public partial class WorldEventNotificationEvent
{
    public string WorldId { get; set; } = string.Empty;
    public WorldEvent Event { get; set; } = new();
    public string NotificationType { get; set; } = string.Empty; // started, ended, updated
    public DateTime Timestamp { get; set; }
}
