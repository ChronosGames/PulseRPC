using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Dispatch;
using PulseRPC.Server.ErrorHandling;
using PulseRPC.Server.Models;
using PulseRPC.Server.Response;

namespace PulseRPC.Server.Pipeline;

/// <summary>
/// End-to-end message pipeline orchestrating all 5 stages:
/// 1. Reception (MessageEngine)
/// 2. Dispatching (Dispatcher)
/// 3. Invocation (CompiledInvoker)
/// 4. Response Generation (ResponseProcessor)
/// 5. Transmission (ResponseTransmitter)
/// </summary>
public sealed class MessagePipeline : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ServiceRegistration> _serviceRegistry;
    private readonly ResponseTransmitter _responseTransmitter;
    private readonly FaultIsolationPolicy _faultIsolation;
    private readonly PipelineOptions _options;
    private readonly ILogger<MessagePipeline> _logger;
    private readonly CancellationTokenSource _shutdownCts;

    private long _totalMessagesProcessed;
    private long _totalErrors;
    private long _totalTimeouts;

    public MessagePipeline(
        PipelineOptions? options = null,
        ILogger<MessagePipeline>? logger = null)
    {
        _options = options ?? new PipelineOptions();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MessagePipeline>.Instance;
        _serviceRegistry = new ConcurrentDictionary<string, ServiceRegistration>();
        _responseTransmitter = new ResponseTransmitter(
            maxQueueDepth: _options.MaxQueueDepth,
            batchSize: _options.ResponseBatchSize,
            batchMaxWait: TimeSpan.FromMilliseconds(_options.ResponseBatchDelayMs),
            logger: null
        );
        _faultIsolation = new FaultIsolationPolicy(logger: null);
        _shutdownCts = new CancellationTokenSource();
    }

    /// <summary>
    /// Registers a service for RPC handling.
    /// </summary>
    public void RegisterService<TService>(
        string serviceName,
        TService serviceInstance,
        TimeSpan? timeout = null,
        MessagePriority priority = MessagePriority.Normal) where TService : class
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be empty", nameof(serviceName));

        if (serviceInstance == null)
            throw new ArgumentNullException(nameof(serviceInstance));

        // Compile all public methods
        var methods = CompiledServiceInvoker.CompileServiceMethods(typeof(TService));

        if (methods.Count == 0)
        {
            _logger.LogWarning("Service {ServiceName} has no public methods to register", serviceName);
        }

        var registration = new ServiceRegistration
        {
            ServiceName = serviceName,
            ServiceType = typeof(TService),
            Methods = methods,
            TimeoutPolicy = timeout ?? _options.DefaultTimeoutPolicy.DefaultTimeout,
            Priority = priority
        };

        if (_serviceRegistry.TryAdd(serviceName, registration))
        {
            // Activate service
            registration.TryTransitionState(ServiceState.Active);

            _logger.LogInformation(
                "Registered service {ServiceName} with {MethodCount} methods (Timeout: {Timeout}ms, Priority: {Priority})",
                serviceName,
                methods.Count,
                timeout?.TotalMilliseconds ?? _options.DefaultTimeoutPolicy.DefaultTimeout.TotalMilliseconds,
                priority);
        }
        else
        {
            throw new InvalidOperationException($"Service '{serviceName}' is already registered");
        }
    }

    /// <summary>
    /// Processes a message through the complete pipeline.
    /// </summary>
    public async Task ProcessMessageAsync(RpcMessage message, ServerConnection connection, object serviceInstance)
    {
        var activity = _options.EnableDistributedTracing ? StartActivity(message) : null;

        try
        {
            // Stage 1: Validation
            if (!message.IsValid(out var validationError))
            {
                _logger.LogWarning(
                    "Invalid message received: {Error} (RequestId: {RequestId})",
                    validationError,
                    message.RequestId);

                var errorResponse = ErrorResponseFactory.CreateProtocolError(message.RequestId, validationError!);
                await _responseTransmitter.TransmitAsync(errorResponse, connection, _shutdownCts.Token);
                return;
            }

            // Stage 2: Service Lookup
            if (!_serviceRegistry.TryGetValue(message.ServiceName, out var serviceRegistration))
            {
                _logger.LogWarning(
                    "Service not found: {ServiceName} (RequestId: {RequestId})",
                    message.ServiceName,
                    message.RequestId);

                var errorResponse = ErrorResponseFactory.CreateServiceNotFoundError(
                    message.RequestId,
                    message.ServiceName);
                await _responseTransmitter.TransmitAsync(errorResponse, connection, _shutdownCts.Token);
                Interlocked.Increment(ref _totalErrors);
                return;
            }

            if (!serviceRegistration.CanHandleRequests)
            {
                _logger.LogWarning(
                    "Service is not active: {ServiceName} (State: {State}, RequestId: {RequestId})",
                    message.ServiceName,
                    serviceRegistration.State,
                    message.RequestId);

                var errorResponse = ErrorResponseFactory.CreateServiceNotFoundError(
                    message.RequestId,
                    message.ServiceName);
                await _responseTransmitter.TransmitAsync(errorResponse, connection, _shutdownCts.Token);
                return;
            }

            // Stage 3: Method Lookup
            if (!serviceRegistration.Methods.TryGetValue(message.MethodName, out var methodInvoker))
            {
                _logger.LogWarning(
                    "Method not found: {ServiceName}.{MethodName} (RequestId: {RequestId})",
                    message.ServiceName,
                    message.MethodName,
                    message.RequestId);

                var errorResponse = ErrorResponseFactory.CreateMethodNotFoundError(
                    message.RequestId,
                    message.ServiceName,
                    message.MethodName);
                await _responseTransmitter.TransmitAsync(errorResponse, connection, _shutdownCts.Token);
                Interlocked.Increment(ref _totalErrors);
                return;
            }

            // Stage 4: Create Request Context
            var context = RpcRequestContext.Create(
                message,
                connection,
                serviceRegistration.TimeoutPolicy,
                activity);

            try
            {
                // Stage 5: Invoke Method with Fault Isolation
                var invocationTask = InvokeMethodAsync(methodInvoker, serviceInstance, message.Payload, context);
                var response = await _faultIsolation.ExecuteWithIsolationAsync(
                    async () => await invocationTask,
                    context);

                // Stage 6: Transmit Response
                var transmitted = await _responseTransmitter.TransmitAsync(response, connection, _shutdownCts.Token);

                // Update statistics
                Interlocked.Increment(ref _totalMessagesProcessed);
                var durationMs = (long)context.GetElapsedTime().TotalMilliseconds;
                serviceRegistration.RecordInvocation(durationMs, !response.IsSuccess);

                if (!response.IsSuccess)
                {
                    Interlocked.Increment(ref _totalErrors);
                }

                if (transmitted)
                {
                    _logger.LogDebug(
                        "Processed message: {ServiceName}.{MethodName} (RequestId: {RequestId}, Duration: {Duration}ms, Success: {Success})",
                        message.ServiceName,
                        message.MethodName,
                        message.RequestId,
                        durationMs,
                        response.IsSuccess);
                }
                else
                {
                    _logger.LogWarning(
                        "Failed to transmit response: {ServiceName}.{MethodName} (RequestId: {RequestId})",
                        message.ServiceName,
                        message.MethodName,
                        message.RequestId);
                }
            }
            finally
            {
                context.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Pipeline error processing message: {ServiceName}.{MethodName} (RequestId: {RequestId})",
                message.ServiceName,
                message.MethodName,
                message.RequestId);

            Interlocked.Increment(ref _totalErrors);

            // Attempt to send error response
            try
            {
                var errorResponse = ErrorResponseFactory.CreateProtocolError(
                    message.RequestId,
                    $"Internal server error: {ex.Message}");
                await _responseTransmitter.TransmitAsync(errorResponse, connection, _shutdownCts.Token);
            }
            catch (Exception transmitEx)
            {
                _logger.LogError(transmitEx, "Failed to transmit error response for RequestId {RequestId}", message.RequestId);
            }
        }
        finally
        {
            activity?.Stop();
        }
    }

    private async Task<object?> InvokeMethodAsync(
        CompiledMethodInvoker invoker,
        object serviceInstance,
        ReadOnlyMemory<byte> payload,
        RpcRequestContext context)
    {
        // TODO: Deserialize payload to parameters using MemoryPack
        // For now, pass empty parameters as placeholder
        var parameters = Array.Empty<object>();

        return await CompiledServiceInvoker.InvokeAsync(
            invoker,
            serviceInstance,
            parameters,
            context.CancellationToken);
    }

    private Activity? StartActivity(RpcMessage message)
    {
        var activity = new Activity("ProcessMessage");
        activity.SetTag("service.name", message.ServiceName);
        activity.SetTag("method.name", message.MethodName);
        activity.SetTag("request.id", message.RequestId.ToString());
        activity.Start();
        return activity;
    }

    /// <summary>
    /// Gets pipeline statistics.
    /// </summary>
    public (long Processed, long Errors, long Timeouts) GetStatistics()
    {
        return (
            Interlocked.Read(ref _totalMessagesProcessed),
            Interlocked.Read(ref _totalErrors),
            Interlocked.Read(ref _totalTimeouts)
        );
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("MessagePipeline disposing");

        _shutdownCts.Cancel();

        await _responseTransmitter.DisposeAsync();

        _shutdownCts.Dispose();

        _logger.LogInformation("MessagePipeline disposed");
    }
}
