using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PulseRPC.Clustering;

namespace PulseRPC.Infrastructure.Discovery;

/// <summary>
/// 把静态成员（<c>StaticClusterMembership</c>/<c>StaticNodeEndpointResolver</c>）替换为服务发现驱动的
/// <see cref="DiscoveryClusterMembership"/>，供各后端（Consul/Etcd/Kubernetes）的 DI 扩展复用（§P8）。
/// </summary>
public static class DiscoveryServiceCollectionExtensions
{
    /// <summary>
    /// 注册服务发现驱动的集群成员视图：以 <see cref="DiscoveryClusterMembership"/> 覆盖
    /// <see cref="IClusterMembership"/> 与 <see cref="INodeEndpointResolver"/> 的默认（静态）注册，
    /// 并将其登记为 <see cref="IHostedService"/> 以驱动自注册/后台刷新/优雅下线。
    /// </summary>
    /// <remarks>
    /// 具体后端应先注册自己的 <see cref="IDiscoveryProvider"/> 实现（及其 <see cref="DiscoveryOptions"/>），
    /// 再调用本方法完成通用装配。应在 <c>AddPulseClustering</c> 之后调用，以覆盖其静态默认注册。
    /// </remarks>
    public static IServiceCollection AddPulseDiscovery(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 单例承载：同一 DiscoveryClusterMembership 实例同时充当成员视图、端点解析器与托管服务。
        services.TryAddSingleton<DiscoveryClusterMembership>();

        services.RemoveAll<IClusterMembership>();
        services.AddSingleton<IClusterMembership>(sp => sp.GetRequiredService<DiscoveryClusterMembership>());

        services.RemoveAll<INodeEndpointResolver>();
        services.AddSingleton<INodeEndpointResolver>(sp => sp.GetRequiredService<DiscoveryClusterMembership>());

        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<DiscoveryClusterMembership>());

        return services;
    }
}
