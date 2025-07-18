using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Client;
using PulseRPC.HealthCheck;
using PulseRPC.LoadBalancing;
using PulseRPC.Server;
using PulseRPC.ServiceDiscovery;
using PulseRPC.Infrastructure.Consul;

namespace PulseRPC.Samples.ConsulExample;

/// <summary>
/// PulseRPC Consul 服务发现示例
/// 演示如何使用 Consul 作为服务注册与发现中心
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== PulseRPC Consul 服务发现示例 ===");
        Console.WriteLine("注意：请确保 Consul 服务已在 localhost:8500 运行");
        Console.WriteLine();

        // 创建主机构建器
        var builder = Host.CreateApplicationBuilder(args);

        // 配置服务
        ConfigureServices(builder.Services);

        // 构建主机
        var host = builder.Build();

        try
        {
            // 运行示例
            await RunConsulExampleAsync(host.Services);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 示例执行失败: {ex.Message}");
            Console.WriteLine("请确保 Consul 服务正在运行并可访问");
        }

        Console.WriteLine("\n示例执行完毕，按任意键退出...");
        Console.ReadKey();
    }

    /// <summary>
    /// 配置服务 - 使用 Consul 作为服务发现中心
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        Console.WriteLine("📋 配置PulseRPC Consul服务...");

        // 1. 添加日志记录
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // 2. 配置 Consul 服务发现
        services.AddServiceDiscovery(options =>
        {
            // options.Address = "http://localhost:8500";
            // options.Datacenter = "dc1";
            // options.EnableHealthCheck = true;
            // options.HealthCheckInterval = TimeSpan.FromSeconds(10);
            // options.HealthCheckTimeout = TimeSpan.FromSeconds(5);
            // options.DeregisterOnShutdown = true;
        });

        // 3. 配置故障转移负载均衡
        services.AddLoadBalancing(options =>
        {
            options.Strategy = LoadBalancingStrategy.Failover;
        });

        // 4. 配置健康检查
        services.AddHealthCheck(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(3);
            options.RetryCount = 1;
            options.EnableConcurrentChecks = true;
        });

        // 5. 配置PulseRPC客户端
        services.AddPulseRpcClient(options =>
        {
            options.ServiceDiscoveryOptions = new()
            {
                RefreshInterval = TimeSpan.FromSeconds(15),
                CacheTimeout = TimeSpan.FromMinutes(2),
                EnableCaching = true
            };
            // options.LoadBalancingOptions = new()
            // {
            //     Strategy = PulseRPC.Client.LoadBalancing.LoadBalancingStrategy.Failover,
            //     EnableHealthCheck = true,
            //     HealthCheckInterval = TimeSpan.FromSeconds(15)
            // };
        });

        // 6. 配置PulseRPC服务器
        services.AddPulseRpcServer(options =>
        {
            options.IPAddress = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 8080).ToString();
        });
        // services.AddPulseRpcServer(options =>
        // {
        //     options.ListenEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 8080);
        //     options.ServiceRegistryOptions = new()
        //     {
        //         ServiceName = "WeatherService",
        //         ServiceVersion = "2.0.0",
        //         EnableHealthCheck = true,
        //         HealthCheckInterval = TimeSpan.FromSeconds(10),
        //         Tags = new Dictionary<string, string>
        //         {
        //             ["environment"] = "production",
        //             ["version"] = "2.0.0",
        //             ["region"] = "us-west-1",
        //             ["az"] = "us-west-1a"
        //         },
        //         Metadata = new Dictionary<string, object>
        //         {
        //             ["description"] = "天气预报服务",
        //             ["api_version"] = "v2",
        //             ["max_connections"] = 1000,
        //             ["features"] = new[] { "weather", "forecast", "alerts" }
        //         }
        //     };
        // });

        // 7. 注册示例服务
        services.AddPulseRpcService<IWeatherService, WeatherService>();
        services.AddPulseRpcService<INotificationService, NotificationService>();

        Console.WriteLine("✅ PulseRPC Consul服务配置完成");
        Console.WriteLine();
    }

    /// <summary>
    /// 运行 Consul 示例
    /// </summary>
    private static async Task RunConsulExampleAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();

        // 1. 服务注册示例
        await ServiceRegistrationExample(services, logger);

        // 2. 服务发现示例
        await ServiceDiscoveryExample(services, logger);

        // 3. 健康检查示例
        await HealthCheckExample(services, logger);

        // 4. 故障转移示例
        await FailoverExample(services, logger);

        // 5. 监听服务变化示例
        await WatchServicesExample(services, logger);
    }

    /// <summary>
    /// 服务注册示例
    /// </summary>
    private static async Task ServiceRegistrationExample(IServiceProvider services, ILogger logger)
    {
        logger.LogInformation("📝 === 服务注册示例 ===");

        var serviceRegistry = services.GetRequiredService<PulseRPC.ServiceDiscovery.IServiceRegistry>();

        // 创建服务端点
        var weatherEndpoint = new PulseRPC.ServiceDiscovery.ServiceEndpoint
        {
            ServiceId = "weather-service-001",
            ServiceName = "WeatherService",
            EndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("127.0.0.1"), 8080),
            Tags = new Dictionary<string, string>
            {
                ["version"] = "2.0.0",
                ["region"] = "us-west-1"
            },
            Metadata = new Dictionary<string, object>
            {
                ["health_check_url"] = "http://127.0.0.1:8080/health",
                ["max_connections"] = 1000
            }
        };

        // 注册服务
        await serviceRegistry.RegisterAsync(weatherEndpoint);
        logger.LogInformation("✅ 天气服务已注册到 Consul: {ServiceId}", weatherEndpoint.ServiceId);

        // 注册另一个实例
        var weatherEndpoint2 = new PulseRPC.ServiceDiscovery.ServiceEndpoint
        {
            ServiceId = "weather-service-002",
            ServiceName = "WeatherService",
            EndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("127.0.0.1"), 8081),
            Tags = new Dictionary<string, string>
            {
                ["version"] = "2.0.0",
                ["region"] = "us-west-1"
            }
        };

        await serviceRegistry.RegisterAsync(weatherEndpoint2);
        logger.LogInformation("✅ 天气服务第二个实例已注册: {ServiceId}", weatherEndpoint2.ServiceId);

        logger.LogInformation("✅ 服务注册示例完成\n");
    }

    /// <summary>
    /// 服务发现示例
    /// </summary>
    private static async Task ServiceDiscoveryExample(IServiceProvider services, ILogger logger)
    {
        logger.LogInformation("🔍 === 服务发现示例 ===");

        var serviceDiscovery = services.GetRequiredService<PulseRPC.ServiceDiscovery.IServiceDiscovery>();

        // 发现天气服务
        var weatherServices = await serviceDiscovery.DiscoverAsync("WeatherService");
        logger.LogInformation("从 Consul 发现天气服务实例数量: {Count}", weatherServices.Count);

        foreach (var service in weatherServices)
        {
            logger.LogInformation("  - {ServiceId}: {EndPoint}", service.ServiceId, service.EndPoint);
            if (service.Tags.Count > 0)
            {
                logger.LogInformation("    标签: {Tags}", string.Join(", ", service.Tags.Select(t => $"{t.Key}={t.Value}")));
            }
        }

        // 根据标签发现服务
        var regionTags = new Dictionary<string, string> { ["region"] = "us-west-1" };
        var regionalServices = await serviceDiscovery.DiscoverByTagsAsync("WeatherService", regionTags);
        logger.LogInformation("发现 us-west-1 区域的服务数量: {Count}", regionalServices.Count);

        logger.LogInformation("✅ 服务发现示例完成\n");
    }

    /// <summary>
    /// 健康检查示例
    /// </summary>
    private static async Task HealthCheckExample(IServiceProvider services, ILogger logger)
    {
        logger.LogInformation("🏥 === 健康检查示例 ===");

        var healthChecker = services.GetRequiredService<PulseRPC.ServiceDiscovery.IHealthChecker>();
        var serviceDiscovery = services.GetRequiredService<PulseRPC.ServiceDiscovery.IServiceDiscovery>();

        // 获取服务端点
        var services_list = await serviceDiscovery.DiscoverAsync("WeatherService");
        if (services_list.Count == 0)
        {
            logger.LogWarning("未找到WeatherService端点进行健康检查");
            return;
        }

        // 执行健康检查
        logger.LogInformation("执行健康检查...");
        var healthResults = await healthChecker.CheckHealthBatchAsync(services_list);

        foreach (var result in healthResults)
        {
            var status = result.Status == PulseRPC.ServiceDiscovery.HealthStatus.Healthy ? "✅" : "❌";
            logger.LogInformation("  {Status} {ServiceId}: {HealthStatus} (响应时间: {ResponseTime}ms)",
                status, result.ServiceId, result.Status, result.ResponseTime.TotalMilliseconds);
        }

        // 获取健康检查统计信息
        var stats = healthChecker.GetStatistics();
        logger.LogInformation("健康检查统计:");
        logger.LogInformation("  总检查次数: {TotalChecks}", stats.GetValueOrDefault("TotalChecks", 0));
        logger.LogInformation("  成功次数: {SuccessfulChecks}", stats.GetValueOrDefault("SuccessfulChecks", 0));
        logger.LogInformation("  平均响应时间: {AvgResponseTime}ms", stats.GetValueOrDefault("AverageResponseTime", 0));

        logger.LogInformation("✅ 健康检查示例完成\n");
    }

    /// <summary>
    /// 故障转移示例
    /// </summary>
    private static async Task FailoverExample(IServiceProvider services, ILogger logger)
    {
        logger.LogInformation("🔄 === 故障转移示例 ===");

        var serviceDiscovery = services.GetRequiredService<PulseRPC.ServiceDiscovery.IServiceDiscovery>();
        var loadBalancerFactory = services.GetRequiredService<PulseRPC.LoadBalancing.Extensions.ILoadBalancerFactory>();

        // 获取服务端点
        var endpoints = await serviceDiscovery.DiscoverAsync("WeatherService");
        if (endpoints.Count == 0)
        {
            logger.LogWarning("未找到WeatherService端点进行故障转移测试");
            return;
        }

        var failoverBalancer = loadBalancerFactory.Create(PulseRPC.Client.LoadBalancing.LoadBalancingStrategy.Failover);

        // 正常选择
        var context = new PulseRPC.Client.LoadBalancing.LoadBalancingContext
        {
            RequestId = Guid.NewGuid().ToString()
        };
        var selectedEndpoint = await failoverBalancer.SelectAsync(endpoints, context);
        logger.LogInformation("正常选择端点: {EndPoint}", selectedEndpoint?.EndPoint);

        // 模拟多次失败
        if (selectedEndpoint != null)
        {
            logger.LogInformation("模拟连续失败...");
            for (int i = 0; i < 3; i++)
            {
                failoverBalancer.ReportResult(selectedEndpoint,
                    PulseRPC.Client.LoadBalancing.LoadBalancingResult.ConnectionFailed,
                    TimeSpan.FromSeconds(5));
                logger.LogInformation("  第{Index}次失败报告", i + 1);
            }

            // 重新选择（应该选择其他端点）
            var newSelectedEndpoint = await failoverBalancer.SelectAsync(endpoints, context);
            logger.LogInformation("故障转移后选择端点: {EndPoint}", newSelectedEndpoint?.EndPoint);

            // 获取故障转移统计
            var failoverStats = failoverBalancer.GetStatistics();
            logger.LogInformation("故障转移统计:");
            foreach (var stat in failoverStats)
            {
                logger.LogInformation("  {Key}: {Value}", stat.Key, stat.Value);
            }
        }

        logger.LogInformation("✅ 故障转移示例完成\n");
    }

    /// <summary>
    /// 监听服务变化示例
    /// </summary>
    private static async Task WatchServicesExample(IServiceProvider services, ILogger logger)
    {
        logger.LogInformation("👀 === 监听服务变化示例 ===");

        var serviceDiscovery = services.GetRequiredService<PulseRPC.ServiceDiscovery.IServiceDiscovery>();

        // 监听服务变化（限制时间避免无限等待）
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            logger.LogInformation("开始监听 WeatherService 变化（10秒）...");

            await foreach (var serviceInstances in serviceDiscovery.WatchAsync("WeatherService", cts.Token))
            {
                logger.LogInformation("检测到服务变化，当前实例数量: {Count}", serviceInstances.Length);
                foreach (var instance in serviceInstances)
                {
                    logger.LogInformation("  - {ServiceId}: {EndPoint}", instance.ServiceId, instance.EndPoint);
                }

                // 只监听一次变化
                break;
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("监听超时，停止监听");
        }

        logger.LogInformation("✅ 监听服务变化示例完成\n");
    }
}

