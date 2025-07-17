using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Client.Extensions;
using PulseRPC.LoadBalancing.Extensions;
using PulseRPC.ServiceDiscovery.Extensions;

namespace PulseRPC.Samples.DnsExample;

/// <summary>
/// PulseRPC DNS 服务发现示例
/// 演示如何使用 DNS 进行轻量级服务发现
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== PulseRPC DNS 服务发现示例 ===");
        Console.WriteLine("注意：本示例演示基于 DNS 的服务发现");
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
            await RunDnsExampleAsync(host.Services);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 示例执行失败: {ex.Message}");
            Console.WriteLine("请检查 DNS 配置和网络连接");
        }

        Console.WriteLine("\n示例执行完毕，按任意键退出...");
        Console.ReadKey();
    }

    /// <summary>
    /// 配置服务 - 使用 DNS 作为服务发现中心
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        Console.WriteLine("📋 配置PulseRPC DNS服务...");

        // 1. 添加日志记录
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // 2. 配置 DNS 服务发现
        services.AddDnsServiceDiscovery(options =>
        {
            options.DnsDomain = "example.com"; // 示例域名
            options.QueryType = PulseRPC.ServiceDiscovery.Implementations.DnsQueryType.Auto;
            options.Protocol = "tcp";
            options.DefaultPort = 8080;
            options.EnableIPv6 = false;
            options.EnableCaching = true;
            options.RefreshInterval = TimeSpan.FromMinutes(2);
            options.WatchPollInterval = TimeSpan.FromSeconds(15);
            options.QueryTimeout = TimeSpan.FromSeconds(5);
            options.EnableHealthCheck = true;
            options.HealthCheckTimeout = TimeSpan.FromSeconds(3);
        });

        // 3. 配置轮询负载均衡
        services.AddRoundRobinLoadBalancing();

        // 4. 配置健康检查
        services.AddHealthCheck(options =>
        {
            options.DefaultTimeout = TimeSpan.FromSeconds(3);
            options.RetryCount = 1;
            options.EnableConcurrentChecks = true;
        });

        // 5. 配置PulseRPC客户端
        services.AddPulseRpcClient(options =>
        {
            options.ServiceDiscoveryOptions = new()
            {
                RefreshInterval = TimeSpan.FromMinutes(2),
                CacheTimeout = TimeSpan.FromMinutes(5),
                EnableCaching = true
            };
            options.LoadBalancingOptions = new()
            {
                Strategy = PulseRPC.Client.LoadBalancing.LoadBalancingStrategy.RoundRobin,
                EnableHealthCheck = true,
                HealthCheckInterval = TimeSpan.FromSeconds(30)
            };
        });

        // 6. 添加服务发现工厂
        services.AddServiceDiscoveryFactory();

        // 7. 注册示例服务
        services.AddPulseRpcService<IWebService, WebService>();
        services.AddPulseRpcService<IDatabaseService, DatabaseService>();

        Console.WriteLine("✅ PulseRPC DNS服务配置完成");
        Console.WriteLine();
    }

    /// <summary>
    /// 运行 DNS 示例
    /// </summary>
    private static async Task RunDnsExampleAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();

        // 1. DNS 服务发现示例
        await DnsServiceDiscoveryExample(services, logger);

        // 2. 标签过滤示例
        await TagFilteringExample(services, logger);

        // 3. 健康检查示例
        await HealthCheckExample(services, logger);

        // 4. 负载均衡示例
        await LoadBalancingExample(services, logger);

        // 5. 监听服务变化示例
        await WatchServicesExample(services, logger);

        // 6. 服务工厂示例
        await ServiceFactoryExample(services, logger);

        // 7. 查询类型示例
        await QueryTypeExample(services, logger);
    }

    /// <summary>
    /// DNS 服务发现示例
    /// </summary>
    private static async Task DnsServiceDiscoveryExample(IServiceProvider services, ILogger logger)
    {
        logger.LogInformation("🔍 === DNS 服务发现示例 ===");

        var serviceDiscovery = services.GetRequiredService<PulseRPC.ServiceDiscovery.IServiceDiscovery>();

        // 发现常见的公共服务（用于演示）
        var testServices = new[] { "google.com", "github.com", "stackoverflow.com" };

        foreach (var serviceName in testServices)
        {
            try
            {
                var endpoints = await serviceDiscovery.DiscoverAsync(serviceName);
                logger.LogInformation("发现服务 {ServiceName}，端点数量: {Count}", serviceName, endpoints.Count);

                foreach (var endpoint in endpoints.Take(3)) // 限制显示数量
                {
                    logger.LogInformation("  - {ServiceId}: {EndPoint}", endpoint.ServiceId, endpoint.EndPoint);
                    if (endpoint.Tags.Count > 0)
                    {
                        logger.LogInformation("    标签: {Tags}", string.Join(", ", endpoint.Tags.Select(t => $"{t.Key}={t.Value}")));
                    }
                }

                if (endpoints.Count > 3)
                {
                    logger.LogInformation("  ... 还有 {More} 个端点", endpoints.Count - 3);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "DNS 查询失败: {ServiceName}", serviceName);
            }
        }

        logger.LogInformation("✅ DNS 服务发现示例完成\n");
    }

    /// <summary>
    /// 标签过滤示例
    /// </summary>
    private static async Task TagFilteringExample(IServiceProvider services, ILogger logger)
    {
        logger.LogInformation("🏷️ === 标签过滤示例 ===");

        var serviceDiscovery = services.GetRequiredService<PulseRPC.ServiceDiscovery.IServiceDiscovery>();

        try
        {
            // 使用标签过滤（DNS 服务发现的标签支持有限）
            var tags = new Dictionary<string, string>
            {
                ["dns_query_type"] = "A"
            };

            var filteredEndpoints = await serviceDiscovery.DiscoverByTagsAsync("google.com", tags);
            logger.LogInformation("根据标签过滤的端点数量: {Count}", filteredEndpoints.Count);

            foreach (var endpoint in filteredEndpoints.Take(2))
            {
                logger.LogInformation("  - {ServiceId}: {EndPoint}", endpoint.ServiceId, endpoint.EndPoint);
                logger.LogInformation("    查询类型: {QueryType}", endpoint.Tags.GetValueOrDefault("dns_query_type", "未知"));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "标签过滤示例执行失败");
        }

        logger.LogInformation("✅ 标签过滤示例完成\n");
    }

    /// <summary>
    /// 健康检查示例
    /// </summary>
    private static async Task HealthCheckExample(IServiceProvider services, ILogger logger)
    {
        logger.LogInformation("🏥 === 健康检查示例 ===");

        var healthChecker = services.GetRequiredService<PulseRPC.ServiceDiscovery.IHealthChecker>();
        var serviceDiscovery = services.GetRequiredService<PulseRPC.ServiceDiscovery.IServiceDiscovery>();

        try
        {
            // 获取一些端点进行健康检查
            var endpoints = await serviceDiscovery.DiscoverAsync("google.com");
            var testEndpoints = endpoints.Take(3).ToList();

            if (testEndpoints.Count == 0)
            {
                logger.LogWarning("未找到端点进行健康检查");
                return;
            }

            logger.LogInformation("对 {Count} 个端点执行健康检查（Ping测试）...", testEndpoints.Count);

            // 执行批量健康检查
            var healthResults = await healthChecker.CheckHealthBatchAsync(testEndpoints);

            var healthyCount = 0;
            var unhealthyCount = 0;

            foreach (var result in healthResults)
            {
                var statusIcon = result.Status == PulseRPC.ServiceDiscovery.HealthStatus.Healthy ? "✅" : "❌";
                logger.LogInformation("  {Icon} {Address}: {Status} (响应时间: {ResponseTime}ms)",
                    statusIcon, 
                    result.ServiceId, 
                    result.Status, 
                    result.ResponseTime.TotalMilliseconds);

                if (result.Status == PulseRPC.ServiceDiscovery.HealthStatus.Healthy)
                    healthyCount++;
                else
                    unhealthyCount++;
            }

            logger.LogInformation("健康检查汇总: ✅健康 {Healthy} | ❌异常 {Unhealthy}", healthyCount, unhealthyCount);

            // 获取健康检查统计信息
            var stats = healthChecker.GetStatistics();
            logger.LogInformation("健康检查统计:");
            foreach (var stat in stats.Take(5)) // 限制显示数量
            {
                logger.LogInformation("  {Key}: {Value}", stat.Key, stat.Value);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "健康检查示例执行失败");
        }

        logger.LogInformation("✅ 健康检查示例完成\n");
    }

    /// <summary>
    /// 负载均衡示例
    /// </summary>
    private static async Task LoadBalancingExample(IServiceProvider services, ILogger logger)
    {
        logger.LogInformation("⚖️ === 负载均衡示例 ===");

        var serviceDiscovery = services.GetRequiredService<PulseRPC.ServiceDiscovery.IServiceDiscovery>();
        var loadBalancerFactory = services.GetRequiredService<PulseRPC.LoadBalancing.Extensions.ILoadBalancerFactory>();

        try
        {
            // 获取端点用于负载均衡测试
            var endpoints = await serviceDiscovery.DiscoverAsync("github.com");
            if (endpoints.Count == 0)
            {
                logger.LogWarning("未找到端点进行负载均衡测试");
                return;
            }

            var roundRobinBalancer = loadBalancerFactory.Create(PulseRPC.Client.LoadBalancing.LoadBalancingStrategy.RoundRobin);
            logger.LogInformation("使用轮询负载均衡策略进行6次选择:");

            for (int i = 0; i < 6; i++)
            {
                var context = new PulseRPC.Client.LoadBalancing.LoadBalancingContext 
                { 
                    RequestId = Guid.NewGuid().ToString() 
                };
                var selectedEndpoint = await roundRobinBalancer.SelectAsync(endpoints, context);
                logger.LogInformation("  第{Index}次选择: {EndPoint}", 
                    i + 1, selectedEndpoint?.EndPoint);

                // 模拟处理时间统计
                if (selectedEndpoint != null)
                {
                    var responseTime = TimeSpan.FromMilliseconds(Random.Shared.Next(20, 100));
                    roundRobinBalancer.ReportResult(selectedEndpoint, 
                        PulseRPC.Client.LoadBalancing.LoadBalancingResult.Success, 
                        responseTime);
                }
            }

            // 获取负载均衡统计
            var lbStats = roundRobinBalancer.GetStatistics();
            logger.LogInformation("负载均衡统计信息:");
            foreach (var stat in lbStats.Take(5))
            {
                logger.LogInformation("  {Key}: {Value}", stat.Key, stat.Value);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "负载均衡示例执行失败");
        }

        logger.LogInformation("✅ 负载均衡示例完成\n");
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
            logger.LogInformation("开始监听 stackoverflow.com 变化（10秒）...");
            
            var watchCount = 0;
            await foreach (var serviceInstances in serviceDiscovery.WatchAsync("stackoverflow.com", cts.Token))
            {
                watchCount++;
                logger.LogInformation("第{Count}次检查，当前端点数量: {InstanceCount}", watchCount, serviceInstances.Length);
                
                if (serviceInstances.Length > 0)
                {
                    var firstEndpoint = serviceInstances[0];
                    logger.LogInformation("  首个端点: {EndPoint}", firstEndpoint.EndPoint);
                }
                
                // 限制监听次数
                if (watchCount >= 2)
                {
                    logger.LogInformation("已监听2次，停止监听");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("监听超时，停止监听");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "监听服务变化失败");
        }

        logger.LogInformation("✅ 监听服务变化示例完成\n");
    }

    /// <summary>
    /// 服务工厂示例
    /// </summary>
    private static async Task ServiceFactoryExample(IServiceProvider services, ILogger logger)
    {
        logger.LogInformation("🏭 === 服务工厂示例 ===");

        var serviceFactory = services.GetRequiredService<PulseRPC.ServiceDiscovery.Extensions.IServiceDiscoveryFactory>();

        try
        {
            logger.LogInformation("使用服务工厂创建 DNS 服务发现实例:");

            var dnsDiscovery = serviceFactory.CreateServiceDiscovery(
                PulseRPC.ServiceDiscovery.Extensions.ServiceDiscoveryProviderType.Dns);
            logger.LogInformation("✅ 创建 DNS 服务发现实例成功");

            // 使用工厂创建的实例进行服务发现
            var discoveredServices = await dnsDiscovery.DiscoverAsync("google.com");
            logger.LogInformation("通过工厂创建的实例发现服务数量: {Count}", discoveredServices.Count);

            if (discoveredServices.Count > 0)
            {
                var firstService = discoveredServices[0];
                logger.LogInformation("  首个服务: {ServiceId} -> {EndPoint}", 
                    firstService.ServiceId, firstService.EndPoint);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "服务工厂示例执行失败");
        }

        logger.LogInformation("✅ 服务工厂示例完成\n");
    }

    /// <summary>
    /// 查询类型示例
    /// </summary>
    private static async Task QueryTypeExample(IServiceProvider services, ILogger logger)
    {
        logger.LogInformation("📋 === DNS 查询类型示例 ===");

        try
        {
            logger.LogInformation("演示不同的 DNS 查询类型:");

            // A 记录查询示例
            logger.LogInformation("1. A 记录查询示例:");
            logger.LogInformation("   查询域名的 IPv4 地址");
            logger.LogInformation("   示例: www.example.com -> 93.184.216.34:8080");

            // SRV 记录查询示例
            logger.LogInformation("2. SRV 记录查询示例:");
            logger.LogInformation("   查询服务的端口和优先级信息");
            logger.LogInformation("   示例: _http._tcp.example.com -> target.example.com:80");

            // 自动模式示例
            logger.LogInformation("3. 自动模式:");
            logger.LogInformation("   优先尝试 SRV 记录，失败时降级到 A 记录");
            logger.LogInformation("   适用于大多数场景");

            logger.LogInformation("注意：实际的 SRV 记录查询需要第三方 DNS 库支持");
            logger.LogInformation("当前实现主要基于 A 记录查询");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "查询类型示例执行失败");
        }

        logger.LogInformation("✅ DNS 查询类型示例完成\n");
    }
}

