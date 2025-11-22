using System;
using System.Collections.Generic;
using PulseRPC.Authentication;

namespace PulseRPC.Server;

/// <summary>
/// 请求调用来源类型
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
/// 服务请求上下文 - 携带调用者身份和权限信息（用于服务间调用和权限验证）
/// 整合了认证上下文和额外的请求信息
/// </summary>
public interface IServiceRequestContext
{
    /// <summary>调用来源类型</summary>
    CallSourceType SourceType { get; }

    /// <summary>调用者标识</summary>
    string CallerId { get; }

    /// <summary>用户ID（外部用户）</summary>
    string? UserId { get; }

    /// <summary>服务PID（内部服务）</summary>
    PID? ServicePID { get; }

    /// <summary>认证Token</summary>
    string? Token { get; }

    /// <summary>权限列表</summary>
    IReadOnlySet<string> Permissions { get; }

    /// <summary>角色列表</summary>
    IReadOnlySet<string> Roles { get; }

    /// <summary>额外声明</summary>
    IReadOnlyDictionary<string, string> Claims { get; }

    /// <summary>IP地址</summary>
    string? IpAddress { get; }

    /// <summary>认证时间</summary>
    DateTime AuthenticatedAt { get; }

    /// <summary>过期时间</summary>
    DateTime? ExpiresAt { get; }

    /// <summary>会话ID</summary>
    string SessionId { get; }

    /// <summary>底层认证上下文（如果有）</summary>
    IAuthenticationContext? AuthenticationContext { get; }

    /// <summary>
    /// 检查是否已过期
    /// </summary>
    bool IsExpired { get; }

    /// <summary>
    /// 检查是否有指定权限
    /// </summary>
    bool HasPermission(string permission);

    /// <summary>
    /// 检查是否有指定角色
    /// </summary>
    bool HasRole(string role);

    /// <summary>
    /// 检查是否有任一权限
    /// </summary>
    bool HasAnyPermission(params string[] permissions);

    /// <summary>
    /// 检查是否有所有权限
    /// </summary>
    bool HasAllPermissions(params string[] permissions);
}
