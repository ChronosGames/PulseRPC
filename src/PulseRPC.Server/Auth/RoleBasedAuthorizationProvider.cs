using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace PulseRPC.Server.Auth;

/// <summary>
/// 基于角色的授权提供者
/// </summary>
public class RoleBasedAuthorizationProvider : IAuthorizationProvider
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _serviceRoles = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _methodRoles = new();
    private readonly ILogger _logger;

    /// <summary>
    /// 创建基于角色的授权提供者
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public RoleBasedAuthorizationProvider(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 为服务添加角色要求
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="requiredRoles">所需角色列表</param>
    public void RequireRolesForService(string serviceName, params string[] requiredRoles)
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            throw new ArgumentException("服务名称不能为空", nameof(serviceName));
        }

        if (requiredRoles == null || requiredRoles.Length == 0)
        {
            return;
        }

        var roles = new HashSet<string>(requiredRoles, StringComparer.OrdinalIgnoreCase);
        _serviceRoles.AddOrUpdate(serviceName, roles, (_, existing) =>
        {
            existing.UnionWith(roles);
            return existing;
        });
    }

    /// <summary>
    /// 为方法添加角色要求
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="methodName">方法名称</param>
    /// <param name="requiredRoles">所需角色列表</param>
    public void RequireRolesForMethod(string serviceName, string methodName, params string[] requiredRoles)
    {
        if (string.IsNullOrEmpty(serviceName) || string.IsNullOrEmpty(methodName))
        {
            throw new ArgumentException("服务名称和方法名称不能为空");
        }

        if (requiredRoles == null || requiredRoles.Length == 0)
        {
            return;
        }

        var key = $"{serviceName}.{methodName}";
        var roles = new HashSet<string>(requiredRoles, StringComparer.OrdinalIgnoreCase);
        _methodRoles.AddOrUpdate(key, roles, (_, existing) =>
        {
            existing.UnionWith(roles);
            return existing;
        });
    }

    /// <summary>
    /// 验证用户权限
    /// </summary>
    /// <param name="user">用户</param>
    /// <param name="serviceName">服务名称</param>
    /// <param name="methodName">方法名称</param>
    /// <returns>授权结果</returns>
    public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, string serviceName, string methodName)
    {
        if (user == null)
        {
            return Task.FromResult(AuthorizationResult.Fail("未提供用户信息"));
        }

        try
        {
            // 检查服务级别的角色要求
            if (_serviceRoles.TryGetValue(serviceName, out var serviceRoles) && serviceRoles.Count > 0)
            {
                bool hasServiceRole = serviceRoles.Any(role => user.IsInRole(role));
                if (!hasServiceRole)
                {
                    var userId = user.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "未知用户";
                    _logger.LogWarning("用户 {UserId} 没有访问服务 {ServiceName} 所需的角色",
                        userId, serviceName);
                    return Task.FromResult(AuthorizationResult.Fail("用户没有访问此服务的权限"));
                }
            }

            // 检查方法级别的角色要求
            var methodKey = $"{serviceName}.{methodName}";
            if (_methodRoles.TryGetValue(methodKey, out var methodRoles) && methodRoles.Count > 0)
            {
                bool hasMethodRole = methodRoles.Any(role => user.IsInRole(role));
                if (!hasMethodRole)
                {
                    var userId = user.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "未知用户";
                    _logger.LogWarning("用户 {UserId} 没有调用方法 {MethodName} 所需的角色",
                        userId, methodKey);
                    return Task.FromResult(AuthorizationResult.Fail("用户没有调用此方法的权限"));
                }
            }

            return Task.FromResult(AuthorizationResult.Success());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "授权验证时发生错误");
            return Task.FromResult(AuthorizationResult.Fail($"授权失败: {ex.Message}"));
        }
    }
}
