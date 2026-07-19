using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Clustering;
using PulseRPC.Routing;
using PulseRPC.Server.Clustering;
using PulseRPC.Server.Gateway;

namespace PulseRPC.Server.Extensions;

/// <summary>
/// Gateway 角色（P5）的 DI 装配：把当前节点注册为对外客户端桥接的 Gateway ——
/// <see cref="IGatewayFrontHub"/>（对外，客户端经此中转调用 Actor）+
/// <see cref="IGatewayRelayHub"/>（对内，供后端节点经 <c>INodeLink</c> 把推送/反向 Ask 投递给本节点上真实的客户端连接）。
/// </summary>
/// <remarks>
/// <para>
/// 必须在 <c>AddPulseClustering</c> 之后调用：Gateway 本身也是一个集群节点（需要 <c>IPulseRouter</c>/
/// <c>INodeLink</c> 才能把中转请求转发给拥有目标 Actor 实例的后端节点）。
/// </para>
/// <para>
/// 只有需要承担 Gateway 角色的节点才需要调用本方法；纯粹的后端节点调用 <c>AddPulseClustering</c> 即可
/// （它们仍可经 <see cref="IGatewayRelayHub"/> 被其它 Gateway 节点调用，只要引用了本程序集，
/// 该 Hub 骨架即由服务端源生成器生成，无需额外注册即可被动响应；本方法额外注册的是"业务实现" DI 绑定）。
/// </para>
/// </remarks>
public static class PulseGatewayServiceExtensions
{
    /// <summary>把当前节点注册为 Gateway：对外暴露 <see cref="IGatewayFrontHub"/>，对内暴露 <see cref="IGatewayRelayHub"/>。</summary>
    public static IServiceCollection AddPulseGateway(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IGatewayFrontHub>(serviceProvider => new GatewayFrontHub(
            serviceProvider.GetRequiredService<IPulseRouter>(),
            serviceProvider.GetRequiredService<IOptions<ClusterTopologyOptions>>(),
            serviceProvider.GetRequiredService<ILogger<GatewayFrontHub>>(),
            serviceProvider.GetService<IConnectionDirectory>(),
            serviceProvider.GetService<IServiceRoutingTable>(),
            serviceProvider.GetServices<IGatewayActorInvocationPolicy>()));
        services.TryAddSingleton<IGatewayRelayHub, GatewayRelayHub>();

        return services;
    }
}