/// <summary>
/// 天气服务接口
/// </summary>
public interface IWeatherService
{
    Task<WeatherInfo> GetCurrentWeatherAsync(string city);
    Task<WeatherForecast[]> GetForecastAsync(string city, int days);
    Task<bool> SubscribeToAlertsAsync(string city, string userId);
}

/// <summary>
/// 天气服务实现
/// </summary>
public class WeatherService : IWeatherService
{
    private readonly ILogger<WeatherService> _logger;

    public WeatherService(ILogger<WeatherService> logger)
    {
        _logger = logger;
    }

    public async Task<WeatherInfo> GetCurrentWeatherAsync(string city)
    {
        _logger.LogInformation("获取城市当前天气: {City}", city);
        await Task.Delay(100);

        return new WeatherInfo
        {
            City = city,
            Temperature = Random.Shared.Next(-10, 35),
            Humidity = Random.Shared.Next(30, 90),
            Description = "晴朗",
            Timestamp = DateTime.UtcNow
        };
    }

    public async Task<WeatherForecast[]> GetForecastAsync(string city, int days)
    {
        _logger.LogInformation("获取城市天气预报: {City}, {Days}天", city, days);
        await Task.Delay(200);

        var forecasts = new WeatherForecast[days];
        for (int i = 0; i < days; i++)
        {
            forecasts[i] = new WeatherForecast
            {
                Date = DateTime.Today.AddDays(i),
                HighTemperature = Random.Shared.Next(20, 35),
                LowTemperature = Random.Shared.Next(-5, 15),
                Description = i % 2 == 0 ? "晴朗" : "多云"
            };
        }

        return forecasts;
    }

