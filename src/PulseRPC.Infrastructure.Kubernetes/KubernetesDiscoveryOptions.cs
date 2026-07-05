namespace PulseRPC.Infrastructure.Kubernetes;

/// <summary>
/// Kubernetes 服务发现后端配置。
/// </summary>
public sealed class KubernetesDiscoveryOptions
{
    /// <summary>要列举/监听的命名空间。留空则使用 in-cluster 的当前命名空间。</summary>
    public string? Namespace { get; set; }

    /// <summary>Pod 标签选择器（如 <c>app=game-server</c>），用于筛选属于本集群的成员 Pod。</summary>
    public string LabelSelector { get; set; } = string.Empty;

    /// <summary>成员 Pod 暴露的 PulseRPC 服务端口（所有成员 Pod 一致）。</summary>
    public int NodePort { get; set; }

    /// <summary>
    /// 是否在集群内运行（使用 ServiceAccount 的 in-cluster 配置）。默认 true；
    /// 为 false 时使用 <see cref="KubeConfigPath"/> 指向的 kubeconfig。
    /// </summary>
    public bool UseInClusterConfig { get; set; } = true;

    /// <summary>非 in-cluster 时使用的 kubeconfig 文件路径（留空则用默认位置）。</summary>
    public string? KubeConfigPath { get; set; }

    /// <summary>是否启用 Pod watch 以加速成员变更收敛。默认 true。</summary>
    public bool EnableWatch { get; set; } = true;
}
