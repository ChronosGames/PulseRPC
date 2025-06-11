using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseServiceDiscovery.Abstractions;
using PulseServiceDiscovery.Abstractions.Models;
using PulseServiceDiscovery.Client.Options;
using System.Collections.Concurrent;
using System.Net.NetworkInformation;

namespace PulseServiceDiscovery.Client.HealthCheck;

/// <summary>
/// 健康检查器实现
/// </summary>
public class HealthChecker : IHealthChecker
{
    private readonly ILogger<HealthChecker> _logger;
    private readonly HealthCheckOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, EndpointHealthState> _healthStates = new();
    private readonly Timer _healthCheckTimer;
    private readonly SemaphoreSlim _checkSemaphore;

    public HealthChecker(
        ILogger<HealthChecker> logger,
        IOptions<ClientOptions> clientOptions,
        HttpClient? httpClient = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = clientOptions?.Value?.HealthCheck ?? throw new ArgumentNullException(nameof(clientOptions));

        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = _options.Timeout;

        _checkSemaphore = new SemaphoreSlim(_options.MaxConcurrentChecks, _options.MaxConcurrentChecks);

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

        var healthConfig = endpoint.HealthCheck;
        if (healthConfig == null)
        {
            _logger.LogDebug("No health check configuration for endpoint: {Endpoint}", endpoint.Address);
            return HealthStatus.Unknown;
        }

        return healthConfig.Type.ToLowerInvariant() switch
        {
            "http" => await CheckHttpHealthAsync(endpoint, healthConfig, cancellationToken),
            "tcp" => await CheckTcpHealthAsync(endpoint, healthConfig, cancellationToken),
            "ping" => await CheckPingHealthAsync(endpoint, healthConfig, cancellationToken),
            _ => await CheckCustomHealthAsync(endpoint, healthConfig, cancellationToken)
        };
    }

    public async Task<Dictionary<string, HealthStatus>> CheckHealthAsync(
        IEnumerable<ServiceEndpoint> endpoints,
        CancellationToken cancellationToken = default)
    {
        if (endpoints == null)
            throw new ArgumentNullException(nameof(endpoints));

        var results = new ConcurrentDictionary<string, HealthStatus>();
        var tasks = endpoints.Select(async endpoint =>
        {
            await _checkSemaphore.WaitAsync(cancellationToken);
            try
            {
                var health = await CheckHealthAsync(endpoint, cancellationToken);
                results[endpoint.Id] = health;

                // 更新健康状态
                UpdateHealthState(endpoint.Id, health);
            }
            finally
            {
                _checkSemaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
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
            var url = config.Path ?? $"http://{endpoint.Host}:{endpoint.Port}/health";
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
        _logger.LogWarning("Custom health check type '{Type}' is not implemented for endpoint: {Endpoint}",
            config.Type, endpoint.Address);

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
        _healthCheckTimer?.Dispose();
        _httpClient?.Dispose();
        _checkSemaphore?.Dispose();
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
