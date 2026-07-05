using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Clustering;
using PulseRPC.Infrastructure.Discovery;

namespace PulseRPC.Infrastructure.Kubernetes;

/// <summary>
/// 基于 Kubernetes API 的 <see cref="IDiscoveryProvider"/> 实现（§P8）。
/// </summary>
/// <remarks>
/// <para>
/// 与 Consul/etcd 不同，K8s 天然管理 Pod 生命周期，因此<strong>无需本进程主动注册/注销</strong>
/// （<see cref="RegisterAsync"/>/<see cref="DeregisterAsync"/> 为 no-op）——成员集合即"命名空间内匹配
/// <see cref="KubernetesDiscoveryOptions.LabelSelector"/> 且处于 Running 的 Pod"。节点标识取 Pod 名
/// （StatefulSet 下稳定，如 <c>game-0</c>；本节点的 <c>LocalNodeId</c> 应设为自身 Pod 名，通常来自
/// downward API 环境变量 <c>HOSTNAME</c>/<c>metadata.name</c>），端点取 <c>PodIP</c> +
/// <see cref="KubernetesDiscoveryOptions.NodePort"/>。
/// </para>
/// <para>
/// 启用 watch 时对匹配 Pod 做原生 watch，Pod 增删/就绪变化即时触发 <see cref="Changed"/>，否则由上层
/// <see cref="DiscoveryClusterMembership"/> 轮询兜底。
/// </para>
/// <para>
/// 说明：与真实 K8s API server 的交互无法离线单元测试；通用成员/端点/变更检测逻辑由
/// <see cref="DiscoveryClusterMembership"/> 的单元测试覆盖。
/// </para>
/// </remarks>
public sealed class KubernetesDiscoveryProvider : IDiscoveryProvider, IDisposable
{
    private const string RunningPhase = "Running";

    private readonly KubernetesDiscoveryOptions _options;
    private readonly ILogger<KubernetesDiscoveryProvider> _logger;
    private readonly IKubernetes _client;
    private readonly string _namespace;

    private CancellationTokenSource? _watchCts;
    private bool _disposed;

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>创建 Kubernetes 发现后端。</summary>
    public KubernetesDiscoveryProvider(IOptions<KubernetesDiscoveryOptions> options, ILogger<KubernetesDiscoveryProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var config = _options.UseInClusterConfig
            ? KubernetesClientConfiguration.InClusterConfig()
            : KubernetesClientConfiguration.BuildConfigFromConfigFile(_options.KubeConfigPath);

        _client = new k8s.Kubernetes(config);
        _namespace = !string.IsNullOrEmpty(_options.Namespace) ? _options.Namespace! : ResolveInClusterNamespace();
    }

    /// <summary>测试可见构造：注入自定义 <see cref="IKubernetes"/> 与命名空间。</summary>
    internal KubernetesDiscoveryProvider(IKubernetes client, string @namespace, KubernetesDiscoveryOptions options, ILogger<KubernetesDiscoveryProvider> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _namespace = @namespace ?? "default";
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static string ResolveInClusterNamespace()
    {
        const string path = "/var/run/secrets/kubernetes.io/serviceaccount/namespace";
        try
        {
            if (System.IO.File.Exists(path))
            {
                return System.IO.File.ReadAllText(path).Trim();
            }
        }
        catch
        {
            // 回退到 default。
        }

        return "default";
    }

    /// <inheritdoc/>
    public Task RegisterAsync(DiscoveredNode self, CancellationToken cancellationToken = default)
    {
        // K8s 管理 Pod 生命周期，成员即 Pod，无需主动注册。
        if (_options.EnableWatch)
        {
            _watchCts = new CancellationTokenSource();
            StartWatch(_watchCts.Token);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeregisterAsync(DiscoveredNode self, CancellationToken cancellationToken = default)
    {
        _watchCts?.Cancel();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DiscoveredNode>> FetchNodesAsync(CancellationToken cancellationToken = default)
    {
        var pods = await _client.CoreV1.ListNamespacedPodAsync(
            _namespace, labelSelector: _options.LabelSelector, cancellationToken: cancellationToken).ConfigureAwait(false);

        return MapPods(pods);
    }

    private List<DiscoveredNode> MapPods(V1PodList pods)
    {
        var nodes = new List<DiscoveredNode>();
        if (pods?.Items is null)
        {
            return nodes;
        }

        foreach (var pod in pods.Items)
        {
            var nodeId = pod.Metadata?.Name;
            var podIp = pod.Status?.PodIP;
            var phase = pod.Status?.Phase;

            if (string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(podIp)
                || !string.Equals(phase, RunningPhase, StringComparison.Ordinal))
            {
                continue;
            }

            nodes.Add(new DiscoveredNode(nodeId, new NodeEndpoint(podIp, _options.NodePort)));
        }

        return nodes;
    }

    private void StartWatch(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var response = _client.CoreV1.ListNamespacedPodWithHttpMessagesAsync(
                    _namespace, labelSelector: _options.LabelSelector, watch: true, cancellationToken: cancellationToken);

                await foreach (var (_, _) in response.WatchAsync<V1Pod, V1PodList>(cancellationToken: cancellationToken).ConfigureAwait(false))
                {
                    Changed?.Invoke();
                }
            }
            catch (OperationCanceledException)
            {
                // 正常停止。
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kubernetes Pod watch 中断，将由轮询兜底成员刷新");
            }
        }, CancellationToken.None);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watchCts?.Cancel();
        _watchCts?.Dispose();
        _client.Dispose();
    }
}
