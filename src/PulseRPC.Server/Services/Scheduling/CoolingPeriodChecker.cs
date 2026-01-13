using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Health; using PulseRPC.Server.Processing; using PulseRPC.Server.Channels; using PulseRPC.Server.Services; using PulseRPC.Server.Services.Scheduling;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Server.Services.Scheduling;

/// <summary>
/// Background service that periodically checks for service instances in cooling period
/// </summary>
/// <remarks>
/// <para>
/// Scans all service instances every 10 seconds and triggers state transitions for:
/// </para>
/// <list type="bullet">
/// <item><description>Isolated → CoolingDown (when cooling period expires)</description></item>
/// <item><description>CoolingDown → ProbeAllowed (automatic transition)</description></item>
/// </list>
/// <para>
/// This ensures timely recovery attempts for isolated service instances.
/// </para>
/// </remarks>
public sealed class CoolingPeriodChecker : BackgroundService
{
    private readonly ServiceInstanceHealthMonitor _healthMonitor;
    private readonly CircuitBreakerPolicy _circuitBreakerPolicy;
    private readonly ILogger<CoolingPeriodChecker> _logger;
    private readonly TimeSpan _checkInterval;

    /// <summary>
    /// Creates a cooling period checker
    /// </summary>
    /// <param name="healthMonitor">Health monitor instance</param>
    /// <param name="circuitBreakerPolicy">Circuit breaker policy</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="checkInterval">Check interval (default: 10 seconds)</param>
    public CoolingPeriodChecker(
        ServiceInstanceHealthMonitor healthMonitor,
        CircuitBreakerPolicy circuitBreakerPolicy,
        ILogger<CoolingPeriodChecker> logger,
        TimeSpan? checkInterval = null)
    {
        _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
        _circuitBreakerPolicy = circuitBreakerPolicy ?? throw new ArgumentNullException(nameof(circuitBreakerPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _checkInterval = checkInterval ?? TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Background execution method
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "CoolingPeriodChecker started with check interval of {Interval}",
            _checkInterval);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckCoolingPeriodsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking cooling periods");
                }

                // Wait for next check interval
                await Task.Delay(_checkInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
            _logger.LogInformation("CoolingPeriodChecker stopping due to cancellation");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "CoolingPeriodChecker stopped unexpectedly");
            throw;
        }
    }

    /// <summary>
    /// Checks all service instances for cooling period expiration
    /// </summary>
    private Task CheckCoolingPeriodsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var healthStates = _healthMonitor.GetAllHealthStates();
        var transitionCount = 0;

        foreach (var (key, health) in healthStates)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // Check if state transition is needed
            var previousState = health.State;

            // Evaluate state transition (will handle Isolated → CoolingDown → ProbeAllowed)
            var stateChanged = _circuitBreakerPolicy.EvaluateTransition(
                health,
                requestSucceeded: true, // Not a real request, just time-based check
                now);

            if (stateChanged && health.State != previousState)
            {
                transitionCount++;

                _logger.LogInformation(
                    "Service instance {ServiceKey} transitioned from {OldState} to {NewState} (cooling period check)",
                    key,
                    previousState,
                    health.State);
            }
        }

        if (transitionCount > 0)
        {
            _logger.LogInformation(
                "Cooling period check completed: {TransitionCount} state transitions",
                transitionCount);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Cleanup on service stop
    /// </summary>
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("CoolingPeriodChecker stopped");
        return base.StopAsync(cancellationToken);
    }
}
