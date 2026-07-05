using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseRPC.Infrastructure.Discovery;

namespace PulseRPC.Infrastructure.Kubernetes;

/// <summary>
/// 注册 Kubernetes 服务发现，把集群成员/端点解析从静态成员切换为 K8s Pod 驱动的动态发现（§P8）。
/// </summary>
public static class KubernetesDiscoveryServiceCollectionExtensions
{
    /// <summary>
    /// 启用 Kubernetes 服务发现。应在 <c>AddPulseClustering(...)</c> 之后调用，以覆盖其静态成员/端点默认注册。
    /// </summary>
    /// <remarks>
    /// <paramref name="localNodeId"/> 通常应设为本 Pod 名（来自 downward API 的 <c>HOSTNAME</c>/<c>metadata.name</c>），
    /// 并与 <c>AddPulseClustering</c> 中的 <c>ClusterTopologyOptions.LocalNodeId</c> 一致。
    /// K8s 下 <paramref name="advertiseHost"/> 一般无需精确（成员端点由各 Pod 的 PodIP 提供），
    /// 但仍需传本 Pod 可达地址以满足通用装配。
    /// </remarks>
    public static IServiceCollection AddKubernetesDiscovery(
        this IServiceCollection services,
        string localNodeId,
        string advertiseHost,
        int advertisePort,
        Action<KubernetesDiscoveryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(localNodeId);
        ArgumentException.ThrowIfNullOrEmpty(advertiseHost);

        services.Configure<KubernetesDiscoveryOptions>(o =>
        {
            // 默认成员端口与本节点公布端口一致（同一 Deployment/StatefulSet 的 Pod 通常端口相同）。
            o.NodePort = advertisePort;
            configure?.Invoke(o);
        });
        services.Configure<DiscoveryOptions>(o =>
        {
            o.LocalNodeId = localNodeId;
            o.AdvertiseHost = advertiseHost;
            o.AdvertisePort = advertisePort;
        });

        services.TryAddSingleton<IDiscoveryProvider, KubernetesDiscoveryProvider>();
        services.AddPulseDiscovery();

        return services;
    }
}
