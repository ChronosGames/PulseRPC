using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Contexts;

namespace PulseRPC.Server;

/// <summary>
/// 认证服务接口 - 用于验证服务间调用和用户身份
/// </summary>
public interface IAuthenticationService
{
    /// <summary>验证内部服务认证</summary>
    Task<ServiceRequestContext?> AuthenticateServiceAsync(PID servicePID, string serviceSecret);

    /// <summary>验证外部用户Token</summary>
    Task<ServiceRequestContext?> AuthenticateUserAsync(string token);

    /// <summary>生成服务密钥</summary>
    string GenerateServiceSecret(PID servicePID);

    /// <summary>验证服务密钥</summary>
    bool ValidateServiceSecret(PID servicePID, string secret);
}

/// <summary>
/// 认证服务实现 - 支持服务间 HMAC 认证和用户 JWT 认证
/// </summary>
public class AuthenticationService : IAuthenticationService
{
    private readonly ILogger<AuthenticationService> _logger;
    private readonly string _clusterSecret; // 集群共享密钥
    private readonly ConcurrentDictionary<PID, string> _serviceSecrets = new();
    private readonly IJwtTokenService _jwtTokenService;

    public AuthenticationService(
        ILogger<AuthenticationService> logger,
        IConfiguration configuration,
        IJwtTokenService jwtTokenService)
    {
        _logger = logger;
        _clusterSecret = configuration["ClusterSecret"] ?? throw new InvalidOperationException("ClusterSecret not configured");
        _jwtTokenService = jwtTokenService;
    }

    /// <summary>
    /// 验证内部服务 - 使用共享密钥或证书
    /// </summary>
    public async Task<ServiceRequestContext?> AuthenticateServiceAsync(PID servicePID, string serviceSecret)
    {
        try
        {
            // 方案1: 验证服务密钥（基于PID生成）
            if (ValidateServiceSecret(servicePID, serviceSecret))
            {
                _logger.LogDebug("Service authenticated - PID: {PID}", servicePID);

                return ServiceRequestContext.CreateServiceContext(servicePID, serviceSecret);
            }

            _logger.LogWarning("Service authentication failed - PID: {PID}", servicePID);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error authenticating service - PID: {PID}", servicePID);
            return null;
        }
    }

    /// <summary>
    /// 验证外部用户 - JWT Token
    /// </summary>
    public async Task<ServiceRequestContext?> AuthenticateUserAsync(string token)
    {
        try
        {
            // 验证JWT Token
            var claims = await _jwtTokenService.ValidateTokenAsync(token);
            if (claims == null)
            {
                _logger.LogWarning("Invalid user token");
                return null;
            }

            // 提取用户 ID (JWT 的 sub claim 会被自动映射为 ClaimTypes.NameIdentifier)
            var userId = claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("Token missing user ID");
                return null;
            }


            // 提取权限和角色
            var permissions = claims
                .Where(c => c.Type == "permission")
                .Select(c => c.Value)
                .ToHashSet();

            var roles = claims
                .Where(c => c.Type == "role")
                .Select(c => c.Value)
                .ToHashSet();

            var expiresAt = claims.FirstOrDefault(x => x.Type is "exp")?.Value;
            var expirationTime = expiresAt != null
                ? DateTimeOffset.FromUnixTimeSeconds(long.Parse(expiresAt)).UtcDateTime
                : (DateTime?)null;

            _logger.LogDebug("User authenticated - UserId: {UserId}, Roles: {Roles}", userId, string.Join(",", roles));

            return ServiceRequestContext.CreateUserContext(
                userId,
                token,
                permissions,
                roles,
                expiresIn: expirationTime.HasValue ? expirationTime.Value - DateTime.UtcNow : null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error authenticating user token");
            return null;
        }
    }

    /// <summary>
    /// 生成服务密钥 - 基于PID和集群密钥的HMAC
    /// </summary>
    public string GenerateServiceSecret(PID servicePID)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(_clusterSecret));

        var data = System.Text.Encoding.UTF8.GetBytes(servicePID.ToString());
        var hash = hmac.ComputeHash(data);

        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// 验证服务密钥
    /// </summary>
    public bool ValidateServiceSecret(PID servicePID, string secret)
    {
        var expectedSecret = GenerateServiceSecret(servicePID);
        return expectedSecret == secret;
    }
}
