using MemoryPack;
using System;
using System.Collections.Generic;

namespace DistributedGameApp.Shared.Messages;

/// <summary>
/// 加入战斗请求
/// </summary>
[MemoryPackable]
public partial class JoinBattleRequest
{
    /// <summary>战斗ID</summary>
    public string BattleId { get; set; } = string.Empty;

    /// <summary>访问令牌</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>角色ID</summary>
    public string CharacterId { get; set; } = string.Empty;
}

/// <summary>
/// 战斗信息
/// </summary>
[MemoryPackable]
public partial class BattleInfo
{
    /// <summary>战斗ID</summary>
    public string BattleId { get; set; } = string.Empty;

    /// <summary>战斗状态</summary>
    public BattleStatus Status { get; set; }

    /// <summary>战斗模式</summary>
    public MatchMode Mode { get; set; }

    /// <summary>队伍1</summary>
    public List<BattlePlayer> Team1 { get; set; } = new();

    /// <summary>队伍2</summary>
    public List<BattlePlayer> Team2 { get; set; } = new();

    /// <summary>当前回合</summary>
    public int CurrentRound { get; set; }

    /// <summary>最大回合数</summary>
    public int MaxRounds { get; set; }

    /// <summary>战斗开始时间</summary>
    public DateTime StartTime { get; set; }

    /// <summary>战斗结束时间</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>队伍1分数</summary>
    public int Team1Score { get; set; }

    /// <summary>队伍2分数</summary>
    public int Team2Score { get; set; }
}

/// <summary>
/// 战斗玩家信息
/// </summary>
[MemoryPackable]
public partial class BattlePlayer
{
    /// <summary>角色ID</summary>
    public string CharacterId { get; set; } = string.Empty;

    /// <summary>角色名称</summary>
    public string CharacterName { get; set; } = string.Empty;

    /// <summary>职业</summary>
    public CharacterClass Class { get; set; }

    /// <summary>等级</summary>
    public int Level { get; set; }

    /// <summary>当前生命值</summary>
    public int CurrentHp { get; set; }

    /// <summary>最大生命值</summary>
    public int MaxHp { get; set; }

    /// <summary>当前魔法值</summary>
    public int CurrentMp { get; set; }

    /// <summary>最大魔法值</summary>
    public int MaxMp { get; set; }

    /// <summary>攻击力</summary>
    public int Attack { get; set; }

    /// <summary>防御力</summary>
    public int Defense { get; set; }

    /// <summary>速度</summary>
    public int Speed { get; set; }

    /// <summary>位置</summary>
    public BattlePosition Position { get; set; } = new();

    /// <summary>是否存活</summary>
    public bool IsAlive { get; set; }

    /// <summary>状态效果</summary>
    public List<StatusEffect> StatusEffects { get; set; } = new();

    /// <summary>击杀数</summary>
    public int Kills { get; set; }

    /// <summary>死亡数</summary>
    public int Deaths { get; set; }

    /// <summary>助攻数</summary>
    public int Assists { get; set; }
}

/// <summary>
/// 战斗位置
/// </summary>
[MemoryPackable]
public partial class BattlePosition
{
    public int X { get; set; }
    public int Y { get; set; }
}

/// <summary>
/// 状态效果
/// </summary>
[MemoryPackable]
public partial class StatusEffect
{
    /// <summary>效果类型</summary>
    public StatusEffectType Type { get; set; }

    /// <summary>剩余回合数</summary>
    public int RemainingRounds { get; set; }

    /// <summary>效果强度</summary>
    public int Intensity { get; set; }
}

/// <summary>
/// 战斗动作
/// </summary>
[MemoryPackable]
public partial class BattleAction
{
    /// <summary>动作ID</summary>
    public string ActionId { get; set; } = string.Empty;

    /// <summary>角色ID</summary>
    public string CharacterId { get; set; } = string.Empty;

    /// <summary>动作类型</summary>
    public ActionType Type { get; set; }

    /// <summary>目标角色IDs</summary>
    public List<string> TargetIds { get; set; } = new();

    /// <summary>技能ID（如果是技能动作）</summary>
    public string? SkillId { get; set; }

    /// <summary>移动目标位置（如果是移动动作）</summary>
    public BattlePosition? TargetPosition { get; set; }

    /// <summary>时间戳</summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 战斗动作结果
/// </summary>
[MemoryPackable]
public partial class BattleActionResult
{
    /// <summary>动作ID</summary>
    public string ActionId { get; set; } = string.Empty;

    /// <summary>是否成功</summary>
    public bool Success { get; set; }

    /// <summary>伤害记录</summary>
    public List<DamageRecord> DamageRecords { get; set; } = new();

    /// <summary>治疗记录</summary>
    public List<HealRecord> HealRecords { get; set; } = new();

    /// <summary>状态变化</summary>
    public List<StatusChange> StatusChanges { get; set; } = new();

