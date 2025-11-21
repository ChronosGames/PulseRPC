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
    /// 角色类型（基于角色的访问控制）
    /// </summary>
    /// <remarks>
    /// 当指定 Role 时，会基于角色类型进行访问控制。
    /// 这是比 AuthType 更细粒度的权限控制方式。
    /// 可以使用 <see cref="RoleTypes"/> 中的预定义常量，也可以使用自定义的角色字符串。
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
    /// 创建授权特性，指定认证类型
    /// </summary>
    /// <param name="authType">认证类型</param>
    public AuthorizeAttribute(AuthType authType)
    {
        AuthType = authType;
    }

    /// <summary>
    /// 创建授权特性，指定所需角色（逗号分隔的角色列表）
    /// </summary>
    /// <param name="roles">所需的角色，多个角色用逗号分隔</param>
    /// <remarks>
    /// 此构造函数用于传统的基于角色名称列表的认证（Roles 属性）。
    /// 若要使用新的 Role 属性（细粒度角色类型），请使用属性初始化器：
    /// <code>[Authorize(Role = RoleTypes.External)]</code>
    /// </remarks>
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

/// <summary>
/// 标记 PulseHub 接口，用于 Provider 驱动的全双工通信
/// </summary>
/// <remarks>
/// 通过指定 Provider 字段，Source Generator 可以根据当前进程的 Provider 列表
/// 决定生成 Proxy（调用远端）或 Dispatcher/Stub（本地实现）。
/// </remarks>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
public sealed class PulseHubAttribute : Attribute
{
    /// <summary>
    /// 指定该 Hub 在当前进程承担的 Provider 角色名称
    /// （如 "GameServer"、"UnityClient"、"ServerPush"）
    /// </summary>
    /// <remarks>
    /// 当连接建立时，双方会交换 Provider 列表。
    /// 如果对端提供了该 Provider，本地会生成 Proxy 用于调用；
    /// 如果本地提供了该 Provider，会生成 Dispatcher/Stub 用于处理请求。
    /// </remarks>
    public string? Provider { get; set; }
}

/// <summary>
/// 调度策略枚举
/// </summary>
public enum DispatchStrategy
{
    /// <summary>
    /// 默认：按 ConnectionId 哈希分配到固定数量的全局队列中。
    /// 适用于无状态或仅与连接相关的逻辑。
    /// </summary>
    ConnectionHash,

    /// <summary>
    /// 按认证后的 RoleType + ID 分配队列。
    /// 适用于玩家个人逻辑。需登录后才可调用。
    /// </summary>
    RoleBased,

    /// <summary>
    /// 按方法参数的特定字段分配队列。
    /// 适用于房间、地图、工会等共享逻辑。
    /// </summary>
    Argument
}

/// <summary>
/// 标记 PulseHub 方法的调度和权限配置
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PulseMethodAttribute : Attribute
{
    /// <summary>
    /// 调度策略，默认为 ConnectionHash
    /// </summary>
    public DispatchStrategy Strategy { get; set; } = DispatchStrategy.ConnectionHash;

    /// <summary>
    /// 当 Strategy 为 Argument 时，指定参与生成 Key 的参数名称列表。
    /// 这里的参数值组合将决定任务被投递到哪个 Fiber。
    /// 支持多实参组合（如 ["battleId", "zoneId"]）。
    /// </summary>
    /// <remarks>
    /// 最佳实践：将用于 KeySelector 的参数放在参数列表的最前面，
    /// 且使用基本数据类型（int, long, string, guid），以最大化 Dispatcher 解析 Key 的性能。
    /// </remarks>
    public string[]? KeySelectors { get; set; }

    /// <summary>
    /// 可选：在方法级别覆盖 Provider，允许同一 Hub 的不同方法由不同 Provider 提供
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// 需要的角色权限（如 ["User", "GM"]）
    /// </summary>
    public string[]? RequireRoles { get; set; }
}
