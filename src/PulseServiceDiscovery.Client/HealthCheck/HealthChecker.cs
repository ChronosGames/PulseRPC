using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseServiceDiscovery.Abstractions;
using PulseServiceDiscovery.Abstractions.Models;
using PulseServiceDiscovery.Abstractions.Events;
using PulseServiceDiscovery.Client.Options;
using System.Collections.Concurrent;
using System.Net.NetworkInformation;

namespace PulseServiceDiscovery.Client.HealthCheck;

/// <summary>
/// 健康检查器实现 - 事件驱动架构
/// </summary>
public class HealthChecker : IHealthChecker, IDisposable
{
    private readonly ILogger<HealthChecker> _logger;
    private readonly HealthCheckOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, EndpointHealthState> _healthStates = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _monitoringTasks = new();
    private readonly Timer _healthCheckTimer;
    private readonly SemaphoreSlim _checkSemaphore;
    private bool _disposed = false;

    /// <summary>
    /// 健康状态变化事件
    /// </summary>
    public event Func<ServiceHealthChangedEvent, Task>? HealthChanged;

    public HealthChecker(
        ILogger<HealthChecker> logger,
        IOptions<ClientOptions> clientOptions,
        HttpClient? httpClient = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = clientOptions?.Value?.HealthCheckOptions ?? throw new ArgumentNullException(nameof(clientOptions));

        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = _options.Timeout;

        _checkSemaphore = new SemaphoreSlim(10, 10); // 使用固定的最大并发检查数

        // 启动定时健康检查
        _healthCheckTimer = new Timer(PerformScheduledHealthChecks, null,
            _options.Interval, _options.Interval);

        _logger.LogInformation("Health checker initialized with interval: {Interval}, timeout: {Timeout}",
            _options.Interval, _options.Timeout);
    }

    public async Task<HealthStatus> CheckHealthAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        if (endpoint == null)
            throw new ArgumentNullException(nameof(endpoint));

        // 从元数据中获取健康检查配置，或使用默认配置
        var healthCheckType = endpoint.GetMetadata("health.type", "tcp");
        var healthCheckPath = endpoint.GetMetadata("health.path");

        var healthConfig = new HealthCheckConfig
        {
            Url = healthCheckPath != null ? $"http://{endpoint.Host}:{endpoint.Port}{healthCheckPath}" : null,
            Interval = _options.Interval,
            Timeout = _options.Timeout,
            FailureThreshold = _options.FailureThreshold,
            SuccessThreshold = _options.SuccessThreshold
        };

        var result = healthCheckType?.ToLowerInvariant() switch
        {
            "http" => await CheckHttpHealthAsync(endpoint, healthConfig, cancellationToken),
            "tcp" => await CheckTcpHealthAsync(endpoint, healthConfig, cancellationToken),
            "ping" => await CheckPingHealthAsync(endpoint, healthConfig, cancellationToken),
            _ => await CheckTcpHealthAsync(endpoint, healthConfig, cancellationToken) // 默认使用TCP检查
        };

        // 更新健康状态并触发事件
        await UpdateHealthStateAndNotifyAsync(endpoint, result);

