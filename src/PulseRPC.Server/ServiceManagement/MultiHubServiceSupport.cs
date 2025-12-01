using PulseRPC.Server.Abstractions;

namespace PulseRPC.Server.ServiceManagement;

/// <summary>
/// 多 Hub 服务支持 - 代码组织指南
/// </summary>
/// <remarks>
/// <para>
/// <strong>架构说明</strong>：
/// </para>
/// <list type="bullet">
/// <item><description>IPulseHub - 无状态的 RPC 接口契约</description></item>
/// <item><description>IUnifiedPulseService - 有状态的服务实例</description></item>
/// <item><description>Hub 与 Service 分离，Hub 通过 IServiceAccessor&lt;T&gt; 访问 Service</description></item>
/// </list>
/// <para>
/// <strong>推荐的文件组织方式</strong>：
/// </para>
/// <code>
/// Services/
/// ├── Player/
/// │   ├── PlayerService.cs              // 有状态服务
/// │   ├── PlayerService.Player.cs       // partial: IPlayerHub 业务逻辑
/// │   ├── PlayerHub.cs                  // 无状态 Hub（RPC 入口）
/// │   └── InventoryHub.cs               // 无状态 Hub（RPC 入口）
/// └── Contracts/
///     ├── IPlayerHub.cs                 // Hub 接口定义
///     └── IInventoryHub.cs
/// </code>
/// <para>
/// <strong>调度行为</strong>：
/// Hub 通过 IServiceAccessor&lt;PlayerService&gt; 获取 Service 实例，
/// 并通过 ExecuteAsync 在 Service 队列中执行操作。
/// </para>
/// </remarks>
public static class MultiHubServiceSupport
{
    // 此类仅用于文档目的
    // Hub 与 Service 分离后，不需要注册 Hub 到 Service 的映射
    // Hub 通过 IServiceAccessor<TService> 直接访问 Service
}

/// <summary>
/// 服务接口分组标记 - 用于标记 partial class 中的接口分组
/// </summary>
/// <remarks>
/// 仅用于文档和代码组织目的，不影响运行时行为。
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ImplementsHubAttribute : Attribute
{
    /// <summary>
    /// Hub 接口类型
    /// </summary>
    public Type HubType { get; }

    /// <summary>
    /// 文件名后缀（用于 partial class 分割）
    /// </summary>
    public string? FileSuffix { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    public ImplementsHubAttribute(Type hubType)
    {
        if (!typeof(IPulseHub).IsAssignableFrom(hubType))
            throw new ArgumentException($"{hubType.Name} must implement IPulseHub");

        HubType = hubType;
    }
}
