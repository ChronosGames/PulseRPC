namespace PulseServiceDiscovery.Kubernetes;

public record KubernetesOptions
{
    public string Namespace { get; init; } = "default";
    public string ServiceLabel { get; init; } = "pulse-service-discovery/service";
    public string InstanceLabel { get; init; } = "pulse-service-discovery/instance";
    public KubernetesConnectionOptions Connection { get; init; } = new();
}

public record KubernetesConnectionOptions
{
    public string? ConfigFile { get; init; }
    public bool UseInClusterConfig { get; init; } = true;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}
