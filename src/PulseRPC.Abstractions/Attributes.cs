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
/// 预定义的角色类型常量 - 用于基于角色的访问控制
/// </summary>
/// <remarks>
/// 提供常用的角色类型常量，但不限制用户自定义角色类型。
/// 用户可以在 AuthorizeAttribute 中使用这些预定义常量，也可以定义自己的角色字符串。
/// </remarks>
public static class RoleTypes
{
    /// <summary>外部角色 - 来自客户端的普通用户/玩家</summary>
    /// <remarks>通常用于标记客户端调用的服务接口</remarks>
    public const string External = "External";

    /// <summary>内部角色 - 服务器之间的内部调用</summary>
    /// <remarks>用于服务间通信，如 GameServer 调用 BattleServer</remarks>
    public const string Internal = "Internal";

    /// <summary>GM角色 - 游戏管理员或运维人员</summary>
    /// <remarks>具有特殊权限的管理人员，可以执行管理操作</remarks>
    public const string GM = "GM";

    /// <summary>系统角色 - 系统级别的调用</summary>
    /// <remarks>最高权限，用于系统内部的关键操作</remarks>
    public const string System = "System";

    /// <summary>匿名角色 - 无需认证的公开访问</summary>
    /// <remarks>用于标记可以公开访问的接口，不需要任何认证</remarks>
    public const string Anonymous = "Anonymous";
}

/// <summary>
/// 授权特性，标记需要认证的方法或类
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false)]
public class AuthorizeAttribute : Attribute
{
    /// <summary>
    /// 授权策略（可选）
    /// </summary>
    public string? Policy { get; set; }

    /// <summary>
    /// 权限范围（可选）
    /// </summary>
    public string[]? Scopes { get; set; }

    /// <summary>
    /// 角色类型（基于角色的访问控制）
    /// </summary>
    /// <remarks>
    /// 使用 <see cref="RoleTypes"/> 中的预定义常量，也可以使用自定义的角色字符串。
    /// </remarks>
    /// <example>
    /// <code>
    /// // 使用预定义角色
    /// [Authorize(Role = RoleTypes.External)]
    ///
    /// // 使用自定义角色
    /// [Authorize(Role = "CustomAdminRole")]
    /// </code>
    /// </example>
    public string? Role { get; set; }

    /// <summary>
    /// 创建授权特性
    /// </summary>
    public AuthorizeAttribute()
    {
    }

    /// <summary>
    /// 创建授权特性，指定角色
    /// </summary>
    /// <param name="role">角色类型</param>
    public AuthorizeAttribute(string role)
    {
        Role = role;
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

/// <summary>
/// 标记接口需要生成 SmartHandler（高级事件处理器）
/// </summary>
/// <remarks>
/// 默认情况下，SourceGenerator 仅生成轻量级的 Dispatcher。
/// 添加此特性后，将额外生成 SmartHandler，提供以下高级功能：
/// - 批量事件处理
/// - 性能监控指标
/// - 断路器模式
/// - 智能订阅管理
/// </remarks>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
public sealed class GenerateSmartHandlerAttribute : Attribute
{
    /// <summary>
    /// 是否启用批量处理，默认为 false
    /// </summary>
    public bool EnableBatchProcessing { get; set; } = false;

    /// <summary>
    /// 是否启用性能监控，默认为 true
    /// </summary>
    public bool EnablePerformanceMonitoring { get; set; } = true;

    /// <summary>
    /// 是否启用断路器，默认为 false
    /// </summary>
    public bool EnableCircuitBreaker { get; set; } = false;
}

