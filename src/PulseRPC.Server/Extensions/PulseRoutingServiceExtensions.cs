using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseRPC.Clustering;
using PulseRPC.Routing;
using PulseRPC.Server.Routing;
using PulseRPC.Server.Services;

namespace PulseRPC.Server.Extensions;

/// <summary>
/// 统一出站路由（<see cref="IPulseRouter"/>）与跨节点扩散（<see cref="IPulseBackplane"/>）的 DI 扩展方法。
/// </summary>
/// <remarks>
/// 对应《统一 IPulseHub 全链路寻址与集群架构设计》§P3：单节点默认注册 <see cref="InProcessBackplane"/>
/// （无跨节点扩散，等价现状）与 <see cref="LocalPulseRouter"/>（把 <see cref="PulseAddress"/> 直接解析为本地投递）。
/// 集群部署可在调用本方法之前/之后用集群实现覆盖 <see cref="IPulseBackplane"/> / <see cref="IPulseRouter"/>
/// （见路线图 P4+）。
/// </remarks>
public static class PulseRoutingServiceExtensions
{
    /// <summary>
    /// 注册统一路由基础设施：<see cref="IPulseBackplane"/>（单节点默认 <see cref="InProcessBackplane"/>）与
    /// <see cref="IPulseRouter"/>（单节点默认 <see cref="LocalPulseRouter"/>）。
    /// </summary>
    /// <remarks>
    /// 依赖 <see cref="IServerChannelManager"/>，应在其注册之后调用（<see cref="PulseServerServiceCollectionExtensions"/>
    /// 已在内部依赖注册流程中于正确的顺序调用本方法，通常无需业务代码手动调用）。
    /// 同时以 <c>TryAddSingleton</c> 幂等注册 <see cref="IGroupManager"/> / <see cref="IUserConnectionMapping"/>
    /// 的默认实现（若业务代码已通过 <c>AddPulseReceiverServices</c> 注册自定义实现，则保留业务注册）。
    /// </remarks>
    public static IServiceCollection AddPulseRouting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Group/User 寻址（PulseAddress.Group/User）依赖这两个管理器；未显式调用 AddPulseReceiverServices 时提供默认实现，
        // 确保 IPulseRouter 在任何注册顺序下都具备完整的本地寻址能力。
        services.TryAddSingleton<IUserConnectionMapping, UserConnectionMapping>();
        services.TryAddSingleton<IGroupManager, GroupManager>();

        services.TryAddSingleton<IPulseBackplane, InProcessBackplane>();
        // 精确一次投递去重集与重试策略需要跨 LocalPulseRouter/ClusterInternalHub（P6）共享同一实例，
        // 否则本地直投与跨节点转发对同一 (Hub,Key) Actor 的去重判定会互不可见。
        services.TryAddSingleton<MessageDeduplicationCache>();
        services.TryAddSingleton<DeliveryRetryOptions>();
        services.TryAddSingleton<IPulseRouter, LocalPulseRouter>();

        return services;
    }
}
