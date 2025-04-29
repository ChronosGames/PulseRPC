using System;
using System.Collections.Generic;

namespace ChatApp.Server;

/// <summary>
/// 玩家实体
/// </summary>
public class Player
{
    // 基本信息
    public string Id { get; set; }
    public string Username { get; set; }
    public string DisplayName { get; set; }
    public int Level { get; set; }
    public long Experience { get; set; }
    public long ExperienceNextLevel { get; set; }
    public string AvatarUrl { get; set; }
    public PlayerStatus Status { get; set; }
    public DateTime CreateTime { get; set; }
    public DateTime LastLoginTime { get; set; }

    // 资源与货币
    public long Gold { get; set; }
    public long Diamonds { get; set; }
    public int Energy { get; set; }
    public int MaxEnergy { get; set; }
    public int EnhanceStones { get; set; }
    public int ProtectionScrolls { get; set; }
    public Dictionary<string, long> Currencies { get; set; } = new Dictionary<string, long>();

    // 社交信息
    public string GuildId { get; set; }
    public string GuildRole { get; set; }
    public int GuildContribution { get; set; }
    public List<string> FriendIds { get; set; } = new List<string>();

    // 游戏进度与成就
    public Dictionary<string, int> Achievements { get; set; } = new Dictionary<string, int>();
    public Dictionary<string, int> QuestProgress { get; set; } = new Dictionary<string, int>();
    public Dictionary<string, bool> CompletedStages { get; set; } = new Dictionary<string, bool>();

    // 战斗属性
    public int TotalPower { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int Health { get; set; }
    public double CritRate { get; set; }
    public double CritDamage { get; set; }

    // 装备与技能
    public Dictionary<string, Guid> EquippedItems { get; set; } = new Dictionary<string, Guid>();
    public List<string> UnlockedSkills { get; set; } = new List<string>();
    public Dictionary<string, int> SkillLevels { get; set; } = new Dictionary<string, int>();

    // 账户状态
    public bool IsLocked { get; set; }
    public DateTime? LockExpiry { get; set; }
    public string LockReason { get; set; }

    // 服务器相关
    public string CurrentServerId { get; set; }
    public string LastServerId { get; set; }

    // 版本控制
    public long Version { get; set; }
}

/// <summary>
/// 玩家状态枚举
/// </summary>
public enum PlayerStatus
{
    Online,
    Offline,
    Away,
    Busy,
    InMatch,
    InRaid
}

/// <summary>
/// 背包物品
/// </summary>
public class InventoryItem
{
    public Guid Id { get; set; }
    public string PlayerId { get; set; }
    public string ItemId { get; set; }
    public string Type { get; set; }
    public int Quantity { get; set; }
    public DateTime AcquireTime { get; set; }
    public DateTime? ExpiryTime { get; set; }
    public bool Bound { get; set; }
    public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>();
}

/// <summary>
/// 资源更新请求
/// </summary>
public class ResourceUpdateRequest
{
    public string RequestId { get; set; }
    public string Source { get; set; }
    public Dictionary<string, long> ResourceChanges { get; set; } = new Dictionary<string, long>();
    public bool AllowNegative { get; set; } = false;
    public bool CheckOptimisticLock { get; set; } = true;
    public long ExpectedVersion { get; set; }
}

/// <summary>
/// 资源更新结果
/// </summary>
public class ResourceUpdateResult
{
    public bool Success { get; set; }
    public string ErrorCode { get; set; }
    public string ErrorMessage { get; set; }
    public Dictionary<string, long> NewValues { get; set; } = new Dictionary<string, long>();
    public long NewVersion { get; set; }
}

/// <summary>
/// 玩家统计信息
/// </summary>
public class PlayerStatistics
{
    public string PlayerId { get; set; }
    public int TotalLogins { get; set; }
    public int TotalPlayTimeMinutes { get; set; }
    public int TotalPurchases { get; set; }
    public decimal TotalAmountSpent { get; set; }
    public DateTime FirstLoginTime { get; set; }
    public Dictionary<string, int> GameModeStats { get; set; } = new Dictionary<string, int>();
    public Dictionary<string, int> ItemsCrafted { get; set; } = new Dictionary<string, int>();
    public Dictionary<string, int> ItemsEnhanced { get; set; } = new Dictionary<string, int>();
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public Dictionary<string, int> AchievementCounts { get; set; } = new Dictionary<string, int>();
}

/// <summary>
/// 玩家操作日志
/// </summary>
public class PlayerActionLog
{
    public Guid Id { get; set; }
    public string PlayerId { get; set; }
    public string ActionType { get; set; }
    public Dictionary<string, object> ActionDetails { get; set; } = new Dictionary<string, object>();
    public string ServerId { get; set; }
    public string ClientIp { get; set; }
    public string DeviceInfo { get; set; }
    public DateTime Timestamp { get; set; }
    public string SessionId { get; set; }
}
