using System.Security.Claims;

namespace PulseRPC.Server.Auth;

/// <summary>
/// 授权提供者接口
/// </summary>
public interface IAuthorizationProvider
{
    /// <summary>
    /// 验证用户是否有权限访问指定的服务和方法
    /// </summary>
    /// <param name="user">用户身份</param>
    /// <param name="serviceName">服务名称</param>
    /// <param name="methodName">方法名称</param>
    /// <returns>授权结果</returns>
    Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, string serviceName, string methodName);
}

/// <summary>
/// 授权结果
/// </summary>
public class AuthorizationResult
{
    /// <summary>
    /// 是否已授权
    /// </summary>
    public bool IsAuthorized { get; set; }

    /// <summary>
    /// 授权失败的错误消息
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 创建成功的授权结果
    /// </summary>
    public static AuthorizationResult Success() =>
        new AuthorizationResult { IsAuthorized = true };

    /// <summary>
    /// 创建失败的授权结果
    /// </summary>
    public static AuthorizationResult Fail(string errorMessage) =>
        new AuthorizationResult { IsAuthorized = false, ErrorMessage = errorMessage };
}
