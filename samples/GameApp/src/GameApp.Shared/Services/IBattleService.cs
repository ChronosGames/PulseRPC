using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using MemoryPack;
using PulseRPC;

namespace GameApp.Shared.Services;

/// <summary>
/// 战斗服务接口 - 处理实时战斗逻辑，使用 KCP 通道保证低延迟
/// </summary>
[Channel("KcpChannel")]
public interface IBattleService : IPulseService
{
    /// <summary>
    /// 加入战斗
    /// </summary>
    ValueTask<JoinBattleResponse> JoinBattleAsync(JoinBattleRequest request);

    /// <summary>
    /// 离开战斗
    /// </summary>
    ValueTask LeaveBattleAsync(LeaveBattleRequest request);

    /// <summary>
    /// 使用技能
    /// </summary>
    ValueTask<SkillResult> UseSkillAsync(UseSkillRequest request);

    /// <summary>
    /// 获取战斗状态 - 使用 TCP 通道获取完整状态信息
    /// </summary>
    [Channel("TcpChannel")]
    ValueTask<BattleState> GetBattleStateAsync(GetBattleStateRequest request);
}

/// <summary>
/// 战斗事件推送接口 - 接收服务器推送的战斗相关事件，使用 KCP 通道
/// </summary>
[Channel("KcpChannel")]
public interface IBattleEvents : IPulseEventHandler
{
    /// <summary>
    /// 战斗状态更新
    /// </summary>
    void OnBattleStateUpdate(BattleStateUpdateEvent eventData);

    /// <summary>
    /// 技能释放事件
    /// </summary>
    void OnSkillUsed(SkillUsedEvent eventData);

    /// <summary>
    /// 伤害事件
    /// </summary>
    void OnDamageDealt(DamageDealtEvent eventData);

    /// <summary>
    /// 玩家战败事件
    /// </summary>
    void OnPlayerDefeated(PlayerDefeatedEvent eventData);

    /// <summary>
    /// 战斗结束事件
    /// </summary>
    void OnBattleEnded(BattleEndedEvent eventData);
}

/// <summary>
/// 技能服务接口 - 处理技能学习、升级等功能
/// </summary>
[Channel("TcpChannel")]
public interface ISkillService : IPulseService
{
    /// <summary>
    /// 学习技能
    /// </summary>
    ValueTask<LearnSkillResponse> LearnSkillAsync(LearnSkillRequest request);

    /// <summary>
    /// 升级技能
    /// </summary>
    ValueTask<UpgradeSkillResponse> UpgradeSkillAsync(UpgradeSkillRequest request);

    /// <summary>
    /// 获取技能列表
    /// </summary>
    ValueTask<SkillListResponse> GetSkillListAsync(GetSkillListRequest request);

    /// <summary>
    /// 重置技能点
    /// </summary>
    ValueTask<ResetSkillsResponse> ResetSkillsAsync(ResetSkillsRequest request);
}

// === 战斗服务数据传输对象 ===

[MemoryPackable]
public partial class JoinBattleRequest
{
    public int PlayerId { get; set; }
    public string BattleType { get; set; } = string.Empty; // pvp, pve, guild_war
    public Dictionary<string, object> Parameters { get; set; } = new();
}

[MemoryPackable]
public partial class JoinBattleResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string BattleId { get; set; } = string.Empty;
    public BattleState? BattleState { get; set; }
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
    public string SkillId { get; set; } = string.Empty;
    public int TargetId { get; set; }
    public BattlePosition Position { get; set; } = new();
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object>? Parameters { get; set; }
}