/// <summary>
/// Web服务接口
/// </summary>
public interface IWebService
{
    Task<string> GetContentAsync(string url);
    Task<bool> CheckAvailabilityAsync(string domain);
    Task<WebResponse> ProcessRequestAsync(WebRequest request);
}

/// <summary>
/// Web服务实现
/// </summary>
public class WebService : IWebService
{
    private readonly ILogger<WebService> _logger;

    public WebService(ILogger<WebService> logger)
    {
        _logger = logger;
    }

    public async Task<string> GetContentAsync(string url)
    {
        _logger.LogInformation("获取内容: {Url}", url);
        await Task.Delay(Random.Shared.Next(100, 300));
        return $"Content from {url}";
    }

    public async Task<bool> CheckAvailabilityAsync(string domain)
    {
        _logger.LogInformation("检查域名可用性: {Domain}", domain);
        await Task.Delay(Random.Shared.Next(50, 150));
        return Random.Shared.NextDouble() > 0.2; // 80% 可用
    }

    public async Task<WebResponse> ProcessRequestAsync(WebRequest request)
    {
        _logger.LogInformation("处理Web请求: {Method} {Path}", request.Method, request.Path);
        await Task.Delay(Random.Shared.Next(80, 200));

        return new WebResponse
        {
            StatusCode = 200,
            Content = $"Response for {request.Method} {request.Path}",
            Headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["Server"] = "PulseRPC-WebService/1.0"
            },
            ProcessedAt = DateTime.UtcNow
        };
    }
}

