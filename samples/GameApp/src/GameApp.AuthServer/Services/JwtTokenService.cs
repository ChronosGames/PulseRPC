using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using GameApp.AuthServer.Models;
using GameApp.AuthServer.Configuration;

namespace GameApp.AuthServer.Services;

/// <summary>
/// JWT Token 服务实现
/// </summary>
public class JwtTokenService : ITokenService
{
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<JwtTokenService> _logger;
    private readonly SecurityKey _signingKey;
    private readonly SigningCredentials _signingCredentials;

    public JwtTokenService(IOptions<JwtOptions> jwtOptions, ILogger<JwtTokenService> logger)
    {
        _jwtOptions = jwtOptions.Value;
        _logger = logger;

        var keyBytes = Encoding.UTF8.GetBytes(_jwtOptions.SecretKey);
        _signingKey = new SymmetricSecurityKey(keyBytes);
        _signingCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);
    }

    /// <summary>
    /// 生成 JWT Token
    /// </summary>
    public string GenerateJwtToken(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("user_id", user.UserId.ToString()),
            new Claim("username", user.Username),
            new Claim("status", user.Status),
            new Claim("vip_level", user.Profile.VipLevel.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes),
            Issuer = _jwtOptions.Issuer,
            Audience = _jwtOptions.Audience,
            SigningCredentials = _signingCredentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        _logger.LogDebug("Generated JWT token for user {UserId}", user.UserId);
        return tokenString;
    }

    /// <summary>
    /// 生成刷新 Token
    /// </summary>
    public string GenerateRefreshToken()
    {
        var randomBytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        var refreshToken = Convert.ToBase64String(randomBytes);

        _logger.LogDebug("Generated refresh token");
        return refreshToken;
    }

    /// <summary>
    /// 验证 JWT Token
    /// </summary>
    public Models.TokenValidationResult ValidateJwtToken(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _signingKey,
                ValidateIssuer = true,
                ValidIssuer = _jwtOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = _jwtOptions.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);
            var jwtToken = (JwtSecurityToken)validatedToken;

            var userClaims = ExtractUserClaims(token);

            return new Models.TokenValidationResult
            {
                IsValid = true,
                UserClaims = userClaims,
                ExpiresAt = jwtToken.ValidTo,
                Message = "Token is valid"
            };
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogWarning("Token validation failed: Token expired");
            return new Models.TokenValidationResult
            {
                IsValid = false,
                Message = "Token has expired"
            };
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning("Token validation failed: {Message}", ex.Message);
            return new Models.TokenValidationResult
            {
                IsValid = false,
                Message = "Invalid token"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during token validation");
            return new Models.TokenValidationResult
            {
                IsValid = false,
                Message = "Token validation error"
            };
        }
    }

    /// <summary>
    /// 从 Token 中提取用户信息
    /// </summary>
    public UserClaims? ExtractUserClaims(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var jsonToken = tokenHandler.ReadJwtToken(token);

            var userIdClaim = jsonToken.Claims.FirstOrDefault(x => x.Type == "user_id");
            var usernameClaim = jsonToken.Claims.FirstOrDefault(x => x.Type == "username");
            var emailClaim = jsonToken.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Email);

            if (userIdClaim == null || usernameClaim == null)
            {
                return null;
            }

            return new UserClaims
            {
                UserId = int.Parse(userIdClaim.Value),
                Username = usernameClaim.Value,
                Email = emailClaim?.Value ?? string.Empty,
                Permissions = new List<string>() // 可以从其他 claims 中提取权限信息
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract user claims from token");
            return null;
        }
    }
}
