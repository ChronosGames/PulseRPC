using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using PulseRPC.Authentication;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace PulseRPC.Server.Security;

/// <summary>
/// 基于JWT的认证验证器
/// </summary>
public class JwtAuthenticationProvider : IAuthenticationValidator
{
    private readonly JwtSecurityTokenHandler _tokenHandler;
    private readonly TokenValidationParameters _validationParameters;
    private readonly ILogger _logger;

    /// <summary>
    /// 创建JWT身份验证提供者
    /// </summary>
    /// <param name="secret">用于验证签名的密钥</param>
    /// <param name="issuer">JWT发行者</param>
    /// <param name="audience">JWT接收者</param>
    /// <param name="logger">日志记录器</param>
    public JwtAuthenticationProvider(string secret, string issuer, string audience, ILogger logger)
    {
        _tokenHandler = new JwtSecurityTokenHandler();
        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidateIssuer = !string.IsNullOrEmpty(issuer),
            ValidIssuer = issuer,
            ValidateAudience = !string.IsNullOrEmpty(audience),
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
        _logger = logger;
    }

    /// <summary>
    /// 验证JWT令牌
    /// </summary>
    /// <param name="token">JWT令牌</param>
    /// <returns>验证结果</returns>
    public async Task<ValidationResult> ValidateAsync(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return ValidationResult.Failure("未提供认证令牌");
        }

        try
        {
            // 验证令牌是异步操作的，但JwtSecurityTokenHandler不提供异步API
            // 所以这里使用Task.Run包装同步操作
            var principal = await Task.Run(() =>
                _tokenHandler.ValidateToken(token, _validationParameters, out var validatedToken));

            return ValidationResult.Success(principal);
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogWarning("令牌已过期");
            return ValidationResult.Failure("认证令牌已过期");
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            _logger.LogWarning("令牌签名无效");
            return ValidationResult.Failure("认证令牌签名无效");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "验证令牌时发生错误");
            return ValidationResult.Failure($"认证失败: {ex.Message}");
        }
    }
}