/// <summary>
/// 数据库服务接口
/// </summary>
public interface IDatabaseService
{
    Task<string> QueryAsync(string sql);
    Task<bool> ExecuteCommandAsync(string command);
    Task<DatabaseStats> GetStatsAsync();
}

/// <summary>
/// 数据库服务实现
/// </summary>
public class DatabaseService : IDatabaseService
{
    private readonly ILogger<DatabaseService> _logger;

    public DatabaseService(ILogger<DatabaseService> logger)
    {
        _logger = logger;
    }

    public async Task<string> QueryAsync(string sql)
    {
        _logger.LogInformation("执行查询: {Sql}", sql);
        await Task.Delay(Random.Shared.Next(50, 200));
        return $"Query result for: {sql}";
    }

    public async Task<bool> ExecuteCommandAsync(string command)
    {
        _logger.LogInformation("执行命令: {Command}", command);
        await Task.Delay(Random.Shared.Next(100, 250));
        return Random.Shared.NextDouble() > 0.1; // 90% 成功率
    }

    public async Task<DatabaseStats> GetStatsAsync()
    {
        _logger.LogInformation("获取数据库统计信息");
        await Task.Delay(Random.Shared.Next(30, 100));

        return new DatabaseStats
        {
            ConnectionCount = Random.Shared.Next(10, 100),
            QueryCount = Random.Shared.Next(1000, 10000),
            AverageResponseTime = TimeSpan.FromMilliseconds(Random.Shared.Next(20, 100)),
            CacheHitRate = Random.Shared.NextDouble() * 0.3 + 0.7, // 70-100%
            LastBackup = DateTime.UtcNow.AddHours(-Random.Shared.Next(1, 24))
        };
    }
}

#region Data Models

/// <summary>
/// Web请求
/// </summary>
public class WebRequest
{
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = new();
    public string? Body { get; set; }
}

/// <summary>
/// Web响应
/// </summary>
public class WebResponse
{
    public int StatusCode { get; set; }
    public string Content { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = new();
    public DateTime ProcessedAt { get; set; }
}

/// <summary>
/// 数据库统计信息
/// </summary>
public class DatabaseStats
{
    public int ConnectionCount { get; set; }
    public long QueryCount { get; set; }
    public TimeSpan AverageResponseTime { get; set; }
    public double CacheHitRate { get; set; }
    public DateTime LastBackup { get; set; }
}

#endregion 