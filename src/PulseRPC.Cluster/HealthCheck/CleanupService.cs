using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.ServiceDiscovery;
using PulseRPC.ServiceRegistration;

namespace PulseRPC.HealthCheck;

/// <summary>
/// 清理服务 - 定期清理过期的服务注册
/// </summary>
public class CleanupService : BackgroundService
{
    private readonly IServiceRegistry _serviceRegistry;
    private readonly ILogger<CleanupService> _logger;
    private readonly CleanupOptions _options;

    public CleanupService(
        IServiceRegistry serviceRegistry,
        ILogger<CleanupService> logger,
        IOptions<CleanupOptions> options)
    {
        _serviceRegistry = serviceRegistry ?? throw new ArgumentNullException(nameof(serviceRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Cleanup service is disabled");
            return;
        }

        _logger.LogInformation("Cleanup service started with interval: {Interval}, expiration: {Expiration}",
            _options.Interval, _options.ServiceExpiration);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformCleanupAsync(stoppingToken);
                await Task.Delay(_options.Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup cycle");

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Cleanup service stopped");
    }

    private async Task PerformCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 使用ServiceRegistry自己的清理方法
            if (_serviceRegistry is ServiceRegistry serviceRegistry)
            {
                await serviceRegistry.CleanupExpiredServicesAsync(cancellationToken);
            }
            else
            {
                // 如果是自定义的注册器，手动实现清理逻辑
                await PerformManualCleanupAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing cleanup");
        }
    }

    private async Task PerformManualCleanupAsync(CancellationToken cancellationToken)
    {
        var allServices = await _serviceRegistry.GetRegistrationsAsync(cancellationToken);
        var expiredThreshold = DateTime.UtcNow - _options.ServiceExpiration;
        var expiredServices = allServices.Where(s => s.LastHeartbeat < expiredThreshold).ToList();

        if (expiredServices.Any())
        {
            _logger.LogInformation("Found {Count} expired services to cleanup", expiredServices.Count);

            foreach (var service in expiredServices)
            {
                try
                {
                    await _serviceRegistry.UnregisterAsync(service.Id, cancellationToken);
                    _logger.LogDebug("Cleaned up expired service: {ServiceName} (ID: {ServiceId})",
                        service.ServiceName, service.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to cleanup expired service: {ServiceId}", service.Id);
                }
            }
        }
    }
}
