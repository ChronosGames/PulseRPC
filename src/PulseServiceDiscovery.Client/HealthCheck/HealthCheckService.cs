using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseServiceDiscovery.Abstractions;
using PulseServiceDiscovery.Abstractions.Events;
using PulseServiceDiscovery.Abstractions.Models;
using PulseServiceDiscovery.Client.Options;
using System.Collections.Concurrent;

namespace PulseServiceDiscovery.Client.HealthCheck;

/// <summary>
/// 健康检查服务 - 后台服务，定期检查所有端点的健康状态
/// </summary>
public class HealthCheckService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HealthCheckService> _logger;
    private readonly HealthCheckOptions _options;
    private readonly ConcurrentDictionary<string, List<ServiceEndpoint>> _serviceEndpoints = new();
    private readonly ConcurrentDictionary<string, HealthStatus> _lastHealthStates = new();

    // 事件
    public event Func<ServiceHealthChangedEvent, Task>? HealthChanged;

    public HealthCheckService(
        IServiceProvider serviceProvider,
        ILogger<HealthCheckService> logger,
        IOptions<ClientOptions> clientOptions)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = clientOptions?.Value?.HealthCheckOptions ?? throw new ArgumentNullException(nameof(clientOptions));
    }

    /// <summary>
    /// 注册服务端点进行健康检查
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="endpoints">端点列表</param>
    public void RegisterEndpoints(string serviceName, IEnumerable<ServiceEndpoint> endpoints)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        if (endpoints == null)
            throw new ArgumentNullException(nameof(endpoints));

        var endpointList = endpoints.ToList(); // 所有端点都可以进行健康检查

        _serviceEndpoints.AddOrUpdate(serviceName, endpointList, (_, _) => endpointList);

        _logger.LogInformation("Registered {Count} endpoints for health checking in service: {ServiceName}",
            endpointList.Count, serviceName);
    }

    /// <summary>
    /// 取消注册服务的健康检查
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    public void UnregisterService(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        if (_serviceEndpoints.TryRemove(serviceName, out var endpoints))
        {
            // 清理健康状态缓存
            foreach (var endpoint in endpoints)
            {
                _lastHealthStates.TryRemove(endpoint.Id, out _);
            }

            _logger.LogInformation("Unregistered health checking for service: {ServiceName}", serviceName);
        }
    }

    /// <summary>
    /// 获取指定端点的健康状态
    /// </summary>
    /// <param name="endpointId">端点ID</param>
    /// <returns>健康状态</returns>
    public HealthStatus GetEndpointHealth(string endpointId)
    {
        if (string.IsNullOrWhiteSpace(endpointId))
            throw new ArgumentException("Endpoint ID cannot be null or empty", nameof(endpointId));

        return _lastHealthStates.TryGetValue(endpointId, out var status)
            ? status
            : HealthStatus.Unknown;
    }

    /// <summary>
    /// 获取所有健康状态
    /// </summary>
    /// <returns>端点ID和健康状态的字典</returns>
    public Dictionary<string, HealthStatus> GetAllHealthStates()
    {
        return _lastHealthStates.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// 手动触发健康检查
    /// </summary>
    /// <param name="serviceName">服务名称，如果为null则检查所有服务</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task TriggerHealthCheckAsync(string? serviceName = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            // 检查所有服务
            var tasks = _serviceEndpoints.Keys.Select(service =>
                PerformHealthCheckForServiceAsync(service, cancellationToken));

            await Task.WhenAll(tasks);
        }
        else
        {
            // 检查指定服务
            await PerformHealthCheckForServiceAsync(serviceName, cancellationToken);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Health check service started");

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

                // 出错时等待较短时间后重试
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

        _logger.LogInformation("Health check service stopped");
    }

    private async Task PerformHealthChecksAsync(CancellationToken cancellationToken)
    {
        if (_serviceEndpoints.IsEmpty)
        {
            _logger.LogDebug("No services registered for health checking");
            return;
        }

        _logger.LogDebug("Starting health check cycle for {ServiceCount} services", _serviceEndpoints.Count);

        var tasks = _serviceEndpoints.Keys.Select(serviceName =>
            PerformHealthCheckForServiceAsync(serviceName, cancellationToken));

        await Task.WhenAll(tasks);

        _logger.LogDebug("Completed health check cycle");
    }

    private async Task PerformHealthCheckForServiceAsync(string serviceName, CancellationToken cancellationToken)
    {
        if (!_serviceEndpoints.TryGetValue(serviceName, out var endpoints) || !endpoints.Any())
        {
            _logger.LogDebug("No endpoints to check for service: {ServiceName}", serviceName);
            return;
        }

        try
        {
            // 使用作用域服务获取 IHealthChecker
            using var scope = _serviceProvider.CreateScope();
            var healthChecker = scope.ServiceProvider.GetRequiredService<IHealthChecker>();

            var healthResults = await healthChecker.CheckHealthAsync(endpoints.ToList(), cancellationToken);

            // 处理健康状态变化
            await ProcessHealthChangesAsync(healthResults);

            _logger.LogDebug("Health check completed for service: {ServiceName}, checked {EndpointCount} endpoints",
                serviceName, endpoints.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform health check for service: {ServiceName}", serviceName);
        }
    }

    private async Task ProcessHealthChangesAsync(
        IReadOnlyDictionary<ServiceEndpoint, HealthStatus> healthResults)
    {
        var healthChangedEvents = new List<ServiceHealthChangedEvent>();

        foreach (var kvp in healthResults)
        {
            var endpoint = kvp.Key;
            var newStatus = kvp.Value;

            var oldStatus = _lastHealthStates.GetValueOrDefault(endpoint.Id, HealthStatus.Unknown);

            // 更新健康状态
            _lastHealthStates[endpoint.Id] = newStatus;

            // 如果状态发生变化，触发事件
            if (oldStatus != newStatus)
            {
                var healthChangedEvent = ServiceHealthChangedEvent.Create(
                    serviceId: endpoint.Id,
                    serviceName: endpoint.ServiceName,
                    endpoint: endpoint,
                    oldStatus: oldStatus,
                    newStatus: newStatus,
                    source: "HealthCheckService");

                healthChangedEvents.Add(healthChangedEvent);

                _logger.LogInformation("Health status changed for endpoint {Endpoint}: {OldStatus} -> {NewStatus}",
                    endpoint.Address, oldStatus, newStatus);
            }
        }

        // 批量触发健康状态变化事件
        if (healthChangedEvents.Any() && HealthChanged != null)
        {
            var eventTasks = healthChangedEvents.Select(evt =>
                TriggerHealthChangedEventSafely(evt));

            await Task.WhenAll(eventTasks);
        }
    }

    private async Task TriggerHealthChangedEventSafely(ServiceHealthChangedEvent healthChangedEvent)
    {
        try
        {
            if (HealthChanged != null)
            {
                await HealthChanged.Invoke(healthChangedEvent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invoking health changed event for endpoint: {Endpoint}",
                healthChangedEvent.Endpoint?.Address ?? "Unknown");
        }
    }

    /// <summary>
    /// 获取健康检查统计信息
    /// </summary>
    /// <returns>统计信息</returns>
    public Dictionary<string, object> GetStatistics()
    {
        var totalEndpoints = _serviceEndpoints.Values.SelectMany(endpoints => endpoints).Count();

        return new Dictionary<string, object>
        {
            ["TotalServices"] = _serviceEndpoints.Count,
            ["TotalEndpoints"] = totalEndpoints,
            ["HealthyEndpoints"] = _lastHealthStates.Count(kvp => kvp.Value == HealthStatus.Healthy),
            ["UnhealthyEndpoints"] = _lastHealthStates.Count(kvp => kvp.Value == HealthStatus.Unhealthy),
            ["UnknownEndpoints"] = _lastHealthStates.Count(kvp => kvp.Value == HealthStatus.Unknown),
            ["DegradedEndpoints"] = _lastHealthStates.Count(kvp => kvp.Value == HealthStatus.Degraded),
            ["CheckInterval"] = _options.Interval.ToString(),
            ["CheckTimeout"] = _options.Timeout.ToString(),
            ["MaxConcurrentChecks"] = 10, // 使用固定值
            ["ServiceEndpoints"] = _serviceEndpoints.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Count)
        };
    }
}
