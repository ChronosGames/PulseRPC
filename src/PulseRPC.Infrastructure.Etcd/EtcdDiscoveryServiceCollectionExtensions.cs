using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseRPC.Infrastructure.Discovery;

namespace PulseRPC.Infrastructure.Etcd;

/// <summary>
/// 注册 etcd 服务发现，把集群成员/端点解析从静态成员切换为 etcd 驱动的动态发现（§P8）。
/// </summary>
public static class EtcdDiscoveryServiceCollectionExtensions
{
    /// <summary>
    /// 启用 etcd 服务发现。应在 <c>AddPulseClustering(...)</c> 之后调用，以覆盖其静态成员/端点默认注册。
    /// </summary>
    /// <remarks>
    /// <paramref name="localNodeId"/> 必须与 <c>AddPulseClustering</c> 中配置的
    /// <c>ClusterTopologyOptions.LocalNodeId</c> 保持一致。动态发现下 <c>ClusterTopologyOptions.Members</c> 可留空。
    /// </remarks>
    public static IServiceCollection AddEtcdDiscovery(
        this IServiceCollection services,
        string localNodeId,
        string advertiseHost,
        int advertisePort,
        Action<EtcdDiscoveryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(localNodeId);
        ArgumentException.ThrowIfNullOrEmpty(advertiseHost);

        services.Configure<EtcdDiscoveryOptions>(configure ?? (_ => { }));
        services.Configure<DiscoveryOptions>(o =>
        {
            o.LocalNodeId = localNodeId;
            o.AdvertiseHost = advertiseHost;
            o.AdvertisePort = advertisePort;
        });

        services.TryAddSingleton<IDiscoveryProvider, EtcdDiscoveryProvider>();
        services.AddPulseDiscovery();

        return services;
    }
}
