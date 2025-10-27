using Microsoft.Extensions.Logging;
using PulseRPC.Scheduling;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Scheduling;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PulseRPC.Server.Pipeline;

/// <summary>
/// Enhanced service invoker that integrates health monitoring for IPulseService instances
/// </summary>
/// <remarks>
/// <para>
/// Wraps ServiceInvoker with additional health monitoring capabilities:
/// </para>
/// <list type="bullet">
/// <item><description>Pre-invocation health checks (rejects isolated instances)</description></item>
/// <item><description>Post-invocation result recording (success/failure tracking)</description></item>
/// <item><description>Automatic circuit breaker state transitions</description></item>
/// <item><description>Backward compatible with non-IPulseService instances</description></item>
/// </list>
/// </remarks>
public sealed class HealthAwareServiceInvoker : IServiceHandler
{
    private readonly ServiceInvoker _innerInvoker;
    private readonly object _serviceInstance;
    private readonly ServiceInstanceHealthMonitor? _healthMonitor;
    private readonly IServiceScheduler? _scheduler;
    private readonly ILogger<HealthAwareServiceInvoker>? _logger;
    private readonly ServiceSchedulingKey? _schedulingKey;
    private readonly bool _isIPulseService;

    /// <summary>
    /// Creates a health-aware service invoker
    /// </summary>
    /// <param name="serviceInstance">The service instance to invoke</param>
    /// <param name="healthMonitor">Health monitor (optional, for IPulseService only)</param>
    /// <param name="scheduler">Service scheduler (optional, for IPulseService only)</param>
    /// <param name="logger">Logger instance (optional)</param>
    /// <param name="defaultTimeout">Default timeout for invocations</param>
    public HealthAwareServiceInvoker(
        object serviceInstance,
        ServiceInstanceHealthMonitor? healthMonitor = null,
        IServiceScheduler? scheduler = null,
        ILogger<HealthAwareServiceInvoker>? logger = null,
        TimeSpan? defaultTimeout = null)
    {
        if (serviceInstance == null)
        {
            throw new ArgumentNullException(nameof(serviceInstance));
        }

        _serviceInstance = serviceInstance;
        _innerInvoker = new ServiceInvoker(serviceInstance, defaultTimeout);
        _healthMonitor = healthMonitor;
        _scheduler = scheduler;
        _logger = logger;

        // Detect if service implements IPulseService
        _isIPulseService = IPulseServiceDetector.TryGetSchedulingKey(serviceInstance, out var key);
        _schedulingKey = _isIPulseService ? key : null;

        if (_isIPulseService)
        {
            _logger?.LogDebug(
                "Service instance {ServiceDescription} implements IPulseService - health monitoring enabled",
                IPulseServiceDetector.GetServiceDescription(serviceInstance));
        }
    }

    /// <summary>
    /// Invokes a service method with health monitoring (if IPulseService)
    /// </summary>
    public async Task<InvocationResult> InvokeAsync(
        string methodName,
        ReadOnlyMemory<byte> parameters,
        IRequestContext context)
    {
        // Non-IPulseService: invoke directly without health monitoring
        if (!_isIPulseService || !_schedulingKey.HasValue || _healthMonitor == null)
        {
            return await _innerInvoker.InvokeAsync(methodName, parameters, context);
        }

        var key = _schedulingKey.Value;

        // Pre-invocation health check
        if (!_healthMonitor.CanAcceptRequest(key))
        {
            _logger?.LogWarning(
                "Service instance {ServiceKey} is in unhealthy state - rejecting request for method {MethodName}",
                key,
                methodName);

            return InvocationResult.Failure(
                "ServiceUnavailable",
                $"Service instance {key} is currently isolated due to health issues",
                null,
                0);
        }

        // Invoke the service
        var startTime = Stopwatch.GetTimestamp();
        InvocationResult result;

        // If scheduler is available, route through scheduler for thread affinity
        if (_scheduler != null && _scheduler.IsRunning)
        {
            InvocationResult? capturedResult = null;

            try
            {
                Func<Task> workItem = async () =>
                {
                    capturedResult = await _innerInvoker.InvokeAsync(methodName, parameters, context);
                };

                await _scheduler.ScheduleAsync(key, workItem, context.CancellationToken);

                result = capturedResult ?? InvocationResult.Failure(
                    "SchedulerError",
                    "Scheduler completed but result was not captured",
                    null,
                    Stopwatch.GetElapsedTime(startTime).TotalMilliseconds);
            }
            catch (Exception ex)
            {
                var elapsed = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
                result = InvocationResult.Failure(
                    ex.GetType().Name,
                    ex.Message,
                    null,
                    elapsed);
            }
        }
        else
        {
            result = await _innerInvoker.InvokeAsync(methodName, parameters, context);
        }

        // Post-invocation: record result for health monitoring
        var success = result.IsSuccess;
        var timestamp = DateTime.UtcNow;

        _healthMonitor.RecordRequestResult(key, success, timestamp);

        if (!success)
        {
            _logger?.LogWarning(
                "Service instance {ServiceKey} method {MethodName} failed with error: {ErrorType}",
                key,
                methodName,
                result.ErrorType);
        }

        return result;
    }

    /// <summary>
    /// Gets the list of available method names
    /// </summary>
    public IReadOnlyList<string> GetMethodNames()
    {
        return _innerInvoker.GetMethodNames();
    }

    /// <summary>
    /// Gets whether this service instance implements IPulseService
    /// </summary>
    public bool IsIPulseService => _isIPulseService;

    /// <summary>
    /// Gets the scheduling key (if IPulseService)
    /// </summary>
    public ServiceSchedulingKey? SchedulingKey => _schedulingKey;
}
