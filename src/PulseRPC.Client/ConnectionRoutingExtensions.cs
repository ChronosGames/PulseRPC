using PulseRPC.Client.Configuration;

namespace PulseRPC.Client;

/// <summary>
/// Routes service requests with per-call load-balancing context.
/// </summary>
public static class ConnectionRoutingExtensions
{
    /// <summary>
    /// Routes to a service using <see cref="ServiceProxyOptions.LoadBalancingHint"/> and
    /// <see cref="ServiceProxyOptions.StickyKey"/>.
    /// </summary>
    public static Task<IClientChannel?> RouteWithOptionsAsync(
        this IConnectionManager connectionManager,
        string serviceName,
        ServiceProxyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (connectionManager == null)
        {
            throw new ArgumentNullException(nameof(connectionManager));
        }

        ValidateSupportedOptions(options);

        var context = new LoadBalancingContext(
            options?.LoadBalancingHint ?? LoadBalancingHint.None,
            options?.StickyKey);
        if (connectionManager is IContextualConnectionManager contextualConnectionManager)
        {
            return contextualConnectionManager.RouteAsync(serviceName, context, cancellationToken);
        }

        if (context.Hint != LoadBalancingHint.None || !string.IsNullOrWhiteSpace(context.StickyKey))
        {
            throw new NotSupportedException(
                $"Connection manager '{connectionManager.GetType().FullName}' does not support contextual load balancing.");
        }

        return connectionManager.RouteAsync(serviceName, cancellationToken);
    }

    private static void ValidateSupportedOptions(ServiceProxyOptions? options)
    {
        if (options == null)
        {
            return;
        }

#pragma warning disable CS0618 // Validate shipped compatibility fields before rejecting them.
        if (options.ConnectionId != null ||
            options.ChannelName != null ||
            options.Tags != null ||
            options.PreferredRegion != null ||
            options.Timeout.HasValue ||
            options.RetryPolicy != null ||
            !options.UseCache)
        {
            throw new NotSupportedException(
                "Only ServiceProxyOptions.LoadBalancingHint and StickyKey are consumed by client routing.");
        }
#pragma warning restore CS0618
    }
}
