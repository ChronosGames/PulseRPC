using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseServiceDiscovery.Abstractions;
using PulseServiceDiscovery.Server.HealthCheck;
using PulseServiceDiscovery.Server.Options;
using PulseServiceDiscovery.Server.Services;
using PulseServiceDiscovery.Server.Storage;

namespace PulseServiceDiscovery.Server.Extensions;

/// <summary>
/// 服务端ServiceCollection扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加服务发现服务端
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置选项</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddServiceDiscoveryServer(
        this IServiceCollection services,
        Action<ServerOptions>? configureOptions = null)
    {
        // 配置选项
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // 注册核心服务
        services.TryAddSingleton<IServiceRegistry, ServiceRegistry>();

        // 默认使用内存存储
        services.TryAddSingleton<IServiceStorage, MemoryServiceStorage>();

        // 注册健康检查相关服务
        services.TryAddSingleton<IHealthChecker, PulseServiceDiscovery.Client.HealthCheck.HealthChecker>();
        services.TryAddSingleton<ServerHealthChecker>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService>(provider =>
            provider.GetRequiredService<ServerHealthChecker>()));

        return services;
    }

    /// <summary>
    /// 使用内存存储
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection UseMemoryStorage(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IServiceStorage, MemoryServiceStorage>());
        return services;
    }

    /// <summary>
    /// 使用文件存储
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="filePath">文件路径（可选）</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection UseFileStorage(
        this IServiceCollection services,
        string? filePath = null)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            services.Configure<ServerOptions>(options =>
            {
                options.Storage.Type = "File";
                options.Storage.FilePath = filePath;
            });
        }

        services.Replace(ServiceDescriptor.Singleton<IServiceStorage, FileServiceStorage>());
        return services;
    }

    /// <summary>
    /// 配置存储选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureStorage">存储配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureStorage(
        this IServiceCollection services,
        Action<StorageOptions> configureStorage)
    {
        services.Configure<ServerOptions>(options =>
        {
            configureStorage(options.Storage);
        });

        return services;
    }

    /// <summary>
    /// 配置服务端健康检查选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureHealthCheck">健康检查配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureServerHealthCheck(
        this IServiceCollection services,
        Action<ServerHealthCheckOptions> configureHealthCheck)
    {
        services.Configure<ServerOptions>(options =>
        {
            configureHealthCheck(options.HealthCheck);
        });

        return services;
    }

    /// <summary>
    /// 配置事件选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureEvents">事件配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureEvents(
        this IServiceCollection services,
        Action<EventOptions> configureEvents)
    {
        services.Configure<ServerOptions>(options =>
        {
            configureEvents(options.Events);
        });

        return services;
    }

    /// <summary>
    /// 配置清理选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureCleanup">清理配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureCleanup(
        this IServiceCollection services,
        Action<CleanupOptions> configureCleanup)
    {
        services.Configure<ServerOptions>(options =>
        {
            configureCleanup(options.Cleanup);
        });

        return services;
    }

    /// <summary>
    /// 添加自定义存储实现
    /// </summary>
    /// <typeparam name="TStorage">存储实现类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddCustomStorage<TStorage>(this IServiceCollection services)
        where TStorage : class, IServiceStorage
    {
        services.Replace(ServiceDescriptor.Singleton<IServiceStorage, TStorage>());
        return services;
    }

    /// <summary>
    /// 添加自定义服务注册器
    /// </summary>
    /// <typeparam name="TRegistry">注册器类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddCustomServiceRegistry<TRegistry>(this IServiceCollection services)
        where TRegistry : class, IServiceRegistry
    {
        services.Replace(ServiceDescriptor.Singleton<IServiceRegistry, TRegistry>());
        return services;
    }

    /// <summary>
    /// 禁用服务端健康检查
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection DisableServerHealthCheck(this IServiceCollection services)
    {
        services.Configure<ServerOptions>(options =>
        {
            options.HealthCheck.Enabled = false;
        });

        return services;
    }

    /// <summary>
    /// 禁用自动清理
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection DisableAutoCleanup(this IServiceCollection services)
    {
        services.Configure<ServerOptions>(options =>
        {
            options.Cleanup.Enabled = false;
        });

        return services;
    }

    /// <summary>
    /// 启用服务过期清理
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="expiration">过期时间（可选）</param>
    /// <param name="interval">清理间隔（可选）</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection EnableServiceExpiration(
        this IServiceCollection services,
        TimeSpan? expiration = null,
        TimeSpan? interval = null)
    {
        services.Configure<ServerOptions>(options =>
        {
            options.Cleanup.Enabled = true;

            if (expiration.HasValue)
            {
                options.Cleanup.ServiceExpiration = expiration.Value;
            }

            if (interval.HasValue)
            {
                options.Cleanup.Interval = interval.Value;
            }
        });

        return services;
    }

    /// <summary>
    /// 添加清理服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddCleanupService(this IServiceCollection services)
    {
        services.TryAddSingleton<CleanupService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService>(provider =>
            provider.GetRequiredService<CleanupService>()));

        return services;
    }
}

/// <summary>
/// 清理服务 - 定期清理过期的服务注册
/// </summary>
public class CleanupService : BackgroundService
{
    private readonly IServiceRegistry _serviceRegistry;
    private readonly Microsoft.Extensions.Logging.ILogger<CleanupService> _logger;
    private readonly CleanupOptions _options;

    public CleanupService(
        IServiceRegistry serviceRegistry,
        Microsoft.Extensions.Logging.ILogger<CleanupService> logger,
        Microsoft.Extensions.Options.IOptions<ServerOptions> options)
    {
        _serviceRegistry = serviceRegistry ?? throw new ArgumentNullException(nameof(serviceRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value?.Cleanup ?? throw new ArgumentNullException(nameof(options));
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
