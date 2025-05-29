using System.Security.Claims;

namespace PulseRPC.Server.Authentication;

/// <summary>
/// 提供身份验证功能的接口
/// </summary>
public interface IAuthenticationProvider
{
    /// <summary>
    /// 验证客户端凭证
    /// </summary>
    /// <param name="credentials">认证凭证（例如令牌或用户名密码）</param>
    /// <returns>认证结果，包含用户信息或错误消息</returns>
    Task<AuthenticationResult> AuthenticateAsync(string credentials);
}

/// <summary>
/// 认证结果
/// </summary>
public class AuthenticationResult
{
    /// <summary>
    /// 认证是否成功
    /// </summary>
    public bool IsAuthenticated { get; set; }

    /// <summary>
    /// 用户标识
    /// </summary>
    public ClaimsPrincipal? User { get; set; }

    /// <summary>
    /// 认证失败时的错误消息
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 创建成功的认证结果
    /// </summary>
    public static AuthenticationResult Success(ClaimsPrincipal user) =>
        new AuthenticationResult { IsAuthenticated = true, User = user };

    /// <summary>
    /// 创建失败的认证结果
    /// </summary>
    public static AuthenticationResult Fail(string errorMessage) =>
        new AuthenticationResult { IsAuthenticated = false, ErrorMessage = errorMessage };
}