    /// <summary>错误消息</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 伤害记录
/// </summary>
[MemoryPackable]
public partial class DamageRecord
{
    /// <summary>攻击者ID</summary>
    public string AttackerId { get; set; } = string.Empty;

    /// <summary>目标ID</summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>伤害值</summary>
    public int Damage { get; set; }

    /// <summary>是否暴击</summary>
    public bool IsCritical { get; set; }

    /// <summary>伤害类型</summary>
    public DamageType Type { get; set; }
}

/// <summary>
/// 治疗记录
/// </summary>
[MemoryPackable]
public partial class HealRecord
{
    /// <summary>治疗者ID</summary>
    public string HealerId { get; set; } = string.Empty;

    /// <summary>目标ID</summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>治疗量</summary>
    public int Amount { get; set; }
}

/// <summary>
/// 状态变化
/// </summary>
[MemoryPackable]
public partial class StatusChange
{
    /// <summary>目标ID</summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>生命值变化</summary>
    public int HpChange { get; set; }

    /// <summary>魔法值变化</summary>
    public int MpChange { get; set; }

    /// <summary>新增状态效果</summary>
    public List<StatusEffect> AddedEffects { get; set; } = new();

    /// <summary>移除的状态效果</summary>
    public List<StatusEffectType> RemovedEffects { get; set; } = new();

    /// <summary>是否死亡</summary>
    public bool IsDead { get; set; }
}

/// <summary>
/// 战斗结果
/// </summary>
[MemoryPackable]
public partial class BattleResult
{
    /// <summary>战斗ID</summary>
    public string BattleId { get; set; } = string.Empty;

    /// <summary>获胜队伍（1或2）</summary>
    public int WinnerTeam { get; set; }

    /// <summary>队伍1统计</summary>
    public TeamStats Team1Stats { get; set; } = new();

    /// <summary>队伍2统计</summary>
    public TeamStats Team2Stats { get; set; } = new();

    /// <summary>玩家奖励</summary>
    public List<PlayerReward> Rewards { get; set; } = new();

    /// <summary>战斗持续时间（秒）</summary>
    public int Duration { get; set; }
}

/// <summary>
/// 队伍统计
/// </summary>
[MemoryPackable]
public partial class TeamStats
{
    /// <summary>总击杀数</summary>
    public int TotalKills { get; set; }

    /// <summary>总死亡数</summary>
    public int TotalDeaths { get; set; }

    /// <summary>总助攻数</summary>
    public int TotalAssists { get; set; }

    /// <summary>总伤害</summary>
    public long TotalDamage { get; set; }

    /// <summary>总治疗</summary>
    public long TotalHealing { get; set; }
}

/// <summary>
/// 玩家奖励
/// </summary>
[MemoryPackable]
public partial class PlayerReward
{
    /// <summary>角色ID</summary>
    public string CharacterId { get; set; } = string.Empty;

    /// <summary>经验值奖励</summary>
    public long ExpReward { get; set; }

    /// <summary>金币奖励</summary>
    public int GoldReward { get; set; }

    /// <summary>胜利</summary>
    public bool IsWinner { get; set; }

    /// <summary>MVP</summary>
    public bool IsMvp { get; set; }
}

/// <summary>
/// 战斗状态
/// </summary>
public enum BattleStatus
{
    /// <summary>等待中</summary>
    Waiting = 0,
    /// <summary>准备中</summary>
    Preparing = 1,
    /// <summary>进行中</summary>
    InProgress = 2,
    /// <summary>已结束</summary>
    Finished = 3,
    /// <summary>已取消</summary>
    Cancelled = 4
}

/// <summary>
/// 动作类型
/// </summary>
public enum ActionType
{
    /// <summary>普通攻击</summary>
    Attack = 1,
    /// <summary>使用技能</summary>
    Skill = 2,
    /// <summary>移动</summary>
    Move = 3,
    /// <summary>防御</summary>
    Defend = 4,
    /// <summary>使用道具</summary>
    UseItem = 5
}

/// <summary>
/// 伤害类型
/// </summary>
public enum DamageType
{
    /// <summary>物理伤害</summary>
    Physical = 1,
    /// <summary>魔法伤害</summary>
    Magical = 2,
    /// <summary>真实伤害</summary>
    True = 3
}

/// <summary>
/// 状态效果类型
/// </summary>
public enum StatusEffectType
{
    /// <summary>中毒</summary>
    Poison = 1,
    /// <summary>灼烧</summary>
    Burn = 2,
    /// <summary>冰冻</summary>
    Frozen = 3,
    /// <summary>眩晕</summary>
    Stunned = 4,
    /// <summary>沉默</summary>
    Silenced = 5,
    /// <summary>攻击增强</summary>
    AttackBuff = 6,
    /// <summary>防御增强</summary>
    DefenseBuff = 7,
    /// <summary>速度增强</summary>
    SpeedBuff = 8,
    /// <summary>再生</summary>
    Regeneration = 9,
    /// <summary>护盾</summary>
    Shield = 10
}
