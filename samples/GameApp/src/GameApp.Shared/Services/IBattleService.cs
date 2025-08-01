using MemoryPack;
using PulseRPC;

namespace GameApp.Shared.Services;

/// <summary>
/// 战斗服务接口
/// </summary>
[Channel("KcpChannel")]
public interface IBattleService : IPulseService
{
    /// <summary>
    /// 加入战斗
    /// </summary>
    Task<JoinBattleResponse> JoinBattleAsync(JoinBattleRequest request);

    /// <summary>
    /// 离开战斗
    /// </summary>
    Task LeaveBattleAsync(LeaveBattleRequest request);

    /// <summary>
    /// 使用技能
    /// </summary>
    Task<SkillResult> UseSkillAsync(UseSkillRequest request);

    /// <summary>
    /// 移动到位置
    /// </summary>
    Task MoveBattlePositionAsync(MoveBattlePositionRequest request);

    /// <summary>
    /// 获取战斗状态
    /// </summary>
    [Channel("TcpChannel")]
    Task<BattleInfo> GetBattleInfoAsync(GetBattleInfoRequest request);
}

/// <summary>
/// 技能服务接口
/// </summary>
[Channel("KcpChannel")]
public interface ISkillService : IPulseService
{
    /// <summary>
    /// 学习技能
    /// </summary>
    [Channel("TcpChannel")]
    Task<LearnSkillResponse> LearnSkillAsync(LearnSkillRequest request);

    /// <summary>
    /// 升级技能
    /// </summary>
    [Channel("TcpChannel")]
    Task<UpgradeSkillResponse> UpgradeSkillAsync(UpgradeSkillRequest request);

    /// <summary>
    /// 获取技能列表
    /// </summary>
    [Channel("TcpChannel")]
    Task<PlayerSkillsResponse> GetPlayerSkillsAsync(GetPlayerSkillsRequest request);
}

/// <summary>
/// 战斗事件监听器接口
/// </summary>
[Channel("KcpChannel")]
public interface IBattleEvents : IPulseEventHandler
{
    /// <summary>
    /// 战斗状态更新
    /// </summary>
    void OnBattleStateUpdate(BattleStateUpdateEvent eventData);

    /// <summary>
    /// 技能使用事件
    /// </summary>
    void OnSkillUsed(SkillUsedEvent eventData);

    /// <summary>
    /// 伤害事件
    /// </summary>
    void OnDamageDealt(DamageDealtEvent eventData);

    /// <summary>
    /// 玩家死亡事件
    /// </summary>
    void OnPlayerDefeated(PlayerDefeatedEvent eventData);

    /// <summary>
    /// 战斗结束事件
    /// </summary>
    void OnBattleEnded(BattleEndedEvent eventData);
}

#region Request/Response Models

[MemoryPackable]
public partial class JoinBattleRequest
{
    public int PlayerId { get; set; }
    public string BattleType { get; set; } = string.Empty; // "pvp", "pve", "raid"
    public string? RoomId { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
}

[MemoryPackable]
public partial class JoinBattleResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public BattleInfo? BattleInfo { get; set; }
    public List<BattlePlayer> Players { get; set; } = new();
}

[MemoryPackable]
public partial class LeaveBattleRequest
{
    public int PlayerId { get; set; }
    public string BattleId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class UseSkillRequest
{
    public int PlayerId { get; set; }
    public string BattleId { get; set; } = string.Empty;
    public int SkillId { get; set; }
    public BattlePosition TargetPosition { get; set; } = new();
    public List<int> TargetPlayerIds { get; set; } = new();
    public DateTime CastTime { get; set; }
}

[MemoryPackable]
public partial class SkillResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int SkillId { get; set; }
    public List<DamageInfo> DamageResults { get; set; } = new();
    public List<BuffEffect> BuffEffects { get; set; } = new();
    public float CooldownRemaining { get; set; }
}

[MemoryPackable]
public partial class MoveBattlePositionRequest
{
    public int PlayerId { get; set; }
    public string BattleId { get; set; } = string.Empty;
    public BattlePosition Position { get; set; } = new();
    public DateTime MoveTime { get; set; }
}

[MemoryPackable]
public partial class GetBattleInfoRequest
{
    public string BattleId { get; set; } = string.Empty;
    public int RequestingPlayerId { get; set; }
}

[MemoryPackable]
public partial class LearnSkillRequest
{
    public int PlayerId { get; set; }
    public int SkillId { get; set; }
}

[MemoryPackable]
public partial class LearnSkillResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public PlayerSkill? LearnedSkill { get; set; }
}

[MemoryPackable]
public partial class UpgradeSkillRequest
{
    public int PlayerId { get; set; }
    public int SkillId { get; set; }
}

[MemoryPackable]
public partial class UpgradeSkillResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public PlayerSkill? UpgradedSkill { get; set; }
}

[MemoryPackable]
public partial class GetPlayerSkillsRequest
{
    public int PlayerId { get; set; }
}

[MemoryPackable]
public partial class PlayerSkillsResponse
{
    public List<PlayerSkill> Skills { get; set; } = new();
    public int SkillPoints { get; set; }
}

#endregion

#region Data Models