[MemoryPackable]
public partial class GetBattleStateRequest
{
    public string BattleId { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class BattleState
{
    public string BattleId { get; set; } = string.Empty;
    public string BattleType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // waiting, active, completed
    public List<BattleCombatant> Participants { get; set; } = new();
    public int CurrentRound { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
}

[MemoryPackable]
public partial class BattleCombatant
{
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; // tank, dps, healer
    public BattleStatus Status { get; set; } = new();
    public BattlePosition Position { get; set; } = new();
    public DateTime JoinTime { get; set; }
    public DateTime? LeftTime { get; set; }
}

[MemoryPackable]
public partial class BattleStatus
{
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public int Mana { get; set; }
    public int MaxMana { get; set; }
    public List<string> Buffs { get; set; } = new();
    public List<string> Debuffs { get; set; } = new();
}

[MemoryPackable]
public partial class BattlePosition
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Rotation { get; set; }
}

[MemoryPackable]
public partial class SkillResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string SkillId { get; set; } = string.Empty;
    public int Damage { get; set; }
    public List<string> Effects { get; set; } = new();
    public Dictionary<string, object> Properties { get; set; } = new();
}

// === 技能服务数据传输对象 ===

[MemoryPackable]
public partial class LearnSkillRequest
{
    public int PlayerId { get; set; }
    public string SkillId { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class LearnSkillResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public SkillInfo? SkillInfo { get; set; }
}

[MemoryPackable]
public partial class UpgradeSkillRequest
{
    public int PlayerId { get; set; }
    public string SkillId { get; set; } = string.Empty;
    public int TargetLevel { get; set; }
}

[MemoryPackable]
public partial class UpgradeSkillResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public SkillInfo? SkillInfo { get; set; }
    public int CostGold { get; set; }
}

[MemoryPackable]
public partial class GetSkillListRequest
{
    public int PlayerId { get; set; }
}

[MemoryPackable]
public partial class SkillListResponse
{
    public List<SkillInfo> Skills { get; set; } = new();
    public int AvailableSkillPoints { get; set; }
}

[MemoryPackable]
public partial class ResetSkillsRequest
{
    public int PlayerId { get; set; }
}

[MemoryPackable]
public partial class ResetSkillsResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int RefundedSkillPoints { get; set; }
    public int CostGold { get; set; }
}

[MemoryPackable]
public partial class SkillInfo
{
    public string SkillId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Level { get; set; }
    public int MaxLevel { get; set; }
    public long Experience { get; set; }
    public long ExperienceToNext { get; set; }
    public DateTime LastUsed { get; set; }
    public Dictionary<string, object> Attributes { get; set; } = new();
}

// === 战斗事件数据对象 ===

[MemoryPackable]
public partial class BattleStateUpdateEvent
{
    public string BattleId { get; set; } = string.Empty;
    public string UpdateType { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

[MemoryPackable]
public partial class SkillUsedEvent
{
    public string BattleId { get; set; } = string.Empty;
    public int PlayerId { get; set; }
    public string SkillId { get; set; } = string.Empty;
    public int TargetId { get; set; }
    public BattlePosition Position { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

[MemoryPackable]
public partial class DamageDealtEvent
{
    public string BattleId { get; set; } = string.Empty;
    public int SourcePlayerId { get; set; }
    public int TargetPlayerId { get; set; }
    public int Damage { get; set; }
    public string DamageType { get; set; } = string.Empty;
    public bool IsCritical { get; set; }
    public List<string> Effects { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

[MemoryPackable]
public partial class PlayerDefeatedEvent
{
    public string BattleId { get; set; } = string.Empty;
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int DefeatedBy { get; set; }
    public DateTime Timestamp { get; set; }
}

[MemoryPackable]
public partial class BattleEndedEvent
{
    public string BattleId { get; set; } = string.Empty;
    public string WinnerTeam { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty; // elimination, timeout, surrender
    public TimeSpan Duration { get; set; }
    public Dictionary<int, BattleReward> Rewards { get; set; } = new();
    public Dictionary<string, object> Statistics { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

[MemoryPackable]
public partial class BattleReward
{
    public long Experience { get; set; }
    public int Gold { get; set; }
    public List<string> Items { get; set; } = new();
    public int Honor { get; set; }
    public Dictionary<string, object> Additional { get; set; } = new();
}
