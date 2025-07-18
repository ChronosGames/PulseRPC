using Consul;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.ServiceDiscovery;
using System.Collections.Concurrent;

namespace PulseRPC.Infrastructure.Consul;

/// <summary>
/// Consul服务监听器
/// </summary>
public class ConsulServiceWatcher : BackgroundService
{
    private readonly IConsulClient _consulClient;
    private readonly ILogger<ConsulServiceWatcher> _logger;
    private readonly ConsulOptions _options;
    private readonly ConcurrentDictionary<string, ServiceWatchContext> _watchContexts = new();

    public ConsulServiceWatcher(
        IConsulClient consulClient,
        ILogger<ConsulServiceWatcher> logger,
        IOptions<ConsulOptions> options)
    {
        _consulClient = consulClient ?? throw new ArgumentNullException(nameof(consulClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// 服务变更事件
    /// </summary>
    public event Func<ServiceChangedEventArgs, Task>? ServiceChanged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.DiscoveryOptions.EnableWatching)
        {
            _logger.LogInformation("Service watching is disabled");
            return;
        }

        _logger.LogInformation("Starting Consul service watcher");

        // 预加载指定的服务
        if (_options.DiscoveryOptions.PreloadServices != null)
        {
            foreach (var serviceName in _options.DiscoveryOptions.PreloadServices)
            {
                await StartWatchingServiceAsync(serviceName, stoppingToken);
            }
        }

        // 定期刷新服务列表
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshServicesAsync(stoppingToken);
                await Task.Delay(_options.DiscoveryOptions.RefreshInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh services");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        _logger.LogInformation("Consul service watcher stopped");
    }

    /// <summary>
    /// 开始监听指定服务
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task StartWatchingServiceAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));
        }

        if (_watchContexts.ContainsKey(serviceName))
        {
            _logger.LogDebug("Service {ServiceName} is already being watched", serviceName);
            return;
        }

        var context = new ServiceWatchContext
        {
            ServiceName = serviceName,
            CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
        };

        if (_watchContexts.TryAdd(serviceName, context))
        {
            _logger.LogInformation("Starting to watch service: {ServiceName}", serviceName);
            
            // 启动监听任务
            _ = Task.Run(async () => await WatchServiceAsync(context), cancellationToken);
        }
    }

    /// <summary>
    /// 停止监听指定服务
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    public async Task StopWatchingServiceAsync(string serviceName)
    {
        if (_watchContexts.TryRemove(serviceName, out var context))
        {
            _logger.LogInformation("Stopping watch for service: {ServiceName}", serviceName);
            context.CancellationTokenSource.Cancel();
            context.CancellationTokenSource.Dispose();
            
            await Task.Delay(100); // 给监听任务一些时间来清理
        }
    }

    private async Task RefreshServicesAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 获取所有服务
            var response = await _consulClient.Catalog.Services(cancellationToken);
            var discoveredServices = response.Response.Keys.ToHashSet();

            // 开始监听新发现的服务
            foreach (var serviceName in discoveredServices)
            {
                if (!_watchContexts.ContainsKey(serviceName))
                {
                    await StartWatchingServiceAsync(serviceName, cancellationToken);
                }
            }

            // 停止监听已不存在的服务
            var watchedServices = _watchContexts.Keys.ToList();
            foreach (var serviceName in watchedServices)
            {
                if (!discoveredServices.Contains(serviceName))
                {
                    await StopWatchingServiceAsync(serviceName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh services");
        }
    }

    private async Task WatchServiceAsync(ServiceWatchContext context)
    {
        var serviceName = context.ServiceName;
        var cancellationToken = context.CancellationTokenSource.Token;
        ulong waitIndex = 0;

        _logger.LogDebug("Started watching service: {ServiceName}", serviceName);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // 使用阻塞查询监听服务变化
                var queryOptions = new QueryOptions
                {
                    WaitIndex = waitIndex,
                    WaitTime = _options.DiscoveryOptions.WatchTimeout
                };

                var response = await _consulClient.Health.Service(
                    serviceName,
                    string.Empty,
                    _options.DiscoveryOptions.HealthyOnly,
                    queryOptions,
                    cancellationToken);

                // 检查是否有变化
                if (response.LastIndex > waitIndex)
                {
                    waitIndex = response.LastIndex;
                    
                    // 转换服务实例
                    var serviceInstances = response.Response
                        .Select(ConvertToServiceInstance)
                        .Where(instance => instance != null)
                        .ToList();

                    // 触发服务变更事件
                    if (ServiceChanged != null)
                    {
                        var eventArgs = new ServiceChangedEventArgs
                        {
                            ServiceName = serviceName,
                            Instances = serviceInstances!,
                            Timestamp = DateTime.UtcNow
                        };

                        await ServiceChanged(eventArgs);
                    }

                    _logger.LogDebug("Service {ServiceName} changed, {Count} instances", 
                        serviceName, serviceInstances.Count);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to watch service: {ServiceName}", serviceName);
                
                // 等待一段时间后重试
                try
                {
                    await Task.Delay(_options.DiscoveryOptions.WatchRetryInterval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogDebug("Stopped watching service: {ServiceName}", serviceName);
    }

    private ServiceInstance? ConvertToServiceInstance(ServiceEntry consulService)
    {
        try
        {
            var service = consulService.Service;
            if (service == null) return null;

            var metadata = new Dictionary<string, object>();
            if (service.Meta != null)
            {
                foreach (var meta in service.Meta)
                {
                    metadata[meta.Key] = meta.Value;
                }
            }

            return new ServiceInstance
            {
                ServiceId = service.ID,
                ServiceName = service.Service,
                Address = service.Address,
                Port = service.Port,
                Tags = service.Tags?.ToList() ?? new List<string>(),
                Metadata = metadata,
                Health = DetermineHealthStatus(consulService.Checks)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to convert service entry: {ServiceId}", 
                consulService.Service?.ID);
            return null;
        }
    }

    private string DetermineHealthStatus(global::Consul.HealthCheck[] checks)
    {
        if (checks == null || checks.Length == 0)
            return "unknown";

        // 如果有任何检查失败，则认为不健康
        if (checks.Any(check => check.Status == HealthStatus.Critical))
            return "critical";

        if (checks.Any(check => check.Status == HealthStatus.Warning))
            return "warning";

        return checks.All(check => check.Status == HealthStatus.Passing) 
            ? "passing" 
            : "unknown";
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Consul service watcher");

        // 停止所有监听
        var stopTasks = _watchContexts.Keys.Select(StopWatchingServiceAsync);
        await Task.WhenAll(stopTasks);

        _watchContexts.Clear();
        await base.StopAsync(cancellationToken);
    }
}

/// <summary>
/// 服务监听上下文
/// </summary>
internal class ServiceWatchContext
{
    public string ServiceName { get; set; } = string.Empty;
    public CancellationTokenSource CancellationTokenSource { get; set; } = new();
}

/// <summary>
/// 服务变更事件参数
/// </summary>
public class ServiceChangedEventArgs
{
    public string ServiceName { get; set; } = string.Empty;
    public IList<ServiceInstance> Instances { get; set; } = new List<ServiceInstance>();
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 服务实例信息
/// </summary>
public class ServiceInstance
{
    public string ServiceId { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; }
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
    public string Health { get; set; } = "unknown";
}