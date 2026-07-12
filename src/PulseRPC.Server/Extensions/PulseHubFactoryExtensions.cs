using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Hubs;
using PulseRPC.Server.Services;
using PulseRPC.Server.Services.Management;

namespace PulseRPC.Server.Extensions;

/// <summary>
/// Compatibility registration for the former one-Hub-per-service cache.
/// </summary>
/// <remarks>
/// New Hubs should be stateless standard-DI services and inject
/// <see cref="IServiceAccessor{TService}"/> for keyed state.
/// </remarks>
[Obsolete("Use a stateless Hub registered with standard DI and inject IServiceAccessor<TService> for stateful services.", false)]
public static class PulseHubFactoryExtensions
{
    /// <summary>Registers the deprecated Hub cache with a simple factory.</summary>
    /// <remarks>
    /// This method does not register a service lifecycle. Existing callers must already
    /// have the compatibility service factory; migrate both registrations together.
    /// </remarks>
    [Obsolete("Use a stateless Hub and inject IServiceAccessor<TService>.", false)]
    public static IServiceCollection AddPulseHubFactory<THub, TService>(
        this IServiceCollection services,
        Func<TService, THub> hubFactory)
        where THub : class
        where TService : IPulseService
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(hubFactory);

        services.TryAddSingleton<IPulseHubFactory<THub, TService>>(serviceProvider =>
            new PulseHubFactory<THub, TService>(
                serviceProvider.GetRequiredService<IPulseServiceFactory<TService>>(),
                hubFactory,
                serviceProvider.GetRequiredService<ILogger<PulseHubFactory<THub, TService>>>()));

        return services;
    }

    /// <summary>Registers the deprecated Hub cache with a DI-aware factory.</summary>
    [Obsolete("Use a stateless Hub and inject IServiceAccessor<TService>.", false)]
    public static IServiceCollection AddPulseHubFactory<THub, TService>(
        this IServiceCollection services,
        Func<IServiceProvider, TService, THub> hubFactory)
        where THub : class
        where TService : IPulseService
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(hubFactory);

        services.TryAddSingleton<IPulseHubFactory<THub, TService>>(serviceProvider =>
            new PulseHubFactory<THub, TService>(
                serviceProvider.GetRequiredService<IPulseServiceFactory<TService>>(),
                service => hubFactory(serviceProvider, service),
                serviceProvider.GetRequiredService<ILogger<PulseHubFactory<THub, TService>>>()));

        return services;
    }

    /// <summary>Registers the deprecated Hub cache using <see cref="ActivatorUtilities"/>.</summary>
    [Obsolete("Use a stateless Hub and inject IServiceAccessor<TService>.", false)]
    public static IServiceCollection AddPulseHubFactory<THub, TService>(
        this IServiceCollection services)
        where THub : class
        where TService : IPulseService
        => services.AddPulseHubFactory<THub, TService>(
            (serviceProvider, service) =>
                ActivatorUtilities.CreateInstance<THub>(serviceProvider, service));
}
