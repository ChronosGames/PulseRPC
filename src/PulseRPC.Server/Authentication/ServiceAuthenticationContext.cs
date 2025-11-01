using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace PulseRPC.Server;

// ========================
// 服务认证上下文定义
// ========================

/// <summary>
/// 调用来源类型
/// </summary>
public enum CallSourceType
{
    /// <summary>内部服务（集群内）</summary>
    InternalService,

    /// <summary>外部用户（终端）</summary>
    ExternalUser,

    /// <summary>系统定时器</summary>
    SystemTimer,

    /// <summary>管理后台</summary>
    AdminConsole
}

/// <summary>
/// 服务认证上下文 - 携带调用者身份信息（用于服务间调用和权限验证）
/// </summary>
public class ServiceAuthenticationContext
{
    /// <summary>调用来源类型</summary>
    public CallSourceType SourceType { get; set; }

    /// <summary>调用者标识</summary>
    public string CallerId { get; set; } = string.Empty;

    /// <summary>用户ID（外部用户）</summary>
    public string? UserId { get; set; }

    /// <summary>服务PID（内部服务）</summary>
    public PID? ServicePID { get; set; }

    /// <summary>认证Token</summary>
    public string? Token { get; set; }

    /// <summary>权限列表</summary>
    public HashSet<string> Permissions { get; set; } = new();

    /// <summary>角色列表</summary>
    public HashSet<string> Roles { get; set; } = new();

    /// <summary>额外声明</summary>
    public Dictionary<string, string> Claims { get; set; } = new();

    /// <summary>IP地址</summary>
    public string? IpAddress { get; set; }

    /// <summary>认证时间</summary>
    public DateTime AuthenticatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>过期时间</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>会话ID</summary>
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 检查是否已过期
    /// </summary>
    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;

    /// <summary>
    /// 检查是否有指定权限
    /// </summary>
    public bool HasPermission(string permission) => Permissions.Contains(permission);

    /// <summary>
    /// 检查是否有指定角色
    /// </summary>
    public bool HasRole(string role) => Roles.Contains(role);

    /// <summary>
    /// 检查是否有任一权限
    /// </summary>
    public bool HasAnyPermission(params string[] permissions)
        => permissions.Any(p => Permissions.Contains(p));

    /// <summary>
    /// 检查是否有所有权限
    /// </summary>
    public bool HasAllPermissions(params string[] permissions)
        => permissions.All(p => Permissions.Contains(p));

    /// <summary>
    /// 创建内部服务认证上下文
    /// </summary>
    public static ServiceAuthenticationContext CreateServiceContext(PID servicePID, string serviceSecret)
    {
        return new ServiceAuthenticationContext
        {
            SourceType = CallSourceType.InternalService,
            CallerId = servicePID.ToString(),
            ServicePID = servicePID,
            Token = serviceSecret,
            Permissions = new HashSet<string> { "*" }, // 内部服务拥有所有权限
            Roles = new HashSet<string> { "Service" }
        };
    }

    /// <summary>
    /// 创建外部用户认证上下文
    /// </summary>
    public static ServiceAuthenticationContext CreateUserContext(
        string userId,
        string token,
        HashSet<string> permissions,
        HashSet<string> roles,
        string? ipAddress = null,
        TimeSpan? expiresIn = null)
    {
        return new ServiceAuthenticationContext
        {
            SourceType = CallSourceType.ExternalUser,
            CallerId = userId,
            UserId = userId,
            Token = token,
            Permissions = permissions,
            Roles = roles,
            IpAddress = ipAddress,
            ExpiresAt = expiresIn.HasValue ? DateTime.UtcNow.Add(expiresIn.Value) : null
        };
    }

    /// <summary>
    /// 创建系统定时器上下文
    /// </summary>
    public static ServiceAuthenticationContext CreateSystemContext()
    {
        return new ServiceAuthenticationContext
        {
            SourceType = CallSourceType.SystemTimer,
            CallerId = "System",
            Permissions = new HashSet<string> { "*" },
            Roles = new HashSet<string> { "System" }
        };
    }
}

/// <summary>
/// 服务认证上下文提供者 - 使用AsyncLocal保证线程安全
/// </summary>
public static class ServiceAuthenticationContextProvider
{
    private static readonly AsyncLocal<ServiceAuthenticationContext?> _current = new();

    /// <summary>
    /// 当前认证上下文
    /// </summary>
    public static ServiceAuthenticationContext? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    /// <summary>
    /// 确保有认证上下文，否则抛出异常
    /// </summary>
    public static ServiceAuthenticationContext RequireCurrent()
    {
        return Current ?? throw new UnauthorizedAccessException("No authentication context available");
    }

    /// <summary>
    /// 设置上下文并返回Disposable用于自动清理
    /// </summary>
    public static IDisposable SetContext(ServiceAuthenticationContext context)
    {
        var previous = Current;
        Current = context;
        return new ContextScope(previous);
    }

    private class ContextScope(ServiceAuthenticationContext? previous) : IDisposable
    {
        public void Dispose()
        {
            Current = previous;
        }
    }
}
