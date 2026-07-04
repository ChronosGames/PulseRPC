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
/// 标记方法为可重入（只读）方法，允许其绕过 Service 的串行邮箱以并发执行。
/// </summary>
/// <remarks>
/// 默认情况下，同一 Service 实例（Actor / DedicatedQueue 模式）内的所有方法调用都通过
/// 单消费者串行邮箱按 FIFO 顺序独占执行，以保证状态一致性。
/// <para>
/// 对于不修改状态的只读查询方法，可标注 <see cref="ReentrantAttribute"/>，此时：
/// </para>
/// <list type="bullet">
/// <item>被标注的方法（读者）之间可以并发执行；</item>
/// <item>未标注的方法（写者）仍然独占执行，且在其执行前会等待所有在途读者完成；</item>
/// <item>读者与写者永远不会重叠，写者按 FIFO 顺序执行且不会被读者饿死。</item>
/// </list>
/// <para>
/// 注意：仅在 Actor（DedicatedQueue）模式下提供读并发语义；DefaultPool 模式本身即并发，
/// ThreadAffinity 模式将其视为普通串行调用。标注的方法必须确保不修改共享状态，否则会破坏一致性。
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ReentrantAttribute : Attribute
{
}

/// <summary>
/// 标记接口（facet）或方法为「客户端可见」，即允许外部客户端直接调用。
/// </summary>
/// <remarks>
/// 这是一道独立于业务鉴权（<see cref="AuthorizeAttribute"/> / <c>IAuthorizationService</c>）的
/// 「协议框架」级可见性门闸：未标注 <see cref="ClientFacingAttribute"/>（或显式设为
/// <c>Enabled = false</c>）的方法，即使实现了完整的业务鉴权逻辑，也永远无法被外部客户端连接调用——
/// 源生成器在编译期把该方法排除出「客户端可调用白名单」，并在生成的路由代码中插入强制拒绝检查，
/// 这条检查位于所有协议号路由的唯一必经路径上，无法被业务代码绕过。
/// <para>
/// 标注位置与优先级（方法级标注覆盖接口/facet 级默认值）：
/// </para>
/// <list type="bullet">
/// <item><description>标注在接口（facet）上：整个 facet 的所有方法默认对客户端可见；</description></item>
/// <item><description>标注在方法上：覆盖 facet 级默认值。可用于从「客户端可见」的 facet 中排除个别方法
/// （<c>[ClientFacing(false)]</c>），或从「非客户端可见」的 facet 中单独放行个别方法
/// （<c>[ClientFacing]</c>）；</description></item>
/// <item><description>接口与方法均未标注：默认<b>不可见</b>（安全默认值 / 白名单语义，拒绝优先）。</description></item>
/// </list>
/// <para>
/// <b>启用方式</b>：本特性描述的白名单始终由源生成器计算并写入生成代码，但实际强制检查默认
/// <b>关闭</b>，以保证现有项目升级后行为不变；需要在服务端显式开启后才会生效
/// （参见 <c>PulseRPC.Server.Configuration.PulseServerOptions.EnableClientFacingGate</c>）。
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = false)]
public sealed class ClientFacingAttribute : Attribute
{
    /// <summary>
    /// 是否对客户端可见，默认 true。
    /// </summary>
    public bool Enabled { get; }

    /// <summary>
    /// 标注为客户端可见（或通过 <paramref name="enabled"/> = false 显式从上级默认值中排除）。
    /// </summary>
    /// <param name="enabled">是否允许外部客户端调用，默认为 true。</param>
    public ClientFacingAttribute(bool enabled = true)
    {
        Enabled = enabled;
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

