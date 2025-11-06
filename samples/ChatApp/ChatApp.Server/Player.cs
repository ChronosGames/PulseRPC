using System;

namespace GameServer.World;

/// <summary>
/// 玩家类
/// </summary>
public class Player
{
    /// <summary>
    /// 玩家ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 玩家名称
    /// </summary>
    public required string Username { get; set; }

    /// <summary>
    /// 玩家等级
    /// </summary>
    public int Level { get; set; } = 1;

    /// <summary>
    /// 玩家位置
    /// </summary>
    public Vector3 Position { get; set; } = new Vector3();

    /// <summary>
    /// 玩家旋转角度
    /// </summary>
    public float RotationY { get; set; }

    /// <summary>
    /// 是否在线
    /// </summary>
    public bool IsOnline { get; set; }

    /// <summary>
    /// 上次登录时间
    /// </summary>
    public DateTime LastLoginTime { get; set; }

    /// <summary>
    /// 最后活动时间
    /// </summary>
    public DateTime LastActivityTime { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// 更新玩家活动时间
    /// </summary>
    public void UpdateActivity()
    {
        LastActivityTime = DateTime.UtcNow;
    }
}

/// <summary>
/// 3D向量
/// </summary>
public class Vector3
{
    /// <summary>
    /// X坐标
    /// </summary>
    public float X { get; set; }

    /// <summary>
    /// Y坐标
    /// </summary>
    public float Y { get; set; }

    /// <summary>
    /// Z坐标
    /// </summary>
    public float Z { get; set; }

    /// <summary>
    /// 创建向量
    /// </summary>
    public Vector3()
    {
    }

    /// <summary>
    /// 创建向量
    /// </summary>
    public Vector3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>
    /// 获取字符串表示
    /// </summary>
    public override string ToString()
    {
        return $"({X}, {Y}, {Z})";
    }
}
