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
/// 仅内部服务可调用 - 标记在方法上，禁止外部用户调用
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class InternalOnlyAttribute : Attribute
{
}

/// <summary>
/// 仅外部用户可调用 - 标记在方法上，禁止内部服务调用
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ExternalOnlyAttribute : Attribute
{
}