        return result;
    }

    /// <summary>
    /// 批量检查服务端点健康状态 - 接口兼容版本
    /// </summary>
    public async Task<IReadOnlyDictionary<ServiceEndpoint, HealthStatus>> CheckHealthAsync(
        IReadOnlyList<ServiceEndpoint> endpoints,
        CancellationToken cancellationToken = default)
    {
        if (endpoints == null)
            throw new ArgumentNullException(nameof(endpoints));

        var results = new ConcurrentDictionary<ServiceEndpoint, HealthStatus>();
        var tasks = endpoints.Select(async endpoint =>
        {
            await _checkSemaphore.WaitAsync(cancellationToken);
            try
            {
                var health = await CheckHealthAsync(endpoint, cancellationToken);
                results[endpoint] = health;
            }
            finally
            {
                _checkSemaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results;
    }

    /// <summary>
    /// 启动健康检查监控
    /// </summary>
    public Task StartMonitoringAsync(ServiceEndpoint endpoint, TimeSpan interval, Action<ServiceEndpoint, HealthStatus> onHealthChanged, CancellationToken cancellationToken = default)
    {
        if (endpoint == null)
            throw new ArgumentNullException(nameof(endpoint));

        var endpointKey = endpoint.Id;

        // 如果已经在监控，先停止
        if (_monitoringTasks.ContainsKey(endpointKey))
        {
            _ = StopMonitoringAsync(endpoint);
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitoringTasks[endpointKey] = cts;

        // 启动监控任务
        _ = Task.Run(async () =>
        {
            _logger.LogInformation("Started monitoring endpoint: {Endpoint} with interval: {Interval}",
                endpoint.Address, interval);

            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var health = await CheckHealthAsync(endpoint, cts.Token);
                        onHealthChanged?.Invoke(endpoint, health);

                        // 发布事件
                        if (HealthChanged != null)
                        {
                            var eventArgs = ServiceHealthChangedEvent.Create(
                                endpoint.Id,
                                endpoint.ServiceName,
                                endpoint,
                                HealthStatus.Unknown,
                                health,
                                source: "HealthChecker.StartMonitoringAsync");
                            await HealthChanged.Invoke(eventArgs);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error during health monitoring for endpoint: {Endpoint}", endpoint.Address);
                        onHealthChanged?.Invoke(endpoint, HealthStatus.Unhealthy);
                    }

                    await Task.Delay(interval, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            finally
            {
                _monitoringTasks.TryRemove(endpointKey, out _);
                _logger.LogInformation("Stopped monitoring endpoint: {Endpoint}", endpoint.Address);
            }
        }, cts.Token);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止健康检查监控
    /// </summary>
    public Task StopMonitoringAsync(ServiceEndpoint endpoint)
    {
        if (endpoint == null)
            throw new ArgumentNullException(nameof(endpoint));

        var endpointKey = endpoint.Id;

        if (_monitoringTasks.TryRemove(endpointKey, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _logger.LogInformation("Requested stop for monitoring endpoint: {Endpoint}", endpoint.Address);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 更新健康状态并发送事件通知
    /// </summary>
    private async Task UpdateHealthStateAndNotifyAsync(ServiceEndpoint endpoint, HealthStatus newStatus)
    {
        var endpointKey = endpoint.Id;
        var previousStatus = _healthStates.TryGetValue(endpointKey, out var state)
            ? state.CurrentStatus
            : HealthStatus.Unknown;

        // 更新状态
        UpdateHealthState(endpointKey, newStatus);

        // 如果状态发生变化，发布事件
        if (previousStatus != newStatus && HealthChanged != null)
        {
            try
            {
                var eventArgs = ServiceHealthChangedEvent.Create(
                    endpoint.Id,
                    endpoint.ServiceName,
                    endpoint,
                    previousStatus,
                    newStatus,
                    source: "HealthChecker.UpdateHealthStateAndNotifyAsync");
                await HealthChanged.Invoke(eventArgs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing health changed event for endpoint: {Endpoint}", endpoint.Address);
            }
        }
    }

    public HealthStatus GetCachedHealth(string endpointId)
    {
        if (string.IsNullOrWhiteSpace(endpointId))
            throw new ArgumentException("Endpoint ID cannot be null or empty", nameof(endpointId));

        return _healthStates.TryGetValue(endpointId, out var state)
            ? state.CurrentStatus
            : HealthStatus.Unknown;
    }

    public Dictionary<string, object> GetHealthStatistics()
    {
        return new Dictionary<string, object>
        {
            ["TotalEndpoints"] = _healthStates.Count,
            ["HealthyEndpoints"] = _healthStates.Count(kvp => kvp.Value.CurrentStatus == HealthStatus.Healthy),
            ["UnhealthyEndpoints"] = _healthStates.Count(kvp => kvp.Value.CurrentStatus == HealthStatus.Unhealthy),
            ["UnknownEndpoints"] = _healthStates.Count(kvp => kvp.Value.CurrentStatus == HealthStatus.Unknown),
            ["DegradedEndpoints"] = _healthStates.Count(kvp => kvp.Value.CurrentStatus == HealthStatus.Degraded),
            ["HealthStates"] = _healthStates.ToDictionary(
                kvp => kvp.Key,
                kvp => new Dictionary<string, object>
                {
                    ["Status"] = kvp.Value.CurrentStatus.ToString(),
                    ["LastCheck"] = kvp.Value.LastCheckTime,
                    ["ConsecutiveFailures"] = kvp.Value.ConsecutiveFailures,
                    ["ConsecutiveSuccesses"] = kvp.Value.ConsecutiveSuccesses
                })
        };
    }

    private async Task<HealthStatus> CheckHttpHealthAsync(
        ServiceEndpoint endpoint,
        HealthCheckConfig config,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = config.Url ?? $"http://{endpoint.Host}:{endpoint.Port}/health";
            var response = await _httpClient.GetAsync(url, cancellationToken);

            var isHealthy = response.IsSuccessStatusCode;
            _logger.LogDebug("HTTP health check for {Endpoint}: {Status} ({StatusCode})",
                endpoint.Address, isHealthy ? "Healthy" : "Unhealthy", response.StatusCode);

            return isHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HTTP health check failed for endpoint: {Endpoint}", endpoint.Address);
            return HealthStatus.Unhealthy;
        }
    }

    private async Task<HealthStatus> CheckTcpHealthAsync(
        ServiceEndpoint endpoint,
        HealthCheckConfig config,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var connectTask = client.ConnectAsync(endpoint.Host, endpoint.Port);
            var timeoutTask = Task.Delay(_options.Timeout, cancellationToken);

            var completedTask = await Task.WhenAny(connectTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _logger.LogWarning("TCP health check timeout for endpoint: {Endpoint}", endpoint.Address);
                return HealthStatus.Unhealthy;
            }

            var isHealthy = client.Connected;
            _logger.LogDebug("TCP health check for {Endpoint}: {Status}",
                endpoint.Address, isHealthy ? "Healthy" : "Unhealthy");

            return isHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TCP health check failed for endpoint: {Endpoint}", endpoint.Address);
            return HealthStatus.Unhealthy;
        }
    }

    private async Task<HealthStatus> CheckPingHealthAsync(
        ServiceEndpoint endpoint,
        HealthCheckConfig config,
        CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(endpoint.Host, (int)_options.Timeout.TotalMilliseconds);

            var isHealthy = reply.Status == IPStatus.Success;
            _logger.LogDebug("Ping health check for {Endpoint}: {Status} ({PingStatus})",
                endpoint.Address, isHealthy ? "Healthy" : "Unhealthy", reply.Status);

            return isHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ping health check failed for endpoint: {Endpoint}", endpoint.Address);
            return HealthStatus.Unhealthy;
        }
    }

    private async Task<HealthStatus> CheckCustomHealthAsync(
        ServiceEndpoint endpoint,
        HealthCheckConfig config,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning("Custom health check is not implemented for endpoint: {Endpoint}",
            endpoint.Address);

        // 默认返回未知状态，可以通过扩展来实现自定义健康检查
        await Task.CompletedTask;
        return HealthStatus.Unknown;
    }

    private void UpdateHealthState(string endpointId, HealthStatus status)
    {
        _healthStates.AddOrUpdate(endpointId,
            new EndpointHealthState(status),
            (_, existing) => existing.UpdateStatus(status));
    }

    private async void PerformScheduledHealthChecks(object? state)
    {
        if (_healthStates.IsEmpty)
            return;

        try
        {
            _logger.LogDebug("Performing scheduled health checks for {Count} endpoints", _healthStates.Count);

            // 这里需要获取所有端点信息，但我们只有端点ID
            // 在实际使用中，应该由HealthCheckService来管理完整的端点信息
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during scheduled health checks");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            // 停止所有监控任务
            foreach (var kvp in _monitoringTasks.ToList())
            {
                kvp.Value.Cancel();
                kvp.Value.Dispose();
            }
            _monitoringTasks.Clear();

            _healthCheckTimer?.Dispose();
            _httpClient?.Dispose();
            _checkSemaphore?.Dispose();

            _logger.LogDebug("HealthChecker disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing HealthChecker");
        }
        finally
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}

/// <summary>
/// 端点健康状态
/// </summary>
internal class EndpointHealthState
{
    private HealthStatus _currentStatus;
    private DateTime _lastCheckTime;
    private int _consecutiveFailures;
    private int _consecutiveSuccesses;
    private readonly object _lock = new();

    public HealthStatus CurrentStatus
    {
        get { lock (_lock) return _currentStatus; }
    }

    public DateTime LastCheckTime
    {
        get { lock (_lock) return _lastCheckTime; }
    }

    public int ConsecutiveFailures
    {
        get { lock (_lock) return _consecutiveFailures; }
    }

    public int ConsecutiveSuccesses
    {
        get { lock (_lock) return _consecutiveSuccesses; }
    }

    public EndpointHealthState(HealthStatus initialStatus)
    {
        _currentStatus = initialStatus;
        _lastCheckTime = DateTime.UtcNow;
        _consecutiveFailures = 0;
        _consecutiveSuccesses = 0;
    }

    public EndpointHealthState UpdateStatus(HealthStatus newStatus)
    {
        lock (_lock)
        {
            if (newStatus == HealthStatus.Healthy)
            {
                _consecutiveSuccesses++;
                _consecutiveFailures = 0;
            }
            else if (newStatus == HealthStatus.Unhealthy)
            {
                _consecutiveFailures++;
                _consecutiveSuccesses = 0;
            }

            _currentStatus = newStatus;
            _lastCheckTime = DateTime.UtcNow;
        }

        return this;
    }
}
