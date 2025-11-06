using System;
using System.Collections.Generic;
using MemoryPack;

namespace DistributedGameApp.Shared.Domain.Accounts;

/// <summary>
/// 登录请求
/// </summary>
[MemoryPackable]
public partial class LoginRequest
{
    /// <summary>
    /// 第三方登录提供商（google, facebook, apple）
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// 第三方提供商的 ID Token
    /// </summary>
    public string IdToken { get; set; } = string.Empty;
}

/// <summary>
/// 登录响应
/// </summary>
[MemoryPackable]
public partial class LoginResponse
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 错误消息
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// JWT Token
    /// </summary>
    public string JwtToken { get; set; } = string.Empty;

    /// <summary>
    /// Token 过期时间
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// 用户ID
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// 用户名
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// 可用的游戏服务器列表
    /// </summary>
    public List<GameServerInfo> GameServers { get; set; } = new();
}

/// <summary>
/// 游戏服务器信息
/// </summary>
[MemoryPackable]
public partial class GameServerInfo
{
    /// <summary>
    /// 服务器ID
    /// </summary>
    public string ServerId { get; set; } = string.Empty;

    /// <summary>
    /// 服务器名称
    /// </summary>
    public string ServerName { get; set; } = string.Empty;

    /// <summary>
    /// 主机地址
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// TCP 端口
    /// </summary>
    public int TcpPort { get; set; }

    /// <summary>
    /// KCP 端口
    /// </summary>
    public int KcpPort { get; set; }

    /// <summary>
    /// 当前负载（在线人数）
    /// </summary>
    public int CurrentLoad { get; set; }

    /// <summary>
    /// 最大容量
    /// </summary>
    public int MaxCapacity { get; set; }

    /// <summary>
    /// 服务器状态（Online, Maintenance, Full）
    /// </summary>
    public string Status { get; set; } = "Online";
}
