using System.Collections.Generic;
using MemoryPack;

namespace DistributedGameApp.Shared.Domain.Matchmaking;

/// <summary>
/// 匹配请求
/// </summary>
[MemoryPackable]
public partial class MatchmakingRequest
{
    /// <summary>
    /// 玩家ID
    /// </summary>
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>
    /// 角色ID
    /// </summary>
    public string CharacterId { get; set; } = string.Empty;

    /// <summary>
    /// 匹配类型（1v1, 2v2, 3v3, 5v5）
    /// </summary>
    public string MatchType { get; set; } = string.Empty;

    /// <summary>
    /// 等级范围（±）
    /// </summary>
    public int LevelRange { get; set; } = 5;
}

/// <summary>
/// 匹配响应
/// </summary>
[MemoryPackable]
public partial class MatchmakingResponse
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 消息
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 匹配ID
    /// </summary>
    public string MatchId { get; set; } = string.Empty;

    /// <summary>
    /// 预计等待时间（秒）
    /// </summary>
    public int EstimatedWaitSeconds { get; set; }
}

/// <summary>
/// 匹配成功通知
/// </summary>
[MemoryPackable]
public partial class MatchFoundNotification
{
    /// <summary>
    /// 匹配ID
    /// </summary>
    public string MatchId { get; set; } = string.Empty;

    /// <summary>
    /// 战斗房间ID
    /// </summary>
    public string BattleRoomId { get; set; } = string.Empty;

    /// <summary>
    /// 战斗服务器地址
    /// </summary>
    public string BattleServerHost { get; set; } = string.Empty;

    /// <summary>
    /// 战斗服务器端口
    /// </summary>
    public int BattleServerPort { get; set; }

    /// <summary>
    /// 队友列表
    /// </summary>
    public List<MatchPlayer> Teammates { get; set; } = new();

    /// <summary>
    /// 对手列表
    /// </summary>
    public List<MatchPlayer> Opponents { get; set; } = new();
}

/// <summary>
/// 匹配玩家信息
/// </summary>
[MemoryPackable]
public partial class MatchPlayer
{
    /// <summary>
    /// 玩家ID
    /// </summary>
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>
    /// 角色名称
    /// </summary>
    public string CharacterName { get; set; } = string.Empty;

    /// <summary>
    /// 等级
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// 职业
    /// </summary>
    public string Class { get; set; } = string.Empty;
}
