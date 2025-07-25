using System;

namespace PulseRPC;

/// <summary>
/// 指定使用的通道
/// </summary>
[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class ChannelAttribute : Attribute
{
    /// <summary>
    /// 通道名称
    /// </summary>
    public string ChannelName { get; }

    public ChannelAttribute(string channelName)
    {
        ChannelName = channelName;
    }
}

/// <summary>
/// 标记需要生成客户端代理的类
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class PulseClientGenerationAttribute : Attribute
{
    /// <summary>
    /// 要扫描的程序集中的任意类型
    /// </summary>
    public Type MarkerType { get; }

    /// <summary>
    /// 初始化 <see cref="PulseClientGenerationAttribute"/>
    /// </summary>
    /// <param name="markerType">要扫描的程序集中的任意类型</param>
    public PulseClientGenerationAttribute(Type markerType)
    {
        MarkerType = markerType ?? throw new ArgumentNullException(nameof(markerType));
    }

    /// <summary>
    /// 获取或设置方法返回类型，用于指定WithDeadline等方法的返回类型
    /// </summary>
    public Type? WithResultType { get; set; }
}

/// <summary>
/// 认证类型枚举（用于特性）
/// </summary>
public enum AuthType
{
    /// <summary>客户端认证</summary>
    Client,
    /// <summary>服务间认证</summary>
    Service,
    /// <summary>内部系统认证</summary>
    Internal,
    /// <summary>任何类型的认证</summary>
    Any
}

/// <summary>
/// 授权特性，标记需要认证的方法或类
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class AuthorizeAttribute : Attribute
{
    /// <summary>
    /// 认证类型，默认为客户端认证
    /// </summary>
    public AuthType AuthType { get; set; } = AuthType.Client;

    /// <summary>
    /// 所需的角色（可选）
    /// </summary>
    public string? Roles { get; set; }

    /// <summary>
    /// 授权策略（可选）
    /// </summary>
    public string? Policy { get; set; }

    /// <summary>
    /// 权限范围（用于服务间认证，可选）
    /// </summary>
    public string[]? Scopes { get; set; }

    /// <summary>
    /// 创建授权特性
    /// </summary>
    public AuthorizeAttribute()
    {
    }

    /// <summary>
    /// 创建授权特性，指定认证类型
    /// </summary>
    /// <param name="authType">认证类型</param>
    public AuthorizeAttribute(AuthType authType)
    {
        AuthType = authType;
    }

    /// <summary>
    /// 创建授权特性，指定所需角色
    /// </summary>
    /// <param name="roles">所需的角色，多个角色用逗号分隔</param>
    public AuthorizeAttribute(string roles)
    {
        Roles = roles;
    }

    /// <summary>
    /// 创建授权特性，指定认证类型和权限范围
    /// </summary>
    /// <param name="authType">认证类型</param>
    /// <param name="scopes">权限范围</param>
    public AuthorizeAttribute(AuthType authType, params string[] scopes)
    {
        AuthType = authType;
        Scopes = scopes;
    }
}

/// <summary>
/// 允许匿名访问特性，覆盖类级别的授权要求
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class AllowAnonymousAttribute : Attribute
{
}
