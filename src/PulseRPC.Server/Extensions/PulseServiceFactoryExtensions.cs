using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Services;
using PulseRPC.Server.Services.Management;

namespace PulseRPC.Server.Extensions;

/// <summary>
/// Compatibility registration for the former per-service factory cache.
/// </summary>
/// <remarks>
/// New code should call <see cref="PulseServiceExtensions.AddPulseService{TService}"/>
/// and inject <see cref="IServiceAccessor{TService}"/>. This type remains only so
/// existing applications can migrate without an immediate binary break.
/// </remarks>
[Obsolete("Use AddPulseService<TService>() and inject IServiceAccessor<TService>. This compatibility path has a parallel lifecycle cache.", false)]
public static class PulseServiceFactoryExtensions
{
    /// <summary>
    /// Registers the deprecated factory cache with a custom instance factory.
    /// </summary>
    /// <typeparam name="TService">Legacy service implementation.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="serviceFactory">Legacy factory receiving the full service id.</param>
    /// <param name="configureOptions">Legacy cache options.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    /// Migration: replace this call with <c>AddPulseService&lt;TService&gt;</c>, pass only
    /// the business id to the service constructor, and resolve
    /// <c>IServiceAccessor&lt;TService&gt;</c> from DI.
    /// </remarks>
    [Obsolete("Use AddPulseService<TService>() and inject IServiceAccessor<TService>.", false)]
    public static IServiceCollection AddPulseServiceFactory<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, string, TService> serviceFactory,
        Action<PulseServiceFactoryOptions>? configureOptions = null)
        where TService : IPulseService
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceFactory);

        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        services.TryAddSingleton<IPulseServiceFactory<TService>>(serviceProvider =>
        {
            var options = new PulseServiceFactoryOptions();
            configureOptions?.Invoke(options);
            return new PulseServiceFactory<TService>(
                serviceId => serviceFactory(serviceProvider, serviceId),
                options,
                serviceProvider.GetRequiredService<ILogger<PulseServiceFactory<TService>>>());
        });

        services.TryAddSingleton<IPulseServiceFactoryMetrics>(serviceProvider =>
            (IPulseServiceFactoryMetrics)serviceProvider
                .GetRequiredService<IPulseServiceFactory<TService>>());

        return services;
    }

    /// <summary>
    /// Registers the deprecated factory cache using <see cref="ActivatorUtilities"/>.
    /// </summary>
    /// <typeparam name="TService">Legacy service implementation.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="configureOptions">Legacy cache options.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    /// Migration: use <c>AddPulseService&lt;TService&gt;((sp, id) =&gt;
    /// ActivatorUtilities.CreateInstance&lt;TService&gt;(sp, id))</c>.
    /// </remarks>
    [Obsolete("Use AddPulseService<TService>() and inject IServiceAccessor<TService>.", false)]
    public static IServiceCollection AddPulseServiceFactory<TService>(
        this IServiceCollection services,
        Action<PulseServiceFactoryOptions>? configureOptions = null)
        where TService : IPulseService
        => services.AddPulseServiceFactory<TService>(
            (serviceProvider, serviceId) =>
            {
                var parts = serviceId.Split(':', 2);
                var businessId = parts.Length > 1 ? parts[1] : serviceId;
                return ActivatorUtilities.CreateInstance<TService>(serviceProvider, businessId);
            },
            configureOptions);
}
