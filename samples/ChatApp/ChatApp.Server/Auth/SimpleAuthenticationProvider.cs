using Microsoft.Extensions.Logging;
using PulseRPC.Server.Auth;
using System.Security.Claims;
using GameServer.World;
using System;
using System.Threading.Tasks;

namespace ChatApp.Server.Auth;

/// <summary>
/// 简单的身份验证提供者，用于ChatApp的用户名/密码认证
/// </summary>
public class SimpleAuthenticationProvider : IAuthenticationProvider
{
    private readonly IPlayerManager _playerManager;
    private readonly ILogger<SimpleAuthenticationProvider> _logger;

    public SimpleAuthenticationProvider(IPlayerManager playerManager, ILogger<SimpleAuthenticationProvider> logger)
    {
        _playerManager = playerManager;
        _logger = logger;
    }

    /// <summary>
    /// 验证用户凭证
    /// </summary>
    /// <param name="credentials">格式: "username:password"</param>
    /// <returns>认证结果</returns>
    public async Task<AuthenticationResult> AuthenticateAsync(string credentials)
    {
        try
        {
            if (string.IsNullOrEmpty(credentials))
            {
                return AuthenticationResult.Fail("未提供认证凭证");
            }

            // 解析凭证
            var (username, password) = ParseCredentials(credentials);

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                return AuthenticationResult.Fail("凭证格式无效");
            }

            // 验证密码 (简化处理，实际应使用加密)
            if (password != "password")
            {
                _logger.LogWarning("用户 {Username} 密码错误", username);
                return AuthenticationResult.Fail("用户名或密码错误");
            }

            // 获取或创建玩家
            var player = await _playerManager.GetOrCreatePlayerAsync(username);

            // 创建用户身份
            var userPrincipal = CreateUserPrincipal(username, player.Id);

            _logger.LogInformation("用户 {Username} (ID: {PlayerId}) 认证成功", username, player.Id);

            return AuthenticationResult.Success(userPrincipal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "认证过程中发生异常");
            return AuthenticationResult.Fail($"认证失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 解析认证凭证
    /// </summary>
    /// <param name="credentials">格式: "username:password"</param>
    /// <returns>用户名和密码</returns>
    private (string username, string password) ParseCredentials(string credentials)
    {
        var parts = credentials.Split(':', 2);
        if (parts.Length != 2)
        {
            return (string.Empty, string.Empty);
        }

        return (parts[0].Trim(), parts[1].Trim());
    }

    /// <summary>
    /// 创建用户身份信息
    /// </summary>
    /// <param name="username">用户名</param>
    /// <param name="userId">用户ID</param>
    /// <returns>Claims身份</returns>
    private ClaimsPrincipal CreateUserPrincipal(string username, Guid userId)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, username),
            new Claim("PlayerId", userId.ToString()),
            new Claim("Username", username)
        };

        var identity = new ClaimsIdentity(claims, "SimpleAuth");
        return new ClaimsPrincipal(identity);
    }
}