    public async Task<bool> SubscribeToAlertsAsync(string city, string userId)
    {
        _logger.LogInformation("订阅天气预警: {City}, 用户: {UserId}", city, userId);
        await Task.Delay(150);
        return true;
    }
}

/// <summary>
/// 通知服务接口
/// </summary>
public interface INotificationService
{
    Task SendNotificationAsync(string userId, string message);
    Task<bool> IsUserSubscribedAsync(string userId, string topic);
}

/// <summary>
/// 通知服务实现
/// </summary>
public class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
    }

    public async Task SendNotificationAsync(string userId, string message)
    {
        _logger.LogInformation("发送通知给用户 {UserId}: {Message}", userId, message);
        await Task.Delay(80);
    }

    public async Task<bool> IsUserSubscribedAsync(string userId, string topic)
    {
        _logger.LogInformation("检查用户订阅状态: {UserId}, 主题: {Topic}", userId, topic);
        await Task.Delay(50);
        return Random.Shared.NextDouble() > 0.3; // 70% 概率已订阅
    }
}

/// <summary>
/// 天气信息
/// </summary>
public class WeatherInfo
{
    public string City { get; set; } = string.Empty;
    public int Temperature { get; set; }
    public int Humidity { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 天气预报
/// </summary>
public class WeatherForecast
{
    public DateTime Date { get; set; }
    public int HighTemperature { get; set; }
    public int LowTemperature { get; set; }
    public string Description { get; set; } = string.Empty;
}
