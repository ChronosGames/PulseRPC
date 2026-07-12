namespace PulseRPC.Client.Configuration;

/// <summary>
/// Provides the current routing weight for a connection.
/// </summary>
/// <remarks>
/// The load balancer calls this provider for every weighted selection, so implementations may return
/// a live capacity or health-derived snapshot. The provider must be thread-safe and return a value greater than zero.
/// </remarks>
public interface IConnectionWeightProvider
{
    /// <summary>Returns the current positive weight for <paramref name="connection"/>.</summary>
    int GetWeight(IClientChannel connection);
}

/// <summary>
/// Strongly typed settings for connection load balancing.
/// </summary>
public sealed class ConnectionLoadBalancingOptions
{
    /// <summary>
    /// Dynamic weight source used by <see cref="LoadBalancingStrategy.WeightedRoundRobin"/>.
    /// When omitted, <see cref="ConnectionDescriptor.Weight"/> is read on every selection.
    /// </summary>
    public IConnectionWeightProvider? WeightProvider { get; set; }

    /// <summary>
    /// Number of deterministic virtual nodes per healthy connection in the consistent hash ring.
    /// </summary>
    public int ConsistentHashVirtualNodes { get; set; } = 128;
}

/// <summary>
/// Per-selection routing data for contextual load balancing.
/// </summary>
public readonly struct LoadBalancingContext
{
    /// <summary>Creates a routing context.</summary>
    public LoadBalancingContext(
        LoadBalancingHint hint = LoadBalancingHint.None,
        string? stickyKey = null)
    {
        Hint = hint;
        StickyKey = stickyKey;
    }

    /// <summary>Optional strategy hint.</summary>
    public LoadBalancingHint Hint { get; }

    /// <summary>
    /// Stable application key used by consistent hashing, such as a user, tenant, or session identifier.
    /// </summary>
    public string? StickyKey { get; }
}

internal sealed class DescriptorConnectionWeightProvider : IConnectionWeightProvider
{
    public static DescriptorConnectionWeightProvider Instance { get; } = new();

    public int GetWeight(IClientChannel connection) => connection.Descriptor.Weight;
}
