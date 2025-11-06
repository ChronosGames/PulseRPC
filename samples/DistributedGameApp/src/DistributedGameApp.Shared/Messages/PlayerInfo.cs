using MemoryPack;
using System;

namespace DistributedGameApp.Shared.Messages
{

/// <summary>
/// 玩家信息
/// </summary>
[MemoryPackable]
public partial class PlayerInfo
{
    /// <summary>玩家ID</summary>
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>玩家名称</summary>
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>等级</summary>
    public int Level { get; set; }

    /// <summary>经验值</summary>
    public long Exp { get; set; }

    /// <summary>所在位置X</summary>
    public float PositionX { get; set; }

    /// <summary>所在位置Y</summary>
    public float PositionY { get; set; }

    /// <summary>所在位置Z</summary>
    public float PositionZ { get; set; }
}

/// <summary>
/// 玩家移动请求
/// </summary>
[MemoryPackable]
public partial class MoveRequest
{
    /// <summary>目标位置X</summary>
    public float TargetX { get; set; }

    /// <summary>目标位置Y</summary>
    public float TargetY { get; set; }

    /// <summary>目标位置Z</summary>
    public float TargetZ { get; set; }

    /// <summary>移动速度</summary>
    public float Speed { get; set; }
}

/// <summary>
/// 玩家移动结果
/// </summary>
[MemoryPackable]
public partial class MoveResult
{
    /// <summary>是否成功</summary>
    public bool Success { get; set; }

    /// <summary>当前位置X</summary>
    public float CurrentX { get; set; }

    /// <summary>当前位置Y</summary>
    public float CurrentY { get; set; }

    /// <summary>当前位置Z</summary>
    public float CurrentZ { get; set; }

    /// <summary>错误消息</summary>
    public string? ErrorMessage { get; set; }
}
}
