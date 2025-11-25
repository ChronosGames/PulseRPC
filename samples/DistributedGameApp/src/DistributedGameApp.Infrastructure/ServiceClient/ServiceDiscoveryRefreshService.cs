using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.Infrastructure.ServiceClient;

/// <summary>
/// 服务发现刷新后台服务
/// 定期刷新服务列表，确保连接池中的服务实例是最新的
/// </summary>
public class ServiceDiscoveryRefreshService : BackgroundService
{
    private readonly UnifiedServiceClientManager _serviceClientManager;
    private readonly ILogger<ServiceDiscoveryRefreshService> _logger;
    private readonly TimeSpan _refreshInterval;

    public ServiceDiscoveryRefreshService(
        UnifiedServiceClientManager serviceClientManager,
        ILogger<ServiceDiscoveryRefreshService> logger,
        TimeSpan? refreshInterval = null)
    {
        _serviceClientManager = serviceClientManager ?? throw new ArgumentNullException(nameof(serviceClientManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _refreshInterval = refreshInterval ?? TimeSpan.FromSeconds(30); // 默认30秒刷新一次
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 等待初始化完成
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        _logger.LogInformation("服务发现刷新服务已启动，刷新间隔: {Interval}s", _refreshInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_refreshInterval, stoppingToken);

                _logger.LogDebug("开始刷新服务列表...");

                // 获取所有已注册的服务类型
                var stats = _serviceClientManager.GetStats();

                foreach (var serverType in stats.RegisteredServerTypes)
                {
                    try
                    {
                        await _serviceClientManager.RefreshServerTypeAsync(serverType, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "刷新服务类型失败: {ServerType}", serverType);
                    }
                }

                _logger.LogDebug("服务列表刷新完成");
            }
            catch (OperationCanceledException)
            {
                // 正常停止
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "服务发现刷新服务异常");
            }
        }

        _logger.LogInformation("服务发现刷新服务已停止");
    }
}
