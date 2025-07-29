using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Authentication;

/// <summary>
/// 认证提供者接口
/// </summary>
public interface IAuthenticationProvider
{
    /// <summary>
    /// 获取认证令牌
    /// </summary>
    Task<AuthenticationToken> GetTokenAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 刷新认证令牌
    /// </summary>
    Task<AuthenticationToken> RefreshTokenAsync(AuthenticationToken currentToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证令牌是否有效
    /// </summary>
    bool IsTokenValid(AuthenticationToken token);

    /// <summary>
    /// 撤销令牌
    /// </summary>
    Task RevokeTokenAsync(AuthenticationToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// 支持的认证类型
    /// </summary>
    string AuthenticationType { get; }
}

/// <summary>
/// 认证令牌
/// </summary>
public class AuthenticationToken
{
    /// <summary>
    /// 令牌值
    /// </summary>
    public string Token { get; set; } = "";

    /// <summary>
    /// 令牌类型
    /// </summary>
    public string TokenType { get; set; } = "Bearer";

    /// <summary>
    /// 过期时间
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// 签发时间
    /// </summary>
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 刷新令牌
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// 声明信息
    /// </summary>
    public Dictionary<string, string> Claims { get; set; } = new();

    /// <summary>
    /// 作用域
    /// </summary>
    public string[] Scopes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 是否即将过期
    /// </summary>
    public bool IsExpiringSoon(TimeSpan threshold = default)
    {
        var checkThreshold = threshold == default ? TimeSpan.FromMinutes(5) : threshold;
        return DateTime.UtcNow.Add(checkThreshold) >= ExpiresAt;
    }

    /// <summary>
    /// 是否已过期
    /// </summary>
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    /// <summary>
    /// 获取授权头值
    /// </summary>
    public string GetAuthorizationHeaderValue()
    {
        return $"{TokenType} {Token}";
    }
}

/// <summary>
/// 认证消息
/// </summary>
public class AuthenticationMessage
{
    /// <summary>
    /// 令牌
    /// </summary>
    public string Token { get; set; } = "";

    /// <summary>
    /// 令牌类型
    /// </summary>
    public string TokenType { get; set; } = "Bearer";

    /// <summary>
    /// 客户端ID
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// 认证时间戳
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 请求的作用域
    /// </summary>
    public string[] RequestedScopes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 附加参数
    /// </summary>
    public Dictionary<string, string> Parameters { get; set; } = new();
}

/// <summary>
/// 认证结果
/// </summary>
public class AuthenticationResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 认证令牌
    /// </summary>
    public AuthenticationToken? Token { get; set; }

    /// <summary>
    /// 错误代码
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// 错误描述
    /// </summary>
    public string? ErrorDescription { get; set; }

    /// <summary>
    /// 授权的作用域
    /// </summary>
    public string[] GrantedScopes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 用户信息
    /// </summary>
    public Dictionary<string, object> UserInfo { get; set; } = new();

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static AuthenticationResult Success(AuthenticationToken token, string[]? grantedScopes = null)
    {
        return new AuthenticationResult
        {
            IsSuccess = true,
            Token = token,
            GrantedScopes = grantedScopes ?? Array.Empty<string>()
        };
    }

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static AuthenticationResult Failure(string errorCode, string? errorDescription = null)
    {
        return new AuthenticationResult
        {
            IsSuccess = false,
            ErrorCode = errorCode,
            ErrorDescription = errorDescription
        };
    }
}

/// <summary>
/// 认证异常
/// </summary>
public class AuthenticationException : Exception
{
    /// <summary>
    /// 错误代码
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// 认证结果
    /// </summary>
    public AuthenticationResult? Result { get; }

    public AuthenticationException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }

    public AuthenticationException(string errorCode, string message, Exception innerException) : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public AuthenticationException(AuthenticationResult result) : base(result.ErrorDescription ?? "Authentication failed")
    {
        ErrorCode = result.ErrorCode ?? "unknown_error";
        Result = result;
    }
}

/// <summary>
/// 认证配置
/// </summary>
public class AuthenticationConfiguration
{
    /// <summary>
    /// 认证类型
    /// </summary>
    public string AuthenticationType { get; set; } = "Bearer";

    /// <summary>
    /// 是否启用自动刷新
    /// </summary>
    public bool EnableAutoRefresh { get; set; } = true;

    /// <summary>
    /// 刷新阈值（提前多久刷新）
    /// </summary>
    public TimeSpan RefreshThreshold { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 重试间隔
    /// </summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 是否缓存令牌
    /// </summary>
    public bool EnableTokenCaching { get; set; } = true;

    /// <summary>
    /// 缓存过期时间
    /// </summary>
    public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromHours(1);
} 