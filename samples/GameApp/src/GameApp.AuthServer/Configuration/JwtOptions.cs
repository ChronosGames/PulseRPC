namespace GameApp.AuthServer.Configuration;

/// <summary>
/// JWT 配置选项
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// 密钥
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// 发行者
    /// </summary>
    public string Issuer { get; set; } = "GameApp";

    /// <summary>
    /// 受众
    /// </summary>
    public string Audience { get; set; } = "GameApp.Client";

    /// <summary>
    /// Access Token 过期时间（分钟）
    /// </summary>
    public int AccessTokenExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// Refresh Token 过期时间（天）
    /// </summary>
    public int RefreshTokenExpirationDays { get; set; } = 30;
}