[MemoryPackable]
public partial class BattleInfo
{
    public string BattleId { get; set; } = string.Empty;
    public string BattleType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // "waiting", "active", "ended"
    public List<BattlePlayer> Players { get; set; } = new();
    public BattleSettings Settings { get; set; } = new();
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int WinnerTeam { get; set; }
}

[MemoryPackable]
public partial class BattlePlayer
{
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int Team { get; set; }
    public BattlePosition Position { get; set; } = new();
    public BattlePlayerStatus Status { get; set; } = new();
    public List<PlayerSkill> Skills { get; set; } = new();
    public List<BuffEffect> ActiveBuffs { get; set; } = new();
}

[MemoryPackable]
public partial class BattlePosition
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Rotation { get; set; }
    public DateTime LastUpdate { get; set; }
}

[MemoryPackable]
public partial class BattlePlayerStatus
{
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public int Mana { get; set; }
    public int MaxMana { get; set; }
    public bool IsAlive { get; set; }
    public bool IsStunned { get; set; }
    public DateTime LastDamageTime { get; set; }
}

[MemoryPackable]
public partial class BattleSettings
{
    public int MaxPlayers { get; set; }
    public int TimeLimit { get; set; } // 秒
    public bool FriendlyFire { get; set; }
    public Dictionary<string, object> CustomSettings { get; set; } = new();
}

[MemoryPackable]
public partial class PlayerSkill
{
    public int SkillId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Level { get; set; }
    public int MaxLevel { get; set; }
    public SkillType Type { get; set; }
    public int ManaCost { get; set; }
    public float CooldownSeconds { get; set; }
    public float Range { get; set; }
    public SkillEffects Effects { get; set; } = new();
}

[MemoryPackable]
public partial class SkillEffects
{
    public int BaseDamage { get; set; }
    public int Healing { get; set; }
    public List<BuffEffect> Buffs { get; set; } = new();
    public float AreaOfEffect { get; set; }
    public int MaxTargets { get; set; }
}

[MemoryPackable]
public partial class BuffEffect
{
    public int BuffId { get; set; }
    public string Name { get; set; } = string.Empty;
    public BuffType Type { get; set; }
    public int Value { get; set; }
    public float Duration { get; set; }
    public DateTime StartTime { get; set; }
    public bool IsStackable { get; set; }
    public int StackCount { get; set; }
}

[MemoryPackable]
public partial class DamageInfo
{
    public int TargetPlayerId { get; set; }
    public int Damage { get; set; }
    public DamageType Type { get; set; }
    public bool IsCritical { get; set; }
    public bool IsBlocked { get; set; }
    public bool IsDodged { get; set; }
}

public enum SkillType
{
    Attack,
    Heal,
    Buff,
    Debuff,
    Movement,
    Area
}

public enum BuffType
{
    HealthRegeneration,
    ManaRegeneration,
    AttackPowerIncrease,
    DefenseIncrease,
    SpeedIncrease,
    Stun,
    Poison,
    Burn,
    Freeze
}

public enum DamageType
{
    Physical,
    Magical,
    True,
    Healing
}

#endregion

#region Event Models

[MemoryPackable]
public partial class BattleStateUpdateEvent
{
    public string BattleId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<BattlePlayer> Players { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

[MemoryPackable]
public partial class SkillUsedEvent
{
    public string BattleId { get; set; } = string.Empty;
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public PlayerSkill Skill { get; set; } = new();
    public BattlePosition CastPosition { get; set; } = new();
    public BattlePosition TargetPosition { get; set; } = new();
    public List<int> TargetPlayerIds { get; set; } = new();
    public DateTime CastTime { get; set; }
}

[MemoryPackable]
public partial class DamageDealtEvent
{
    public string BattleId { get; set; } = string.Empty;
    public int SourcePlayerId { get; set; }
    public string SourcePlayerName { get; set; } = string.Empty;
    public List<DamageInfo> DamageResults { get; set; } = new();
    public int? SkillId { get; set; }
    public DateTime Timestamp { get; set; }
}

[MemoryPackable]
public partial class PlayerDefeatedEvent
{
    public string BattleId { get; set; } = string.Empty;
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int KillerPlayerId { get; set; }
    public string KillerPlayerName { get; set; } = string.Empty;
    public DateTime DeathTime { get; set; }
}

[MemoryPackable]
public partial class BattleEndedEvent
{
    public string BattleId { get; set; } = string.Empty;
    public int WinnerTeam { get; set; }
    public List<int> WinnerPlayerIds { get; set; } = new();
    public string EndReason { get; set; } = string.Empty; // "victory", "timeout", "forfeit"
    public BattleStatistics Statistics { get; set; } = new();
    public DateTime EndTime { get; set; }
}

[MemoryPackable]
public partial class BattleStatistics
{
    public string BattleId { get; set; } = string.Empty;
    public int Duration { get; set; } // 秒
    public Dictionary<int, PlayerBattleStats> PlayerStats { get; set; } = new();
}

[MemoryPackable]
public partial class PlayerBattleStats
{
    public int PlayerId { get; set; }
    public int DamageDealt { get; set; }
    public int DamageTaken { get; set; }
    public int HealingDone { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int SkillsUsed { get; set; }
}

#endregion
