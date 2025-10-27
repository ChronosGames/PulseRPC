using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Scheduling;
using System;

namespace PulseRPC.Server.Builder;

/// <summary>
/// Extension methods for configuring IPulseService infrastructure in DI container
/// </summary>
/// <remarks>
/// Registers all components needed for service instance thread scheduling and disaster isolation:
/// <list type="bullet">
/// <item><description>ConsistentHashRing - Thread assignment</description></item>
/// <item><description>ThreadAffinityManager - Instance-to-thread mapping</description></item>
/// <item><description>CircuitBreakerPolicy - Health state transitions</description></item>
/// <item><description>ServiceInstanceHealthMonitor - Health tracking</description></item>
/// <item><description>CoolingPeriodChecker - Background recovery checks</description></item>
/// </list>
/// </remarks>
public static class IPulseServiceExtensions
{
    /// <summary>
    /// Add IPulseService infrastructure with default configuration
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddIPulseServiceInfrastructure(this IServiceCollection services)
    {
        return AddIPulseServiceInfrastructure(services, null, null);
    }

    /// <summary>
    /// Add IPulseService infrastructure with custom configuration
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureScheduling">Action to configure scheduling options</param>
    /// <param name="configureHealthMonitoring">Action to configure health monitoring options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddIPulseServiceInfrastructure(
        this IServiceCollection services,
        Action<ServiceSchedulingOptions>? configureScheduling,
        Action<HealthMonitorOptions>? configureHealthMonitoring)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // Register configuration options
        services.Configure<ServiceSchedulingOptions>(options =>
        {
            configureScheduling?.Invoke(options);
            options.Validate();
        });

        services.Configure<HealthMonitorOptions>(options =>
        {
            configureHealthMonitoring?.Invoke(options);
            options.Validate();
        });

        // Register ConsistentHashRing as singleton
        services.AddSingleton<ConsistentHashRing>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ServiceSchedulingOptions>>().Value;
            return new ConsistentHashRing(
                options.WorkerThreadCount,
                options.VirtualNodesPerThread);
        });

        // Register ThreadAffinityManager as singleton
        services.AddSingleton<ThreadAffinityManager>();

        // Register CircuitBreakerPolicy as singleton
        services.AddSingleton<CircuitBreakerPolicy>();

        // Register ServiceInstanceHealthMonitor as singleton
        services.AddSingleton<ServiceInstanceHealthMonitor>();

        // Register CoolingPeriodChecker as hosted service
        services.AddSingleton<CoolingPeriodChecker>();
        services.AddHostedService<CoolingPeriodChecker>(sp =>
            sp.GetRequiredService<CoolingPeriodChecker>());

        return services;
    }

    /// <summary>
    /// Add IPulseService infrastructure with configuration section binding
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration root</param>
    /// <param name="schedulingSectionName">Scheduling configuration section name (default: "ServiceScheduling")</param>
    /// <param name="healthMonitoringSectionName">Health monitoring configuration section name (default: "HealthMonitoring")</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddIPulseServiceInfrastructure(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        string schedulingSectionName = "ServiceScheduling",
        string healthMonitoringSectionName = "HealthMonitoring")
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        // Bind configuration sections
        services.Configure<ServiceSchedulingOptions>(
            configuration.GetSection(schedulingSectionName));

        services.Configure<HealthMonitorOptions>(
            configuration.GetSection(healthMonitoringSectionName));

        // Register components
        return AddIPulseServiceInfrastructure(services, null, null);
    }
}

/// <summary>
/// Usage examples for IPulseService infrastructure registration
/// </summary>
/// <example>
/// <code>
/// // In Program.cs or Startup.cs:
///
/// // Option 1: Default configuration
/// services.AddIPulseServiceInfrastructure();
///
/// // Option 2: Custom configuration
/// services.AddIPulseServiceInfrastructure(
///     configureScheduling: options =>
///     {
///         options.WorkerThreadCount = 16;
///         options.IdleInstanceTimeout = TimeSpan.FromMinutes(10);
///         options.VirtualNodesPerThread = 200;
///     },
///     configureHealthMonitoring: options =>
///     {
///         options.FailureThreshold = 5;
///         options.CoolingPeriod = TimeSpan.FromMinutes(2);
///         options.ProbeRequestLimit = 10;
///         options.ProbeSuccessThreshold = 6;
///     });
///
/// // Option 3: Configuration from appsettings.json
/// services.AddIPulseServiceInfrastructure(Configuration);
///
/// // appsettings.json:
/// {
///   "ServiceScheduling": {
///     "WorkerThreadCount": 16,
///     "IdleInstanceTimeout": "00:10:00",
///     "VirtualNodesPerThread": 150
///   },
///   "HealthMonitoring": {
///     "FailureThreshold": 3,
///     "CoolingPeriod": "00:01:00",
///     "ProbeRequestLimit": 5,
///     "ProbeSuccessThreshold": 3
///   }
/// }
///
/// // Then use HealthAwareServiceInvoker when registering services:
/// var healthMonitor = serviceProvider.GetRequiredService<ServiceInstanceHealthMonitor>();
/// var scheduler = serviceProvider.GetService<IServiceScheduler>();
/// var logger = serviceProvider.GetService<ILogger<HealthAwareServiceInvoker>>();
///
/// var invoker = new HealthAwareServiceInvoker(
///     serviceInstance: myService,
///     healthMonitor: healthMonitor,
///     scheduler: scheduler,
///     logger: logger);
/// </code>
/// </example>
public static class IPulseServiceRegistrationExamples
{
    // Documentation class - see XML documentation above for usage examples
}
