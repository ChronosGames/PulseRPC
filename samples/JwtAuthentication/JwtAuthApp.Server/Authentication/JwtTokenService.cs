using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace JwtAuthApp.Server.Authentication;

/// <summary>
/// 真实签发/校验 JWT（HMAC-SHA256），供 <c>AccountHub</c> 在应用层完成"连接级"认证。
/// </summary>
/// <remarks>
/// 与 <c>PulseRPC.Server.JwtTokenService</c>（框架内置的占位实现，仅用于演示接口形状）不同，
/// 本类型使用 <c>System.IdentityModel.Tokens.Jwt</c> 生成/校验符合 RFC 7519 的真实签名令牌。
/// </remarks>
public class JwtTokenService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromSeconds(15);

    private readonly SymmetricSecurityKey _securityKey;

    public JwtTokenService(IOptions<JwtTokenServiceOptions> jwtTokenServiceOptions)
    {
        _securityKey = new SymmetricSecurityKey(Convert.FromBase64String(jwtTokenServiceOptions.Value.Secret));
    }

    /// <summary>
    /// 为指定用户签发一个短生命周期的 JWT（默认 15 秒，便于在示例中演示过期后需要重新登录的场景）。
    /// </summary>
    public (string Token, DateTimeOffset Expires) CreateToken(long userId, string displayName, IReadOnlyCollection<string> roles)
    {
        var jwtTokenHandler = new JwtSecurityTokenHandler();
        var expires = DateTime.UtcNow.Add(TokenLifetime);

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, displayName),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var token = jwtTokenHandler.CreateEncodedJwt(new SecurityTokenDescriptor
        {
            SigningCredentials = new SigningCredentials(_securityKey, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(claims),
            Expires = expires,
        });

        return (token, expires);
    }

    /// <summary>
    /// 校验 JWT 的签名与有效期，成功时返回其中携带的 <see cref="ClaimsPrincipal"/>。
    /// </summary>
    public ClaimsPrincipal? ValidateToken(string token)
    {
        var jwtTokenHandler = new JwtSecurityTokenHandler();

        try
        {
            return jwtTokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                IssuerSigningKey = _securityKey,
                ValidateIssuerSigningKey = true,
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(2),
            }, out _);
        }
        catch (SecurityTokenException)
        {
            return null;
        }
    }
}

public class JwtTokenServiceOptions
{
    public JwtTokenServiceOptions()
    {
    }

    public JwtTokenServiceOptions(string secret)
    {
        Secret = secret;
    }

    public string Secret { get; set; } = string.Empty;
}
