using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.HealthCheck;
using PulseRPC.Configuration;
using PulseRPC.ServiceDiscovery;
using PulseRPC.ServiceRegistration;

namespace PulseRPC.Infrastructure.Consul;

/// <summary>
/// Consul健康检查后台服务
/// </summary>
public class ConsulHealthCheckService : BackgroundService
{
    private readonly IServiceDiscovery _serviceDiscovery;
    private readonly ILogger<ConsulHealthCheckService> _logger;
    private readonly ConsulOptions _options;
    private readonly IHealthChecker? _healthChecker;

    public ConsulHealthCheckService(
        IServiceDiscovery serviceDiscovery,
        ILogger<ConsulHealthCheckService> logger,
        IOptions<ConsulOptions> options,
        IHealthChecker? healthChecker = null)
    {
        _serviceDiscovery = serviceDiscovery ?? throw new ArgumentNullException(nameof(serviceDiscovery));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _healthChecker = healthChecker;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.HealthCheck.Enabled)
        {
            _logger.LogInformation("Health check is disabled");
            return;
        }

        _logger.LogInformation("Starting Consul health check service with interval: {Interval}", 
            _options.HealthCheck.Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformHealthCheckAsync(stoppingToken);
                await Task.Delay(_options.HealthCheck.Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // 正常关闭
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed");
                
                // 在发生错误时等待一段时间再重试
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

        _logger.LogInformation("Consul health check service stopped");
    }

    private async Task PerformHealthCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 获取所有已注册的服务
            var services = await _serviceDiscovery.GetServicesAsync("", cancellationToken);
            
            if (services.Count == 0)
            {
                _logger.LogDebug("No services registered for health check");
                return;
            }

            _logger.LogDebug("Performing health check for {Count} services", services.Count);

            // 对每个服务执行健康检查
            var healthCheckTasks = services.Select(service => 
                CheckServiceHealthAsync(service, cancellationToken));

            await Task.WhenAll(healthCheckTasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform health check");
        }
    }

    private async Task CheckServiceHealthAsync(ServiceEndpoint service, CancellationToken cancellationToken)
    {
        try
        {
            var isHealthy = await IsServiceHealthyAsync(service, cancellationToken);
            var status = isHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy;

            // 更新服务健康状态
            if (_serviceDiscovery is ConsulServiceDiscovery consulDiscovery)
            {
                await consulDiscovery.UpdateHealthAsync(service.ServiceId, status, cancellationToken);
            }

            _logger.LogDebug("Health check result for {ServiceId}: {Status}", 
                service.ServiceId, status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check health for service: {ServiceId}", service.ServiceId);
            
            // 在检查失败时标记为不健康
            try
            {
                if (_serviceDiscovery is ConsulServiceDiscovery consulDiscovery)
                {
                    await consulDiscovery.UpdateHealthAsync(service.ServiceId, HealthStatus.Unhealthy, cancellationToken);
                }
            }
            catch (Exception updateEx)
            {
                _logger.LogError(updateEx, "Failed to update health status for service: {ServiceId}", 
                    service.ServiceId);
            }
        }
    }

    private async Task<bool> IsServiceHealthyAsync(ServiceEndpoint service, CancellationToken cancellationToken)
    {
        // 如果有自定义的健康检查器，使用它
        if (_healthChecker != null)
        {
            var result = await _healthChecker.CheckHealthAsync(service, cancellationToken);
            return result.Status == HealthStatus.Healthy;
        }

        // 默认的健康检查逻辑
        return await DefaultHealthCheckAsync(service, cancellationToken);
    }

    private async Task<bool> DefaultHealthCheckAsync(ServiceEndpoint service, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new HttpClient
            {
                Timeout = _options.HealthCheck.Timeout
            };

            // 根据健康检查类型进行检查
            switch (_options.HealthCheck.CheckType.ToUpperInvariant())
            {
                case "HTTP":
                    return await HttpHealthCheckAsync(httpClient, service, cancellationToken);
                
                case "TCP":
                    return await TcpHealthCheckAsync(service, cancellationToken);
                
                case "TTL":
                    // TTL检查由服务自己发送心跳，这里总是返回true
                    return true;
                
                default:
                    _logger.LogWarning("Unknown health check type: {CheckType}", _options.HealthCheck.CheckType);
                    return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Default health check failed for service: {ServiceId}", service.ServiceId);
            return false;
        }
    }

    private async Task<bool> HttpHealthCheckAsync(HttpClient httpClient, ServiceEndpoint service, CancellationToken cancellationToken)
    {
        try
        {
            var healthPath = _options.HealthCheck.HttpPath ?? "/health";
            var scheme = _options.Security.EnableTls ? "https" : "http";
            var healthUrl = $"{scheme}://{service.Host}:{service.Port}{healthPath}";

            // 添加自定义头部
            if (_options.HealthCheck.HttpHeaders != null)
            {
                foreach (var header in _options.HealthCheck.HttpHeaders)
                {
                    httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
                }
            }

            var response = await httpClient.GetAsync(healthUrl, cancellationToken);
            var isHealthy = response.IsSuccessStatusCode;

            _logger.LogDebug("HTTP health check for {ServiceId} at {Url}: {StatusCode}", 
                service.ServiceId, healthUrl, response.StatusCode);

            return isHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "HTTP health check failed for service: {ServiceId}", service.ServiceId);
            return false;
        }
    }

    private async Task<bool> TcpHealthCheckAsync(ServiceEndpoint service, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(service.Host, service.Port, cancellationToken);
            
            var isHealthy = client.Connected;
            _logger.LogDebug("TCP health check for {ServiceId} at {EndPoint}: {Connected}", 
                service.ServiceId, $"{service.Host}:{service.Port}", isHealthy);

            return isHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TCP health check failed for service: {ServiceId}", service.ServiceId);
            return false;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Consul health check service");
        await base.StopAsync(cancellationToken);
    }
}