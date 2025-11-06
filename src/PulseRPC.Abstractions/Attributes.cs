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

    /// <summary>
    /// 服务名称，用于线程调度（可选）
    /// 如果未指定，默认使用接口名称
    /// </summary>
    public string? ServiceName { get; set; }

    public ChannelAttribute(string channelName)
    {
        ChannelName = channelName;
    }

    /// <summary>
    /// 初始化通道特性并指定服务名称
    /// </summary>
    /// <param name="channelName">通道名称</param>
    /// <param name="serviceName">服务名称（用于线程调度）</param>
    public ChannelAttribute(string channelName, string? serviceName) : this(channelName)
    {
        ServiceName = serviceName;
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
/// 标记需要生成服务端代理的类
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class PulseServerGenerationAttribute : Attribute
{
    /// <summary>
    /// 要扫描的程序集中的任意类型
    /// </summary>
    public Type MarkerType { get; }

    /// <summary>
    /// 初始化 <see cref="PulseServerGenerationAttribute"/>
    /// </summary>
    /// <param name="markerType">要扫描的程序集中的任意类型</param>
    public PulseServerGenerationAttribute(Type markerType)
    {
        MarkerType = markerType ?? throw new ArgumentNullException(nameof(markerType));
    }

    /// <summary>
    /// 获取或设置优化级别
    /// </summary>
    public int OptimizationLevel { get; set; } = 1;

    /// <summary>
    /// 获取或设置是否启用性能监控代码生成
    /// </summary>
    public bool EnablePerformanceMonitoring { get; set; } = false;
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

/// <summary>
/// 消息优先级枚举（Service 方法级别）
/// </summary>
public enum MessagePriority : byte
{
    /// <summary>关键消息 - 最高优先级（如GM命令、紧急操作）</summary>
    Critical = 0,

    /// <summary>高优先级消息</summary>
    High = 1,

    /// <summary>普通优先级消息（默认）</summary>
    Normal = 2,

    /// <summary>低优先级消息</summary>
    Low = 3,

    /// <summary>批量处理消息 - 最低优先级</summary>
    Bulk = 4
}

/// <summary>
/// 优先级特性，标记方法的消息处理优先级
/// </summary>
/// <remarks>
/// 用于 Service 内部的方法优先级控制。同一 Service 实例内，高优先级消息优先处理。
/// 同优先级消息按 FIFO 顺序处理。
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class PriorityAttribute : Attribute
{
    /// <summary>
    /// 消息优先级，默认为 Normal
    /// </summary>
    public MessagePriority Priority { get; }

    /// <summary>
    /// 创建优先级特性
    /// </summary>
    /// <param name="priority">消息优先级</param>
    public PriorityAttribute(MessagePriority priority = MessagePriority.Normal)
    {
        Priority = priority;
    }
}
