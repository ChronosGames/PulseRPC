using System.Security.Claims;
using System.Threading.Tasks;

namespace PulseRPC.Authentication;

/// <summary>
/// 服务端认证验证器接口
/// </summary>
public interface IAuthenticationValidator
{
    /// <summary>
    /// 验证客户端提供的认证令牌
    /// </summary>
    /// <param name="token">认证令牌</param>
    /// <returns>验证结果，包含用户身份信息</returns>
    Task<ValidationResult> ValidateAsync(string token);
}

/// <summary>
/// 认证验证结果
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// 验证是否成功
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// 用户身份主体（验证成功时）
    /// </summary>
    public ClaimsPrincipal? Principal { get; set; }

    /// <summary>
    /// 错误消息（验证失败时）
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 创建成功的验证结果
    /// </summary>
    public static ValidationResult Success(ClaimsPrincipal principal) =>
        new ValidationResult { IsValid = true, Principal = principal };

    /// <summary>
    /// 创建失败的验证结果
    /// </summary>
    public static ValidationResult Failure(string errorMessage) =>
        new ValidationResult { IsValid = false, ErrorMessage = errorMessage };
}
