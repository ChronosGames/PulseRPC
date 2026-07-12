using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Scheduling;
using PulseRPC.Server.Health;
using PulseRPC.Server.Services.Scheduling;

namespace PulseRPC.Server.Configuration;

/// <summary>
/// Extension methods for configuring ServiceThreadScheduler in DI container.
/// </summary>
public static class ServiceSchedulerExtensions
{
    private const string SchedulerObsoleteMessage =
        "AddServiceScheduler starts a standalone scheduler that is not consumed by MessageEngine. Configure PulseServerOptions.MessageWorkerShardCount and MessageQueueCapacityPerShard instead.";

    /// <summary>
    /// Add ServiceThreadScheduler to the service collection with default configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    [Obsolete(SchedulerObsoleteMessage, false)]
    public static IServiceCollection AddServiceScheduler(this IServiceCollection services)
    {
        return AddServiceScheduler(services, _ => { });
    }

    /// <summary>
    /// Add ServiceThreadScheduler to the service collection with custom configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure scheduler options.</param>
    /// <returns>The service collection for chaining.</returns>
    [Obsolete(SchedulerObsoleteMessage, false)]
    public static IServiceCollection AddServiceScheduler(
        this IServiceCollection services,
        Action<SchedulerConfiguration> configureOptions)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        // Register configuration
        var config = new SchedulerConfiguration();
        configureOptions?.Invoke(config);
        config.Validate();

        services.AddSingleton(config);

        // Register scheduler as singleton
        services.AddSingleton<IServiceScheduler>(sp =>
        {
            var configuration = sp.GetRequiredService<SchedulerConfiguration>();
            var logger = sp.GetService<ILogger<ServiceThreadScheduler>>();
            var healthMonitor = sp.GetService<ServiceHealthMonitor>(); // Optional
            return new ServiceThreadScheduler(configuration, logger, healthMonitor);
        });

        // Register as hosted service to manage lifecycle
        services.AddSingleton<IHostedService, ServiceSchedulerHostedService>();

        return services;
    }

    /// <summary>
    /// Add ServiceThreadScheduler with configuration from IConfiguration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration section containing scheduler settings.</param>
    /// <param name="sectionName">The configuration section name (default: "Scheduler").</param>
    /// <returns>The service collection for chaining.</returns>
    [Obsolete(SchedulerObsoleteMessage, false)]
    public static IServiceCollection AddServiceScheduler(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "Scheduler")
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        return AddServiceScheduler(services, config =>
        {
            var section = configuration.GetSection(sectionName);
            if (section.Exists())
            {
                section.Bind(config);
            }
        });
    }

    /// <summary>
    /// Add ServiceHealthMonitor to the service collection with default configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddServiceHealthMonitor(this IServiceCollection services)
    {
        return AddServiceHealthMonitor(services, _ => { });
    }

    /// <summary>
    /// Add ServiceHealthMonitor to the service collection with custom configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure health monitor options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddServiceHealthMonitor(
        this IServiceCollection services,
        Action<BasicHealthMonitorOptions> configureOptions)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        // Register configuration
        var options = new BasicHealthMonitorOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);

        // Register health monitor as singleton
        services.AddSingleton<ServiceHealthMonitor>(sp =>
        {
            var healthOptions = sp.GetRequiredService<BasicHealthMonitorOptions>();
            var logger = sp.GetService<ILogger<ServiceHealthMonitor>>();
            return new ServiceHealthMonitor(healthOptions, logger);
        });

        return services;
    }

    /// <summary>
    /// Add ServiceHealthMonitor with configuration from IConfiguration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration section containing health monitor settings.</param>
    /// <param name="sectionName">The configuration section name (default: "HealthMonitor").</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddServiceHealthMonitor(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "HealthMonitor")
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        return AddServiceHealthMonitor(services, options =>
        {
            var section = configuration.GetSection(sectionName);
            if (section.Exists())
            {
                section.Bind(options);
            }
        });
    }
}

/// <summary>
/// Hosted service to manage ServiceThreadScheduler lifecycle.
/// Starts the scheduler when the application starts and stops it on shutdown.
/// </summary>
internal sealed class ServiceSchedulerHostedService : IHostedService
{
    private readonly IServiceScheduler _scheduler;
    private readonly ILogger<ServiceSchedulerHostedService> _logger;

    public ServiceSchedulerHostedService(
        IServiceScheduler scheduler,
        ILogger<ServiceSchedulerHostedService> logger)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting ServiceThreadScheduler");

        try
        {
            await _scheduler.StartAsync(cancellationToken);
            _logger.LogInformation("ServiceThreadScheduler started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start ServiceThreadScheduler");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping ServiceThreadScheduler");

        try
        {
            await _scheduler.StopAsync(cancellationToken);
            _logger.LogInformation("ServiceThreadScheduler stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping ServiceThreadScheduler");
            // Don't throw on shutdown
        }
    }
}

/// <summary>
/// Historical documentation placeholder retained for binary compatibility.
/// The standalone scheduler is not connected to the current MessageEngine.
/// </summary>
[Obsolete("Historical scheduler example only; AddServiceScheduler is not connected to MessageEngine.", false)]
public static class RegistrationExamples
{
    // Documentation class - see XML documentation above for usage examples
}
