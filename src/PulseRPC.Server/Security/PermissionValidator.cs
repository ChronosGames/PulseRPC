using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Contexts;

namespace PulseRPC.Server;

/// <summary>
/// 授权服务接口
/// </summary>
public interface IAuthorizationService
{
    /// <summary>检查是否有权限</summary>
    Task<bool> CheckPermissionAsync(IServiceRequestContext context, string permission);

    /// <summary>获取用户权限列表</summary>
    Task<HashSet<string>> GetUserPermissionsAsync(string userId);

    /// <summary>获取用户角色列表</summary>
    Task<HashSet<string>> GetUserRolesAsync(string userId);
}

/// <summary>
/// 权限验证器 - 验证方法调用的权限和角色要求
/// </summary>
public class PermissionValidator
{
    private readonly ILogger<PermissionValidator> _logger;

    public PermissionValidator(ILogger<PermissionValidator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 验证方法调用权限
    /// </summary>
    /// <param name="methodInfo">要调用的方法</param>
    /// <param name="authContext">认证上下文</param>
    /// <param name="errorMessage">验证失败时的错误信息</param>
    /// <returns>验证是否通过</returns>
    public bool ValidateMethodCall(
        MethodInfo methodInfo,
        IServiceRequestContext? authContext,
        out string? errorMessage)
    {
        errorMessage = null;

        // 没有认证上下文
        if (authContext == null)
        {
            // 检查是否需要认证
            if (HasAnyAuthenticationAttribute(methodInfo))
            {
                errorMessage = "Authentication required";
                _logger.LogWarning("Unauthenticated call to {Method}", methodInfo.Name);
                return false;
            }
            return true; // 方法不需要认证
        }

        // 检查Token是否过期
        if (authContext.IsExpired)
        {
            errorMessage = "Authentication token expired";
            _logger.LogWarning("Expired token for {Caller}", authContext.CallerId);
            return false;
        }

        // 检查 Internal 特性（含旧名 InternalOnly，后者派生自 InternalAttribute）
        var internalOnly = methodInfo.GetCustomAttribute<InternalAttribute>();
        if (internalOnly != null && authContext.SourceType != CallSourceType.InternalService)
        {
            errorMessage = "This method can only be called by internal services";
            _logger.LogWarning(
                "Unauthorized internal-only call - Method: {Method}, Caller: {Caller}, Source: {Source}",
                methodInfo.Name, authContext.CallerId, authContext.SourceType);
            return false;
        }

        // 检查ExternalOnly特性
        var externalOnly = methodInfo.GetCustomAttribute<ExternalOnlyAttribute>();
        if (externalOnly != null && authContext.SourceType != CallSourceType.ExternalUser)
        {
            errorMessage = "This method can only be called by external users";
            _logger.LogWarning(
                "Unauthorized external-only call - Method: {Method}, Caller: {Caller}, Source: {Source}",
                methodInfo.Name, authContext.CallerId, authContext.SourceType);
            return false;
        }

        // 检查权限要求
        var permissionAttrs = methodInfo.GetCustomAttributes<RequirePermissionAttribute>();
        foreach (var attr in permissionAttrs)
        {
            // 内部服务绕过检查
            if (attr.AllowInternal && authContext.SourceType == CallSourceType.InternalService)
                continue;

            // 系统调用绕过检查
            if (attr.AllowSystem && authContext.SourceType == CallSourceType.SystemTimer)
                continue;

            if (!authContext.HasPermission(attr.Permission))
            {
                errorMessage = $"Missing required permission: {attr.Permission}";
                _logger.LogWarning(
                    "Permission denied - Method: {Method}, Caller: {Caller}, Required: {Permission}",
                    methodInfo.Name, authContext.CallerId, attr.Permission);
                return false;
            }
        }

        // 检查角色要求
        var roleAttrs = methodInfo.GetCustomAttributes<RequireRoleAttribute>();
        foreach (var attr in roleAttrs)
        {
            if (attr.AllowInternal && authContext.SourceType == CallSourceType.InternalService)
                continue;

            if (attr.AllowSystem && authContext.SourceType == CallSourceType.SystemTimer)
                continue;

            if (!authContext.HasRole(attr.Role))
            {
                errorMessage = $"Missing required role: {attr.Role}";
                _logger.LogWarning(
                    "Role denied - Method: {Method}, Caller: {Caller}, Required: {Role}",
                    methodInfo.Name, authContext.CallerId, attr.Role);
                return false;
            }
        }

        _logger.LogDebug(
            "Method call authorized - Method: {Method}, Caller: {Caller}, Source: {Source}",
            methodInfo.Name, authContext.CallerId, authContext.SourceType);

        return true;
    }

    private bool HasAnyAuthenticationAttribute(MethodInfo methodInfo)
    {
        return methodInfo.GetCustomAttribute<RequirePermissionAttribute>() != null
            || methodInfo.GetCustomAttribute<RequireRoleAttribute>() != null
            || methodInfo.GetCustomAttribute<InternalAttribute>() != null
            || methodInfo.GetCustomAttribute<ExternalOnlyAttribute>() != null;
    }
}
