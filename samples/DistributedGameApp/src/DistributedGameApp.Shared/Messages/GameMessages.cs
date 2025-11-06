using MemoryPack;
using System;
using System.Collections.Generic;

namespace DistributedGameApp.Shared.Messages;

/// <summary>
/// 登录请求
/// </summary>
[MemoryPackable]
public partial class LoginRequest
{
    /// <summary>账号</summary>
    public string Account { get; set; } = string.Empty;

    /// <summary>密码（实际应该加密）</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>设备ID</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>客户端版本</summary>
    public string ClientVersion { get; set; } = string.Empty;
}

/// <summary>
/// 登录响应
/// </summary>
[MemoryPackable]
public partial class LoginResponse
{
    /// <summary>是否成功</summary>
    public bool Success { get; set; }

    /// <summary>玩家ID</summary>
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>访问令牌</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>刷新令牌</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>令牌过期时间（Unix 时间戳）</summary>
    public long TokenExpireAt { get; set; }

    /// <summary>错误码</summary>
    public int ErrorCode { get; set; }

    /// <summary>错误消息</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 创建角色请求
/// </summary>
[MemoryPackable]
public partial class CreateCharacterRequest
{
    /// <summary>角色名称</summary>
    public string CharacterName { get; set; } = string.Empty;

    /// <summary>职业类型</summary>
    public CharacterClass Class { get; set; }

    /// <summary>性别</summary>
    public Gender Gender { get; set; }

    /// <summary>外观配置（JSON 字符串）</summary>
    public string AppearanceConfig { get; set; } = "{}";
}

/// <summary>
/// 角色信息
/// </summary>
[MemoryPackable]
public partial class CharacterInfo
{
    /// <summary>角色ID</summary>
    public string CharacterId { get; set; } = string.Empty;

    /// <summary>玩家ID</summary>
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>角色名称</summary>
    public string CharacterName { get; set; } = string.Empty;

    /// <summary>职业</summary>
    public CharacterClass Class { get; set; }

    /// <summary>性别</summary>
    public Gender Gender { get; set; }

    /// <summary>等级</summary>
    public int Level { get; set; }

    /// <summary>经验值</summary>
    public long Exp { get; set; }

    /// <summary>生命值</summary>
    public int Hp { get; set; }

    /// <summary>最大生命值</summary>
    public int MaxHp { get; set; }

    /// <summary>魔法值</summary>
    public int Mp { get; set; }

    /// <summary>最大魔法值</summary>
    public int MaxMp { get; set; }

    /// <summary>攻击力</summary>
    public int Attack { get; set; }

    /// <summary>防御力</summary>
    public int Defense { get; set; }

    /// <summary>速度</summary>
    public int Speed { get; set; }

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>最后登录时间</summary>
    public DateTime LastLoginAt { get; set; }

    /// <summary>位置</summary>
    public Position Position { get; set; } = new();

    /// <summary>在线状态</summary>
    public OnlineStatus OnlineStatus { get; set; }
}

/// <summary>
/// 位置信息
/// </summary>
[MemoryPackable]
public partial class Position
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public string MapId { get; set; } = string.Empty;
}

/// <summary>
/// 匹配请求
/// </summary>
[MemoryPackable]
public partial class MatchmakingRequest
{
    /// <summary>匹配模式</summary>
    public MatchMode Mode { get; set; }

    /// <summary>期望的队伍大小</summary>
    public int TeamSize { get; set; }

    /// <summary>是否组队匹配</summary>
    public bool IsPartyMatch { get; set; }

    /// <summary>队伍成员（如果是组队匹配）</summary>
    public List<string> PartyMembers { get; set; } = new();
}

/// <summary>
/// 匹配响应
/// </summary>
[MemoryPackable]
public partial class MatchmakingResponse
{
    /// <summary>是否成功</summary>
    public bool Success { get; set; }

    /// <summary>匹配票据ID</summary>
    public string TicketId { get; set; } = string.Empty;

    /// <summary>预估等待时间（秒）</summary>
    public int EstimatedWaitTime { get; set; }

    /// <summary>错误消息</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 匹配成功通知
/// </summary>
[MemoryPackable]
public partial class MatchFoundNotification
{
    /// <summary>战斗ID</summary>
    public string BattleId { get; set; } = string.Empty;

    /// <summary>战斗服务器地址</summary>
    public string BattleServerAddress { get; set; } = string.Empty;

    /// <summary>战斗服务器端口</summary>
    public int BattleServerPort { get; set; }

    /// <summary>队伍1</summary>
    public List<CharacterInfo> Team1 { get; set; } = new();

    /// <summary>队伍2</summary>
    public List<CharacterInfo> Team2 { get; set; } = new();

    /// <summary>倒计时（秒）</summary>
    public int Countdown { get; set; }
}

/// <summary>
/// 角色职业
/// </summary>
public enum CharacterClass
{
    /// <summary>战士</summary>
    Warrior = 1,
    /// <summary>法师</summary>
    Mage = 2,
    /// <summary>弓箭手</summary>
    Archer = 3,
    /// <summary>刺客</summary>
    Assassin = 4,
    /// <summary>牧师</summary>
    Priest = 5
}

/// <summary>
/// 性别
/// </summary>
public enum Gender
{
    /// <summary>男性</summary>
    Male = 1,
    /// <summary>女性</summary>
    Female = 2
}

/// <summary>
/// 在线状态
/// </summary>
public enum OnlineStatus
{
    /// <summary>离线</summary>
    Offline = 0,
    /// <summary>在线</summary>
    Online = 1,
    /// <summary>匹配中</summary>
    InMatchmaking = 2,
    /// <summary>战斗中</summary>
    InBattle = 3,
    /// <summary>离开</summary>
    Away = 4
}

/// <summary>
/// 匹配模式
/// </summary>
public enum MatchMode
{
    /// <summary>1v1 对战</summary>
    OneVsOne = 1,
    /// <summary>3v3 组队</summary>
    ThreeVsThree = 3,
    /// <summary>5v5 团战</summary>
    FiveVsFive = 5
}
