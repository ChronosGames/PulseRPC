using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Models;
using PulseRPC.Server.Observability;

namespace PulseRPC.Server.Pipeline;

/// <summary>
/// Extension methods to integrate MessagePipeline with PulseServer.
/// </summary>
public static class PulseServerPipelineExtensions
{
    private static readonly ConditionalWeakTable<PulseServer, ServerPipelineContext> _pipelineContexts = new();

    /// <summary>
    /// Enables the message processing pipeline for the server.
    /// </summary>
    public static PulseServer UseMessagePipeline(
        this PulseServer server,
        PipelineOptions? options = null,
        ILoggerFactory? loggerFactory = null)
    {
        if (_pipelineContexts.TryGetValue(server, out var existingContext))
        {
            throw new InvalidOperationException("Message pipeline is already enabled for this server");
        }

        var logger = loggerFactory?.CreateLogger<MessagePipeline>();
        var pipeline = new MessagePipeline(options, logger);
        var metricsCollector = new PipelineMetricsCollector();
        var diagnosticEndpoints = new DiagnosticEndpoints(metricsCollector, loggerFactory?.CreateLogger<DiagnosticEndpoints>());

        var context = new ServerPipelineContext
        {
            Pipeline = pipeline,
            MetricsCollector = metricsCollector,
            DiagnosticEndpoints = diagnosticEndpoints,
            Options = options ?? new PipelineOptions()
        };

        _pipelineContexts.Add(server, context);

        return server;
    }

    /// <summary>
    /// Registers a service with the message pipeline.
    /// </summary>
    public static PulseServer RegisterPipelineService<TService>(
        this PulseServer server,
        string serviceName,
        TService serviceInstance,
        TimeSpan? timeout = null,
        MessagePriority priority = MessagePriority.Normal) where TService : class
    {
        if (!_pipelineContexts.TryGetValue(server, out var context))
        {
            throw new InvalidOperationException("Message pipeline not enabled. Call UseMessagePipeline() first.");
        }

        context.Pipeline.RegisterService(serviceName, serviceInstance, timeout, priority);

        // Store service instance for later use
        context.ServiceInstances[serviceName] = serviceInstance;

        return server;
    }

    /// <summary>
    /// Gets the message pipeline for the server.
    /// </summary>
    public static MessagePipeline? GetMessagePipeline(this PulseServer server)
    {
        return _pipelineContexts.TryGetValue(server, out var context) ? context.Pipeline : null;
    }

    /// <summary>
    /// Gets the metrics collector for the server.
    /// </summary>
    public static PipelineMetricsCollector? GetMetricsCollector(this PulseServer server)
    {
        return _pipelineContexts.TryGetValue(server, out var context) ? context.MetricsCollector : null;
    }

    /// <summary>
    /// Gets the diagnostic endpoints for the server.
    /// </summary>
    public static DiagnosticEndpoints? GetDiagnosticEndpoints(this PulseServer server)
    {
        return _pipelineContexts.TryGetValue(server, out var context) ? context.DiagnosticEndpoints : null;
    }

    /// <summary>
    /// Gets pipeline statistics.
    /// </summary>
    public static (long Processed, long Errors, long Timeouts) GetPipelineStatistics(this PulseServer server)
    {
        if (_pipelineContexts.TryGetValue(server, out var context))
        {
            return context.Pipeline.GetStatistics();
        }

        return (0, 0, 0);
    }

    /// <summary>
    /// Processes a message through the pipeline (internal use).
    /// </summary>
    internal static async Task ProcessMessageAsync(
        this PulseServer server,
        RpcMessage message,
        ServerConnection connection)
    {
        if (!_pipelineContexts.TryGetValue(server, out var context))
        {
            throw new InvalidOperationException("Message pipeline not enabled");
        }

        // Get service instance
        if (!context.ServiceInstances.TryGetValue(message.ServiceName, out var serviceInstance))
        {
            throw new InvalidOperationException($"Service instance not found: {message.ServiceName}");
        }

        // Record metrics
        context.MetricsCollector.RecordRequestStart();

        try
        {
            await context.Pipeline.ProcessMessageAsync(message, connection, serviceInstance);
        }
        finally
        {
            // Metrics recorded inside pipeline
        }
    }
}

/// <summary>
/// Internal context holding pipeline-related objects for a server instance.
/// </summary>
internal sealed class ServerPipelineContext
{
    public MessagePipeline Pipeline { get; init; } = null!;
    public PipelineMetricsCollector MetricsCollector { get; init; } = null!;
    public DiagnosticEndpoints DiagnosticEndpoints { get; init; } = null!;
    public PipelineOptions Options { get; init; } = null!;
    public ConcurrentDictionary<string, object> ServiceInstances { get; } = new();
}
