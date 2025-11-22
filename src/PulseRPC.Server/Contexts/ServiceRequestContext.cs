using System;
using System.Collections.Generic;
using System.Linq;
using PulseRPC.Authentication;

namespace PulseRPC.Server;

/// <summary>
/// 服务请求上下文实现
/// </summary>
public class ServiceRequestContext : IServiceRequestContext
{
    private readonly HashSet<string> _permissions;
    private readonly HashSet<string> _roles;
    private readonly Dictionary<string, string> _claims;

    public CallSourceType SourceType { get; init; }
    public string CallerId { get; init; } = string.Empty;
    public string? UserId { get; init; }
    public PID? ServicePID { get; init; }
    public string? Token { get; init; }
    public IReadOnlySet<string> Permissions => _permissions;
    public IReadOnlySet<string> Roles => _roles;
    public IReadOnlyDictionary<string, string> Claims => _claims;
    public string? IpAddress { get; init; }
    public DateTime AuthenticatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; init; }
    public string SessionId { get; init; } = Guid.NewGuid().ToString("N");
    public IAuthenticationContext? AuthenticationContext { get; init; }

    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;

    public ServiceRequestContext()
    {
        _permissions = new HashSet<string>();
        _roles = new HashSet<string>();
        _claims = new Dictionary<string, string>();
    }

    public ServiceRequestContext(HashSet<string> permissions, HashSet<string> roles, Dictionary<string, string> claims)
    {
        _permissions = permissions ?? new HashSet<string>();
        _roles = roles ?? new HashSet<string>();
        _claims = claims ?? new Dictionary<string, string>();
    }

    public bool HasPermission(string permission) => _permissions.Contains(permission);

    public bool HasRole(string role) => _roles.Contains(role);

    public bool HasAnyPermission(params string[] permissions)
        => permissions.Any(p => _permissions.Contains(p));

    public bool HasAllPermissions(params string[] permissions)
        => permissions.All(p => _permissions.Contains(p));

    /// <summary>
    /// 创建内部服务请求上下文
    /// </summary>
    public static ServiceRequestContext CreateServiceContext(PID servicePID, string serviceSecret)
    {
        return new ServiceRequestContext(
            new HashSet<string> { "*" }, // 内部服务拥有所有权限
            new HashSet<string> { "Service" },
            new Dictionary<string, string>())
        {
            SourceType = CallSourceType.InternalService,
            CallerId = servicePID.ToString(),
            ServicePID = servicePID,
            Token = serviceSecret
        };
    }

    /// <summary>
    /// 创建外部用户请求上下文
    /// </summary>
    public static ServiceRequestContext CreateUserContext(
        string userId,
        string token,
        HashSet<string> permissions,
        HashSet<string> roles,
        string? ipAddress = null,
        TimeSpan? expiresIn = null,
        IAuthenticationContext? authContext = null)
    {
        return new ServiceRequestContext(permissions, roles, new Dictionary<string, string>())
        {
            SourceType = CallSourceType.ExternalUser,
            CallerId = userId,
            UserId = userId,
            Token = token,
            IpAddress = ipAddress,
            ExpiresAt = expiresIn.HasValue ? DateTime.UtcNow.Add(expiresIn.Value) : null,
            AuthenticationContext = authContext
        };
    }

    /// <summary>
    /// 创建系统定时器上下文
    /// </summary>
    public static ServiceRequestContext CreateSystemContext()
    {
        return new ServiceRequestContext(
            new HashSet<string> { "*" },
            new HashSet<string> { "System" },
            new Dictionary<string, string>())
        {
            SourceType = CallSourceType.SystemTimer,
            CallerId = "System"
        };
    }

    /// <summary>
    /// 从认证上下文创建请求上下文
    /// </summary>
    public static ServiceRequestContext FromAuthenticationContext(IAuthenticationContext authContext)
    {
        var permissions = new HashSet<string>();
        var roles = new HashSet<string>();

        // 从 Scopes 中提取权限
        if (authContext.Scopes != null)
        {
            foreach (var scope in authContext.Scopes)
            {
                permissions.Add(scope);
            }
        }

        // 从 Principal 中提取角色
        if (authContext.Principal != null)
        {
            foreach (var claim in authContext.Principal.Claims)
            {
                if (claim.Type == System.Security.Claims.ClaimTypes.Role)
                {
                    roles.Add(claim.Value);
                }
            }
        }

        return new ServiceRequestContext(permissions, roles, new Dictionary<string, string>())
        {
            SourceType = CallSourceType.ExternalUser,
            CallerId = authContext.Identity ?? "Unknown",
            UserId = authContext.Identity,
            Token = authContext.Token,
            AuthenticatedAt = authContext.AuthenticationTime ?? DateTime.UtcNow,
            AuthenticationContext = authContext
        };
    }
}
