using System;
using System.Collections.Generic;
using MemoryPack;

namespace DistributedGameApp.Shared.Domain.Battles;

/// <summary>
/// 战斗房间
/// </summary>
[MemoryPackable]
public partial class BattleRoom
{
    /// <summary>
    /// 房间ID
    /// </summary>
    public string RoomId { get; set; } = string.Empty;

    /// <summary>
    /// 房间名称
    /// </summary>
    public string RoomName { get; set; } = string.Empty;

    /// <summary>
    /// 战斗类型（PvP, PvE, Team）
    /// </summary>
    public string BattleType { get; set; } = string.Empty;

    /// <summary>
    /// 房间状态（Waiting, InBattle, Finished）
    /// </summary>
    public BattleRoomStatus Status { get; set; } = BattleRoomStatus.Waiting;

    /// <summary>
    /// 最大玩家数
    /// </summary>
    public int MaxPlayers { get; set; } = 2;

    /// <summary>
    /// 当前玩家列表
    /// </summary>
    public List<BattlePlayer> Players { get; set; } = new();

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 战斗开始时间
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// 战斗结束时间
    /// </summary>
    public DateTime? FinishedAt { get; set; }
}

/// <summary>
/// 战斗房间状态
/// </summary>
[MemoryPackable]
public partial class BattleRoomStatus
{
    public static readonly BattleRoomStatus Waiting = new() { Code = 0, Name = "Waiting" };
    public static readonly BattleRoomStatus InBattle = new() { Code = 1, Name = "InBattle" };
    public static readonly BattleRoomStatus Finished = new() { Code = 2, Name = "Finished" };

    public int Code { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 战斗玩家
/// </summary>
[MemoryPackable]
public partial class BattlePlayer
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
    /// 角色名称
    /// </summary>
    public string CharacterName { get; set; } = string.Empty;

    /// <summary>
    /// 队伍ID（1 或 2）
    /// </summary>
    public int TeamId { get; set; }

    /// <summary>
    /// 当前生命值
    /// </summary>
    public int CurrentHp { get; set; }

    /// <summary>
    /// 最大生命值
    /// </summary>
    public int MaxHp { get; set; }

    /// <summary>
    /// 当前魔法值
    /// </summary>
    public int CurrentMp { get; set; }

    /// <summary>
    /// 最大魔法值
    /// </summary>
    public int MaxMp { get; set; }

    /// <summary>
    /// 位置
    /// </summary>
    public Vector3 Position { get; set; } = new();

    /// <summary>
    /// 旋转
    /// </summary>
    public Vector3 Rotation { get; set; } = new();

    /// <summary>
    /// 是否已准备
    /// </summary>
    public bool IsReady { get; set; }

    /// <summary>
    /// 是否存活
    /// </summary>
    public bool IsAlive { get; set; } = true;
}

/// <summary>
/// 三维向量
/// </summary>
[MemoryPackable]
public partial class Vector3
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

/// <summary>
/// 战斗动作
/// </summary>
[MemoryPackable]
public partial class BattleAction
{
    /// <summary>
    /// 动作ID
    /// </summary>
    public string ActionId { get; set; } = string.Empty;

    /// <summary>
    /// 动作类型（Move, Attack, Skill, UseItem）
    /// </summary>
    public string ActionType { get; set; } = string.Empty;

    /// <summary>
    /// 玩家ID
    /// </summary>
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>
    /// 时间戳
    /// </summary>
    public long Timestamp { get; set; }

    /// <summary>
    /// 动作数据（JSON）
    /// </summary>
    public string Data { get; set; } = string.Empty;
}

/// <summary>
/// 战斗结果
/// </summary>
[MemoryPackable]
public partial class BattleResult
{
    /// <summary>
    /// 房间ID
    /// </summary>
    public string RoomId { get; set; } = string.Empty;

    /// <summary>
    /// 胜利队伍ID
    /// </summary>
    public int WinnerTeamId { get; set; }

    /// <summary>
    /// 战斗持续时间（秒）
    /// </summary>
    public int DurationSeconds { get; set; }

    /// <summary>
    /// 玩家结果列表
    /// </summary>
    public List<PlayerBattleResult> PlayerResults { get; set; } = new();
}

/// <summary>
/// 玩家战斗结果
/// </summary>
[MemoryPackable]
public partial class PlayerBattleResult
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
    /// 队伍ID
    /// </summary>
    public int TeamId { get; set; }

    /// <summary>
    /// 是否胜利
    /// </summary>
    public bool IsWinner { get; set; }

    /// <summary>
    /// 造成的伤害
    /// </summary>
    public long DamageDealt { get; set; }

    /// <summary>
    /// 受到的伤害
    /// </summary>
    public long DamageTaken { get; set; }

    /// <summary>
    /// 击杀数
    /// </summary>
    public int Kills { get; set; }

    /// <summary>
    /// 死亡数
    /// </summary>
    public int Deaths { get; set; }

    /// <summary>
    /// 获得经验
    /// </summary>
    public long ExpGained { get; set; }

    /// <summary>
    /// 获得金币
    /// </summary>
    public long GoldGained { get; set; }
}
