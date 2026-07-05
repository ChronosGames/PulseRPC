using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PulseRPC.Clustering;
using PulseRPC.Routing;
using PulseRPC.Server.Clustering;
using PulseRPC.Server.Routing;

namespace PulseRPC.Server.Extensions;

/// <summary>
/// 节点↔节点集群能力（P4）的 DI 装配：静态成员拓扑 + 一致性哈希 +
/// L2 租约 <see cref="IActorDirectory"/> + 共享密钥 <see cref="INodeAuthenticator"/>，
/// 并把 <see cref="IPulseRouter"/> 从单节点 <see cref="LocalPulseRouter"/> 升级为
/// 集群感知的 <see cref="ClusterPulseRouter"/>。
/// </summary>
public static class PulseClusteringServiceExtensions
{
    /// <summary>
    /// 注册集群能力。调用方需通过 <paramref name="configureTopology"/> 提供本节点标识与全部静态成员端点，
    /// 通过 <paramref name="configureAuthenticator"/> 提供集群共享密钥。
    /// </summary>
    /// <remarks>
    /// 应在 <c>AddPulseServer</c>（进而 <c>AddPulseRouting</c>）之后调用：本方法以 <c>TryAddSingleton</c>
    /// 幂等注册 <c>AddPulseRouting</c> 的前置依赖，并用 <c>ClusterPulseRouter</c> 覆盖 <see cref="IPulseRouter"/>
    /// 的默认单节点注册。
    /// </remarks>
    public static IServiceCollection AddPulseClustering(
        this IServiceCollection services,
        Action<ClusterTopologyOptions> configureTopology,
        Action<SharedSecretNodeAuthenticatorOptions> configureAuthenticator,
        Action<LeaseActorDirectoryOptions>? configureLeaseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureTopology);
        ArgumentNullException.ThrowIfNull(configureAuthenticator);

        // 确保单节点路由基础设施（IPulseBackplane / IPulseRouter 的默认注册及其依赖）已就位，
        // 下方会用集群实现覆盖 IPulseRouter。
        services.AddPulseRouting();

        services.Configure(configureTopology);
        services.Configure(configureAuthenticator);
        services.Configure<LeaseActorDirectoryOptions>(configureLeaseDirectory ?? (_ => { }));

        // P8：集群成员视图（静态成员 + P7 故障接管所需的存活集健康管理）。
        // 作为 IClusterMembership 的默认实现；动态发现（Consul/Etcd/K8s）可通过替换本注册接入。
        services.TryAddSingleton<StaticClusterMembership>();
        services.TryAddSingleton<IClusterMembership>(sp => sp.GetRequiredService<StaticClusterMembership>());

        // 一致性哈希环以「当前存活成员」为准构建（P7）：故障节点被移出存活集后，环随之重建，
        // 其原本拥有的 (Hub, Key) 重新映射到存活节点。ClusterPulseRouter 订阅 IClusterMembership.Changed 完成重建。
        services.TryAddSingleton(sp =>
        {
            var membership = sp.GetRequiredService<IClusterMembership>();
            var live = membership.LiveNodeIds;
            if (live.Count > 0)
            {
                return new NodeConsistentHashRing(live);
            }

            var topology = sp.GetRequiredService<IOptions<ClusterTopologyOptions>>().Value;
            return new NodeConsistentHashRing(topology.Members.Select(m => m.NodeId));
        });

        services.TryAddSingleton<INodeAuthenticator, SharedSecretNodeAuthenticator>();
        services.TryAddSingleton<INodeEndpointResolver, StaticNodeEndpointResolver>();
        services.TryAddSingleton<IActorDirectory, LeaseActorDirectory>();
        services.TryAddSingleton<INodeLink, UnsupportedNodeLink>();
        services.TryAddSingleton<IClusterInternalHub, ClusterInternalHub>();

        // ClusterPulseRouter 内部持有 LocalPulseRouter 做本地投递；覆盖 IPulseRouter 的默认单节点注册。
        services.TryAddSingleton<LocalPulseRouter>();
        services.RemoveAll<IPulseRouter>();
        services.AddSingleton<IPulseRouter>(sp => new ClusterPulseRouter(
            sp.GetRequiredService<LocalPulseRouter>(),
            sp.GetRequiredService<NodeConsistentHashRing>(),
            sp.GetRequiredService<IActorDirectory>(),
            sp.GetRequiredService<INodeLink>(),
            sp.GetRequiredService<IPulseBackplane>(),
            sp.GetRequiredService<IOptions<ClusterTopologyOptions>>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ClusterPulseRouter>>(),
            sp.GetService<DeliveryRetryOptions>(),
            sp.GetRequiredService<IClusterMembership>()));

        return services;
    }

    /// <summary>
    /// 切换节点互信鉴权为基于 X.509 证书的生产级实现（<see cref="CertificateNodeAuthenticator"/>），
    /// 覆盖 <see cref="AddPulseClustering"/> 默认注册的共享密钥鉴权（§P8）。
    /// </summary>
    /// <remarks>应在 <see cref="AddPulseClustering"/> 之后调用。</remarks>
    public static IServiceCollection UseCertificateNodeAuthentication(
        this IServiceCollection services,
        Action<CertificateNodeAuthenticatorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.RemoveAll<INodeAuthenticator>();
        services.AddSingleton<INodeAuthenticator, CertificateNodeAuthenticator>();

        return services;
    }
}
