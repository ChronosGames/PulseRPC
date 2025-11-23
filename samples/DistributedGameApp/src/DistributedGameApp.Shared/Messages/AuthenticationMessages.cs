using System;
using MemoryPack;

namespace DistributedGameApp.Shared.Messages;

/// <summary>
/// 连接认证请求
/// </summary>
/// <remarks>
/// <para><strong>协议约定</strong>：</para>
/// <list type="bullet">
/// <item><description>此消息必须作为连接后的首个消息发送</description></item>
/// <item><description>服务端在收到此消息前会阻塞所有其他 RPC 调用</description></item>
/// <item><description>认证超时时间：5 秒</description></item>
/// <item><description>认证失败将立即断开连接</description></item>
/// </list>
/// </remarks>
[MemoryPackable]
public partial class ConnectionAuthRequest
{
    /// <summary>
    /// JWT Access Token（从 LoginServer 获取）
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// 客户端版本号（可选）
    /// </summary>
    public string? ClientVersion { get; set; }

    /// <summary>
    /// 客户端平台（可选，例如 "Windows", "iOS", "Android"）
    /// </summary>
    public string? Platform { get; set; }
}

/// <summary>
/// 连接认证响应
/// </summary>
[MemoryPackable]
public partial class ConnectionAuthResponse
{
    /// <summary>
    /// 认证是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 用户ID（认证成功时返回）
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// 用户名（认证成功时返回）
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// 用户角色列表
    /// </summary>
    public string[] Roles { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 用户权限列表
    /// </summary>
    public string[] Permissions { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Token 过期时间（Unix 时间戳）
    /// </summary>
    public DateTime? TokenExpiresAt { get; set; }

    /// <summary>
    /// 错误码（认证失败时返回）
    /// </summary>
    public int ErrorCode { get; set; }

    /// <summary>
    /// 错误消息（认证失败时返回）
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 认证错误码
/// </summary>
public static class AuthErrorCode
{
    /// <summary>
    /// Token 为空或格式不正确
    /// </summary>
    public const int InvalidToken = 4001;

    /// <summary>
    /// Token 验证失败
    /// </summary>
    public const int TokenValidationFailed = 4002;

    /// <summary>
    /// Token 已过期
    /// </summary>
    public const int TokenExpired = 4003;

    /// <summary>
    /// 认证超时
    /// </summary>
    public const int AuthTimeout = 4004;

    /// <summary>
    /// 用户已被封禁
    /// </summary>
    public const int UserBanned = 4005;

    /// <summary>
    /// 内部服务器错误
    /// </summary>
    public const int InternalError = 5000;
}
