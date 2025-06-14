using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using PulseRPC.HealthCheck;
using PulseRPC.ServiceRegistration;

namespace PulseRPC.HealthCheck;

public class ServerHealthChecker(
    IServiceProvider serviceProvider,
    ILogger<ServerHealthChecker> logger,
    IOptions<HealthCheckOptions> options)
    : BackgroundService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly ILogger<ServerHealthChecker> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly HealthCheckOptions _options = options.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ConcurrentDictionary<string, HealthCheckState> _healthStates = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Server health check service is disabled");
            return;
        }

        _logger.LogInformation("Server health check service started with interval: {Interval}", _options.Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformHealthChecksAsync(stoppingToken);
                await Task.Delay(_options.Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during health check cycle");

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Server health check service stopped");
    }

    private async Task PerformHealthChecksAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var serviceRegistry = scope.ServiceProvider.GetRequiredService<IServiceRegistry>();
            var healthChecker = scope.ServiceProvider.GetRequiredService<IHealthChecker>();

            var allServices = await serviceRegistry.GetRegistrationsAsync(cancellationToken);
            var servicesToCheck = allServices.Where(s => s.HealthCheck != null).ToList();

            if (!servicesToCheck.Any())
            {
                _logger.LogDebug("No services with health check configuration found");
                return;
            }

            _logger.LogDebug("Starting health check for {Count} services", servicesToCheck.Count);

            var semaphore = new SemaphoreSlim(_options.MaxConcurrentChecks, _options.MaxConcurrentChecks);
            var healthCheckTasks = servicesToCheck.Select(async service =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await CheckServiceHealthAsync(service, healthChecker, serviceRegistry, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(healthCheckTasks);
            var healthyCount = results.Count(r => r.IsHealthy);
            var unhealthyCount = results.Length - healthyCount;

            _logger.LogDebug("Health check completed: {Healthy} healthy, {Unhealthy} unhealthy",
                healthyCount, unhealthyCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing health checks");
        }
    }

    private async Task<HealthCheckResult> CheckServiceHealthAsync(
        ServiceRegistration.ServiceRegistration service,
        IHealthChecker healthChecker,
        IServiceRegistry serviceRegistry,
        CancellationToken cancellationToken)
    {
        try
        {
            var endpoint = service.ToEndpoint();
            var currentHealth = await healthChecker.CheckHealthAsync(endpoint, cancellationToken);

            var state = _healthStates.AddOrUpdate(service.Id,
                new HealthCheckState(currentHealth.Status),
                (_, existing) => existing.UpdateHealth(currentHealth.Status));

            await ProcessHealthStateChange(service, state, serviceRegistry, cancellationToken);

            _logger.LogDebug("Health check for service {ServiceName} ({ServiceId}): {Status}",
                service.ServiceName, service.Id, currentHealth);

            return new HealthCheckResult
            {
                ServiceId = service.Id,
                ServiceName = service.ServiceName,
                Status = currentHealth.Status,
                IsHealthy = currentHealth.Status == HealthStatus.Healthy,
                ConsecutiveFailures = state.ConsecutiveFailures,
                ConsecutiveSuccesses = state.ConsecutiveSuccesses
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking health for service {ServiceName} ({ServiceId})",
                service.ServiceName, service.Id);

            return new HealthCheckResult
            {
                ServiceId = service.Id,
                ServiceName = service.ServiceName,
                Status = HealthStatus.Unhealthy,
                IsHealthy = false,
                Error = ex.Message
            };
        }
    }

    private async Task ProcessHealthStateChange(
        ServiceRegistration.ServiceRegistration service,
        HealthCheckState state,
        IServiceRegistry serviceRegistry,
        CancellationToken cancellationToken)
    {
        if (_options.RemoveUnhealthyServices &&
            state.ConsecutiveFailures >= _options.FailureThreshold)
        {
            _logger.LogWarning("Service {ServiceName} ({ServiceId}) has failed {Failures} consecutive health checks, removing from registry",
                service.ServiceName, service.Id, state.ConsecutiveFailures);

            try
            {
                await serviceRegistry.UnregisterAsync(service.Id, cancellationToken);
                _healthStates.TryRemove(service.Id, out _);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove unhealthy service {ServiceId}", service.Id);
            }
        }
        else if (state.ConsecutiveSuccesses >= _options.SuccessThreshold &&
                 state.PreviousHealth != HealthStatus.Healthy &&
                 state.CurrentHealth == HealthStatus.Healthy)
        {
            _logger.LogInformation("Service {ServiceName} ({ServiceId}) has recovered to healthy status",
                service.ServiceName, service.Id);
        }
    }

    public Dictionary<string, HealthCheckState> GetAllHealthStates()
    {
        return _healthStates.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public HealthCheckState? GetHealthState(string serviceId)
    {
        return _healthStates.TryGetValue(serviceId, out var state) ? state : null;
    }

    public Dictionary<string, object> GetStatistics()
    {
        var healthStates = _healthStates.Values.ToList();

        return new Dictionary<string, object>
        {
            ["Enabled"] = _options.Enabled,
            ["Interval"] = _options.Interval.ToString(),
            ["Timeout"] = _options.Timeout.ToString(),
            ["MaxConcurrentChecks"] = _options.MaxConcurrentChecks,
            ["FailureThreshold"] = _options.FailureThreshold,
            ["SuccessThreshold"] = _options.SuccessThreshold,
            ["RemoveUnhealthyServices"] = _options.RemoveUnhealthyServices,
            ["TotalMonitoredServices"] = healthStates.Count,
            ["HealthyServices"] = healthStates.Count(s => s.CurrentHealth == HealthStatus.Healthy),
            ["UnhealthyServices"] = healthStates.Count(s => s.CurrentHealth == HealthStatus.Unhealthy),
            ["UnknownServices"] = healthStates.Count(s => s.CurrentHealth == HealthStatus.Unknown),
            ["DegradedServices"] = healthStates.Count(s => s.CurrentHealth == HealthStatus.Degraded)
        };
    }
}
