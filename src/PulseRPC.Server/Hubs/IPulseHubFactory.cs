using System.Diagnostics.CodeAnalysis;
using PulseRPC.Server.Services;

namespace PulseRPC.Server.Hubs;

/// <summary>
/// Deprecated cache that binds one Hub instance to one legacy ServiceFactory instance.
/// </summary>
/// <typeparam name="THub">Cached Hub type.</typeparam>
/// <typeparam name="TService">Legacy stateful service type.</typeparam>
/// <remarks>
/// Keep Hubs stateless and register them through standard DI. Move keyed state into a
/// Pulse Service registered with <c>AddPulseService&lt;TService&gt;</c>, then inject
/// <see cref="IServiceAccessor{TService}"/> into the Hub.
/// </remarks>
[Obsolete("Use a stateless Hub registered with standard DI and inject IServiceAccessor<TService>.", false)]
public interface IPulseHubFactory<THub, TService>
    where THub : class
    where TService : IPulseService
{
    /// <summary>Gets or creates a cached compatibility Hub.</summary>
    /// <param name="serviceId">Legacy full service id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The compatibility Hub instance.</returns>
    ValueTask<THub> GetOrCreateAsync(
        string serviceId,
        CancellationToken cancellationToken = default);

    /// <summary>Tries to read an existing compatibility Hub.</summary>
    /// <param name="serviceId">Legacy full service id.</param>
    /// <param name="hub">Existing Hub when found.</param>
    /// <returns><see langword="true"/> when found.</returns>
    bool TryGet(string serviceId, [NotNullWhen(true)] out THub? hub);

    /// <summary>Removes a compatibility Hub and its parallel ServiceFactory instance.</summary>
    /// <param name="serviceId">Legacy full service id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when removed.</returns>
    ValueTask<bool> RemoveAsync(
        string serviceId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a snapshot of legacy service ids.</summary>
    IReadOnlyCollection<string> GetActiveServiceIds();

    /// <summary>Gets the number of cached compatibility Hubs.</summary>
    int ActiveCount { get; }
}
