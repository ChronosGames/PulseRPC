using System;

namespace PulseRPC.Server;

/// <summary>
/// 权限验证特性 - 标记在方法上，要求调用者具有指定权限
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class RequirePermissionAttribute : Attribute
{
    /// <summary>
    /// 所需的权限名称
    /// </summary>
    public string Permission { get; }

    /// <summary>
    /// 是否允许内部服务绕过权限检查
    /// </summary>
    public bool AllowInternal { get; set; } = true;

    /// <summary>
    /// 是否允许系统调用绕过权限检查
    /// </summary>
    public bool AllowSystem { get; set; } = true;

    public RequirePermissionAttribute(string permission)
    {
        Permission = permission;
    }
}

/// <summary>
/// 角色验证特性 - 标记在方法上，要求调用者具有指定角色
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class RequireRoleAttribute : Attribute
{
    /// <summary>
    /// 所需的角色名称
    /// </summary>
    public string Role { get; }

    /// <summary>
    /// 是否允许内部服务绕过角色检查
    /// </summary>
    public bool AllowInternal { get; set; } = true;

    /// <summary>
    /// 是否允许系统调用绕过角色检查
    /// </summary>
    public bool AllowSystem { get; set; } = true;

    public RequireRoleAttribute(string role)
    {
        Role = role;
    }
}

/// <summary>
/// 仅内部服务可调用 - 标记在方法上，禁止外部用户调用。
/// </summary>
/// <remarks>
/// 对应「契约即接口·HubActor 统一模型」§4.2 声明式注解中的 <c>[Internal]</c>：
/// 该方法仅服务端（内部服务）可调用，客户端不可见。运行时由 <see cref="PermissionValidator"/>
/// 强制——仅当调用来源为内部服务（<c>CallSourceType.InternalService</c>）时放行。
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public class InternalAttribute : Attribute
{
}

/// <summary>
/// 仅外部用户可调用 - 标记在方法上，禁止内部服务调用
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ExternalOnlyAttribute : Attribute
{
}
