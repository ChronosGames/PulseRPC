using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace DistributedGameApp.LoginServer.Services;

/// <summary>
/// JWT 令牌服务
/// </summary>
public class JwtService
{
    private readonly JwtOptions _options;
    private readonly ILogger<JwtService> _logger;

    public JwtService(IOptions<JwtOptions> options, ILogger<JwtService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 生成访问令牌
    /// </summary>
    public string GenerateAccessToken(string userId, string username, Dictionary<string, string>? claims = null)
    {
        var tokenClaims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.UniqueName, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim("type", "client") // 认证类型
        };

        // 添加自定义声明
        if (claims != null)
        {
            foreach (var claim in claims)
            {
                tokenClaims.Add(new Claim(claim.Key, claim.Value));
            }
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expiresAt = DateTime.UtcNow.AddMinutes(_options.AccessTokenExpirationMinutes);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: tokenClaims,
            expires: expiresAt,
            signingCredentials: credentials
        );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        _logger.LogInformation("Generated access token for user {UserId}", userId);

        return tokenString;
    }

    /// <summary>
    /// 生成访问令牌（增强版 - 支持角色和权限）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="username">用户名</param>
    /// <param name="roles">用户角色列表</param>
    /// <param name="permissions">用户权限列表</param>
    /// <param name="additionalClaims">额外的声明</param>
    /// <returns>JWT Token</returns>
    public string GenerateAccessToken(
        string userId,
        string username,
        string[] roles,
        string[] permissions,
        Dictionary<string, string>? additionalClaims = null)
    {
        var tokenClaims = new List<Claim>
        {
            // 标准声明
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.UniqueName, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),

            // 认证类型
            new Claim("type", "client")
        };

        // 添加角色
        foreach (var role in roles)
        {
            tokenClaims.Add(new Claim(ClaimTypes.Role, role));
        }

        // 添加权限
        foreach (var permission in permissions)
        {
            tokenClaims.Add(new Claim("permission", permission));
        }

        // 添加额外声明
        if (additionalClaims != null)
        {
            foreach (var claim in additionalClaims)
            {
                tokenClaims.Add(new Claim(claim.Key, claim.Value));
            }
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expiresAt = DateTime.UtcNow.AddMinutes(_options.AccessTokenExpirationMinutes);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: tokenClaims,
            expires: expiresAt,
            signingCredentials: credentials
        );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        _logger.LogInformation(
            "Generated access token for user {UserId} with roles: {Roles}, permissions: {Permissions}",
            userId,
            string.Join(", ", roles),
            string.Join(", ", permissions));

        return tokenString;
    }

    /// <summary>
    /// 生成服务间认证令牌
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="serviceName">服务名称</param>
    /// <param name="scopes">权限范围</param>
    /// <returns>JWT Token</returns>
    public string GenerateServiceToken(string serviceId, string serviceName, string[] scopes)
    {
        var tokenClaims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, serviceId),
            new Claim(JwtRegisteredClaimNames.UniqueName, serviceName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("type", "service")
        };

        // 添加权限范围
        foreach (var scope in scopes)
        {
            tokenClaims.Add(new Claim("scope", scope));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: tokenClaims,
            expires: DateTime.UtcNow.AddHours(24), // 服务Token有效期更长
            signingCredentials: credentials
        );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        _logger.LogInformation(
            "Generated service token for {ServiceId} with scopes: {Scopes}",
            serviceId,
            string.Join(", ", scopes));

        return tokenString;
    }

    /// <summary>
    /// 生成刷新令牌
    /// </summary>
    public string GenerateRefreshToken(string userId)
    {
        var tokenClaims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("token_type", "refresh")
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: tokenClaims,
            expires: DateTime.UtcNow.AddDays(_options.RefreshTokenExpirationDays),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// 验证令牌
    /// </summary>
    public ClaimsPrincipal? ValidateToken(string token, bool validateLifetime = true)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_options.SecretKey);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _options.Issuer,
                ValidateAudience = true,
                ValidAudience = _options.Audience,
                ValidateLifetime = validateLifetime,
                ClockSkew = TimeSpan.Zero
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

            return principal;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return null;
        }
    }

    /// <summary>
    /// 从令牌中提取用户ID
    /// </summary>
    public string? GetUserIdFromToken(string token)
    {
        var principal = ValidateToken(token, validateLifetime: false);
        return principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
    }

    /// <summary>
    /// 检查令牌是否为刷新令牌
    /// </summary>
    public bool IsRefreshToken(string token)
    {
        var principal = ValidateToken(token, validateLifetime: false);
        return principal?.FindFirst("token_type")?.Value == "refresh";
    }
}

/// <summary>
/// JWT 配置选项
/// </summary>
public class JwtOptions
{
    /// <summary>
    /// 配置节名称
    /// </summary>
    public const string SectionName = "Jwt";

    /// <summary>
    /// 密钥（至少32字符）
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// 发行者
    /// </summary>
    public string Issuer { get; set; } = "DistributedGameApp";

    /// <summary>
    /// 受众
    /// </summary>
    public string Audience { get; set; } = "DistributedGameApp.Client";

    /// <summary>
    /// 访问令牌过期时间（分钟）
    /// </summary>
    public int AccessTokenExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// 刷新令牌过期时间（天）
    /// </summary>
    public int RefreshTokenExpirationDays { get; set; } = 30;
}
