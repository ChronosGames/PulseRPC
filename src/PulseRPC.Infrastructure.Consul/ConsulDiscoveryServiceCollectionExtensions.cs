using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseRPC.Infrastructure.Discovery;

namespace PulseRPC.Infrastructure.Consul;

/// <summary>
/// 注册 Consul 服务发现，把集群成员/端点解析从静态成员切换为 Consul 驱动的动态发现（§P8）。
/// </summary>
public static class ConsulDiscoveryServiceCollectionExtensions
{
    /// <summary>
    /// 启用 Consul 服务发现。应在 <c>AddPulseClustering(...)</c> 之后调用，以覆盖其静态成员/端点默认注册。
    /// </summary>
    /// <remarks>
    /// <paramref name="localNodeId"/> 必须与 <c>AddPulseClustering</c> 中配置的
    /// <c>ClusterTopologyOptions.LocalNodeId</c> 保持一致（集群路由器据后者识别"本节点"）。
    /// 动态发现下 <c>ClusterTopologyOptions.Members</c> 可留空——端点由 Consul 提供。
    /// </remarks>
    /// <param name="services">服务集合。</param>
    /// <param name="localNodeId">本节点标识。</param>
    /// <param name="advertiseHost">本节点对外公布主机名/IP。</param>
    /// <param name="advertisePort">本节点对外公布端口。</param>
    /// <param name="configure">Consul 后端配置（地址、服务名、健康检查等）。</param>
    public static IServiceCollection AddConsulDiscovery(
        this IServiceCollection services,
        string localNodeId,
        string advertiseHost,
        int advertisePort,
        Action<ConsulDiscoveryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(localNodeId);
        ArgumentException.ThrowIfNullOrEmpty(advertiseHost);

        services.Configure<ConsulDiscoveryOptions>(configure ?? (_ => { }));
        services.Configure<DiscoveryOptions>(o =>
        {
            o.LocalNodeId = localNodeId;
            o.AdvertiseHost = advertiseHost;
            o.AdvertisePort = advertisePort;
        });

        services.TryAddSingleton<IDiscoveryProvider, ConsulDiscoveryProvider>();
        services.AddPulseDiscovery();

        return services;
    }
}
