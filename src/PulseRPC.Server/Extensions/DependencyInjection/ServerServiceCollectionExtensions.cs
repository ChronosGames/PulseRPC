using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Core;
using PulseRPC.Server.Models;
using PulseRPC.Server.Observability;
using PulseRPC.Server.Pipeline;
using System;

namespace PulseRPC.Server.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring PulseRPC Server with Microsoft.Extensions.DependencyInjection.
/// Implements FR-066: Dependency injection integration.
/// </summary>
public static class ServerServiceCollectionExtensions
{
    /// <summary>
    /// Adds PulseRPC Server services to the service collection.
    /// Registers all pipeline components, core infrastructure, and observability services.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">Optional configuration action for PulseServerOptions.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPulseRpcServer(
        this IServiceCollection services,
        Action<PulseServerOptions>? configure = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // Register and configure options
        var options = new PulseServerOptions();
        configure?.Invoke(options);
        options.Validate(); // Validate configuration

        services.TryAddSingleton(options);

        // Convert to ServerHostOptions
        var hostOptions = options.ToServerHostOptions();
        services.TryAddSingleton(hostOptions);

        // Register core pipeline components
        RegisterPipelineComponents(services, hostOptions);

        // Register core infrastructure
        RegisterCoreInfrastructure(services, hostOptions);

        // Register observability services
        RegisterObservabilityServices(services, options);

        // Register ServerHost as the main orchestrator
        services.TryAddSingleton<ServerHost>();

        return services;
    }

    /// <summary>
    /// Registers a service implementation for RPC invocation.
    /// The service will be available for remote method calls.
    /// </summary>
    /// <typeparam name="TService">The service implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceName">The name to register the service under.</param>
    /// <param name="serviceOptions">Optional service-specific options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPulseService<TService>(
        this IServiceCollection services,
        string serviceName,
        ServiceOptions? serviceOptions = null)
        where TService : class
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));
        }

        // Register the service implementation as scoped (allows per-request state)
        services.TryAddScoped<TService>();

        // Register service descriptor for ServiceRegistry
        services.AddSingleton(sp => new ServiceDescriptor<TService>(serviceName, serviceOptions));

        return services;
    }

    /// <summary>
    /// Registers a singleton service implementation for RPC invocation.
    /// Use for stateless services that can be shared across all requests.
    /// </summary>
    /// <typeparam name="TService">The service implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceName">The name to register the service under.</param>
    /// <param name="serviceOptions">Optional service-specific options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPulseSingletonService<TService>(
        this IServiceCollection services,
        string serviceName,
        ServiceOptions? serviceOptions = null)
        where TService : class
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));
        }

        // Register the service implementation as singleton
        services.TryAddSingleton<TService>();

        // Register service descriptor for ServiceRegistry
        services.AddSingleton(sp => new ServiceDescriptor<TService>(serviceName, serviceOptions));

        return services;
    }

    /// <summary>
    /// Registers a transient service implementation for RPC invocation.
    /// Use for services that require a new instance per request.
    /// </summary>
    /// <typeparam name="TService">The service implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceName">The name to register the service under.</param>
    /// <param name="serviceOptions">Optional service-specific options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPulseTransientService<TService>(
        this IServiceCollection services,
        string serviceName,
        ServiceOptions? serviceOptions = null)
        where TService : class
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));
        }

        // Register the service implementation as transient
        services.TryAddTransient<TService>();

        // Register service descriptor for ServiceRegistry
        services.AddSingleton(sp => new ServiceDescriptor<TService>(serviceName, serviceOptions));

        return services;
    }

    private static void RegisterPipelineComponents(
        IServiceCollection services,
        ServerHostOptions hostOptions)
    {
        // Message Receiver - will be initialized later with transport
        // Transport is created by ServerHost at runtime

        // Message Dispatcher
        services.TryAddSingleton<MessageDispatcher>();

        // Service Invoker
        services.TryAddSingleton<CompiledServiceInvoker>();
        services.TryAddSingleton<ServiceInvoker>();

        // Response Builder
        services.TryAddSingleton<ResponseBuilder>();

        // Error Response Factory
        services.TryAddSingleton<ErrorResponseFactory>();

        // Response Transmitter
        services.TryAddSingleton<ResponseTransmitter>();
    }

    private static void RegisterCoreInfrastructure(
        IServiceCollection services,
        ServerHostOptions hostOptions)
    {
        // Connection Manager
        services.TryAddSingleton<ConnectionManager>();

        // Service Registry
        services.TryAddSingleton<ServiceRegistry>();

        // Backpressure Policy
        services.TryAddSingleton<Core.BackpressurePolicy>();
    }

    private static void RegisterObservabilityServices(
        IServiceCollection services,
        PulseServerOptions options)
    {
        // Metrics Collector
        services.TryAddSingleton<PipelineMetricsCollector>();

        // Distributed Tracing (if enabled)
        // Note: DistributedTracingIntegration is a static class, no DI registration needed

        // Diagnostic Endpoints (if enabled)
        if (options.EnableDiagnosticEndpoints)
        {
            services.TryAddSingleton<DiagnosticEndpoints>();
        }
    }
}

/// <summary>
/// Service descriptor for registering services with ServiceRegistry.
/// Used internally by DI to track service registrations.
/// </summary>
/// <typeparam name="TService">The service implementation type.</typeparam>
public sealed class ServiceDescriptor<TService>
    where TService : class
{
    /// <summary>
    /// Gets the service name for RPC invocation.
    /// </summary>
    public string ServiceName { get; }

    /// <summary>
    /// Gets the service-specific options.
    /// </summary>
    public ServiceOptions? Options { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceDescriptor{TService}"/> class.
    /// </summary>
    /// <param name="serviceName">The service name.</param>
    /// <param name="options">The service options.</param>
    public ServiceDescriptor(string serviceName, ServiceOptions? options = null)
    {
        ServiceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
        Options = options;
    }
}
