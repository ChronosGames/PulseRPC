using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseServiceDiscovery.Abstractions;
using PulseServiceDiscovery.Abstractions.Models;
using PulseServiceDiscovery.Server.Options;
using System.Collections.Concurrent;

namespace PulseServiceDiscovery.Server.HealthCheck;

/// <summary>
/// 服务端健康检查服务 - 定期检查已注册服务的健康状态
/// </summary>
public class ServerHealthCheckService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ServerHealthCheckService> _logger;
    private readonly ServerHealthCheckOptions _options;
    private readonly ConcurrentDictionary<string, HealthCheckState> _healthStates = new();

    public ServerHealthCheckService(
        IServiceProvider serviceProvider,
        ILogger<ServerHealthCheckService> logger,
        IOptions<ServerOptions> options)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value?.HealthCheck ?? throw new ArgumentNullException(nameof(options));
    }

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

            // 获取所有已注册的服务
            var allServices = await serviceRegistry.GetAllServicesAsync(cancellationToken);
            var servicesToCheck = allServices.Where(s => s.Endpoint.HealthCheck != null).ToList();

            if (!servicesToCheck.Any())
            {
                _logger.LogDebug("No services with health check configuration found");
                return;
            }

            _logger.LogDebug("Starting health check for {Count} services", servicesToCheck.Count);

            // 并行执行健康检查（限制并发数）
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
        ServiceRegistration service,
        IHealthChecker healthChecker,
        IServiceRegistry serviceRegistry,
        CancellationToken cancellationToken)
    {
        try
        {
            var endpoint = service.Endpoint;
            var currentHealth = await healthChecker.CheckHealthAsync(endpoint, cancellationToken);

            var state = _healthStates.AddOrUpdate(service.Id,
                new HealthCheckState(currentHealth),
                (_, existing) => existing.UpdateHealth(currentHealth));

            // 检查是否需要更新服务状态或移除不健康的服务
            await ProcessHealthStateChange(service, state, serviceRegistry, cancellationToken);

            _logger.LogDebug("Health check for service {ServiceName} ({ServiceId}): {Status}",
                service.ServiceName, service.Id, currentHealth);

            return new HealthCheckResult
            {
                ServiceId = service.Id,
                ServiceName = service.ServiceName,
                CurrentHealth = currentHealth,
                IsHealthy = currentHealth == HealthStatus.Healthy,
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
                CurrentHealth = HealthStatus.Unhealthy,
                IsHealthy = false,
                Error = ex.Message
            };
        }
    }

    private async Task ProcessHealthStateChange(
        ServiceRegistration service,
        HealthCheckState state,
        IServiceRegistry serviceRegistry,
        CancellationToken cancellationToken)
    {
        // 如果连续失败次数达到阈值，且配置了移除不健康服务
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

        // 如果从不健康恢复到健康状态
        else if (state.ConsecutiveSuccesses >= _options.SuccessThreshold &&
                 state.PreviousHealth != HealthStatus.Healthy &&
                 state.CurrentHealth == HealthStatus.Healthy)
        {
            _logger.LogInformation("Service {ServiceName} ({ServiceId}) has recovered to healthy status",
                service.ServiceName, service.Id);
        }
    }

    /// <summary>
    /// 获取所有服务的健康状态
    /// </summary>
    /// <returns>健康状态字典</returns>
    public Dictionary<string, HealthCheckState> GetAllHealthStates()
    {
        return _healthStates.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// 获取指定服务的健康状态
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <returns>健康状态，如果不存在则返回null</returns>
    public HealthCheckState? GetHealthState(string serviceId)
    {
        return _healthStates.TryGetValue(serviceId, out var state) ? state : null;
    }

    /// <summary>
    /// 手动触发指定服务的健康检查
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康检查结果</returns>
    public async Task<HealthCheckResult?> TriggerHealthCheckAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var serviceRegistry = scope.ServiceProvider.GetRequiredService<IServiceRegistry>();
            var healthChecker = scope.ServiceProvider.GetRequiredService<IHealthChecker>();

            var service = await serviceRegistry.GetServiceAsync(serviceId, cancellationToken);
            if (service == null || service.Endpoint.HealthCheck == null)
            {
                return null;
            }

            return await CheckServiceHealthAsync(service, healthChecker, serviceRegistry, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering health check for service: {ServiceId}", serviceId);
            return null;
        }
    }

    /// <summary>
    /// 获取健康检查统计信息
    /// </summary>
    /// <returns>统计信息</returns>
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

/// <summary>
/// 健康检查状态
/// </summary>
public class HealthCheckState
{
    private HealthStatus _currentHealth;
    private HealthStatus _previousHealth;
    private int _consecutiveFailures;
    private int _consecutiveSuccesses;
    private DateTime _lastCheckTime;
    private readonly object _lock = new();

    public HealthStatus CurrentHealth
    {
        get { lock (_lock) return _currentHealth; }
    }

    public HealthStatus PreviousHealth
    {
        get { lock (_lock) return _previousHealth; }
    }

    public int ConsecutiveFailures
    {
        get { lock (_lock) return _consecutiveFailures; }
    }

    public int ConsecutiveSuccesses
    {
        get { lock (_lock) return _consecutiveSuccesses; }
    }

    public DateTime LastCheckTime
    {
        get { lock (_lock) return _lastCheckTime; }
    }

    public HealthCheckState(HealthStatus initialHealth)
    {
        _currentHealth = initialHealth;
        _previousHealth = HealthStatus.Unknown;
        _lastCheckTime = DateTime.UtcNow;
        _consecutiveFailures = initialHealth == HealthStatus.Unhealthy ? 1 : 0;
        _consecutiveSuccesses = initialHealth == HealthStatus.Healthy ? 1 : 0;
    }

    public HealthCheckState UpdateHealth(HealthStatus newHealth)
    {
        lock (_lock)
        {
            _previousHealth = _currentHealth;
            _currentHealth = newHealth;
            _lastCheckTime = DateTime.UtcNow;

            if (newHealth == HealthStatus.Healthy)
            {
                _consecutiveSuccesses++;
                _consecutiveFailures = 0;
            }
            else if (newHealth == HealthStatus.Unhealthy)
            {
                _consecutiveFailures++;
                _consecutiveSuccesses = 0;
            }
            else
            {
                // 对于Unknown或Degraded状态，重置计数器
                _consecutiveSuccesses = 0;
                _consecutiveFailures = 0;
            }
        }

        return this;
    }
}

/// <summary>
/// 健康检查结果
/// </summary>
public record HealthCheckResult
{
    /// <summary>
    /// 服务ID
    /// </summary>
    public string ServiceId { get; init; } = string.Empty;

    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; init; } = string.Empty;

    /// <summary>
    /// 当前健康状态
    /// </summary>
    public HealthStatus CurrentHealth { get; init; }

    /// <summary>
    /// 是否健康
    /// </summary>
    public bool IsHealthy { get; init; }

    /// <summary>
    /// 连续失败次数
    /// </summary>
    public int ConsecutiveFailures { get; init; }

    /// <summary>
    /// 连续成功次数
    /// </summary>
    public int ConsecutiveSuccesses { get; init; }

    /// <summary>
    /// 错误信息（如果有）
    /// </summary>
    public string? Error { get; init; }
}
