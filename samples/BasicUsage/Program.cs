using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Client.Extensions;
using PulseRPC.LoadBalancing.Extensions;
using PulseRPC.Server.Extensions;
using PulseRPC.ServiceDiscovery.Extensions;

namespace PulseRPC.Samples.BasicUsage;

/// <summary>
/// PulseRPC 基础使用示例
/// 演示服务发现、负载均衡、客户端和服务端的基本配置和使用
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== PulseRPC 基础使用示例 ===");
        Console.WriteLine();

        // 创建主机构建器
        var builder = Host.CreateApplicationBuilder(args);

        // 配置服务
        ConfigureServices(builder.Services);

        // 构建主机
        var host = builder.Build();

        // 运行示例
        await RunExamplesAsync(host.Services);

        Console.WriteLine("\n示例执行完毕，按任意键退出...");
        Console.ReadKey();
    }

    /// <summary>
    /// 配置服务 - 展示完整的PulseRPC配置
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        Console.WriteLine("📋 配置PulseRPC服务...");

        // 1. 添加日志记录
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // 2. 配置静态服务发现（用于演示）
        services.AddStaticServiceDiscovery(endpoints =>
        {
            endpoints["UserService"] = new[] { "127.0.0.1:8001", "127.0.0.1:8002" };
            endpoints["OrderService"] = new[] { "127.0.0.1:8003", "127.0.0.1:8004", "127.0.0.1:8005" };
            endpoints["PaymentService"] = new[] { "127.0.0.1:8006" };
        });

        // 3. 配置轮询负载均衡
        services.AddRoundRobinLoadBalancing();

        // 4. 配置故障转移负载均衡（带自定义选项）
        services.AddFailoverLoadBalancing(options =>
        {
            options.FailureThreshold = 3;
            options.RecoveryThreshold = 2;
            options.CircuitBreakerOpenTime = TimeSpan.FromMinutes(1);
            options.EnableGracefulDegradation = true;
        });

        // 5. 配置负载均衡器工厂
        services.AddLoadBalancerFactory();

        // 6. 配置健康检查
        services.AddHealthCheck(options =>
        {
            options.DefaultTimeout = TimeSpan.FromSeconds(5);
            options.RetryCount = 2;
            options.EnableConcurrentChecks = true;
            options.MaxConcurrentChecks = 50;
        });

        // 7. 配置PulseRPC客户端
        services.AddPulseRpcClient(options =>
        {
            options.ServiceDiscoveryOptions = new()
            {
                RefreshInterval = TimeSpan.FromSeconds(30),
                CacheTimeout = TimeSpan.FromMinutes(5)
            };
            options.LoadBalancingOptions = new()
            {
                Strategy = PulseRPC.Client.LoadBalancing.LoadBalancingStrategy.RoundRobin,
                EnableHealthCheck = true,
                HealthCheckInterval = TimeSpan.FromSeconds(30)
            };
        });

        // 8. 配置PulseRPC服务器
        services.AddPulseRpcServer(options =>
        {
            options.ListenEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 8000);
            options.ServiceRegistryOptions = new()
            {
                ServiceName = "DemoService",
                ServiceVersion = "1.0.0",
                EnableHealthCheck = true,
                HealthCheckInterval = TimeSpan.FromSeconds(30),
                Tags = new Dictionary<string, string>
                {
                    ["environment"] = "development",
                    ["version"] = "1.0.0",
                    ["region"] = "local"
                }
            };
        });

        // 9. 注册示例服务
        services.AddPulseRpcService<IUserService, UserService>();
        services.AddPulseRpcService<ICalculatorService, CalculatorService>();

        // 10. 配置性能选项
        services.ConfigurePulseRpcPerformance(options =>
        {
            options.MaxConcurrentConnections = 1000;
            options.MaxConcurrentRequests = 5000;
            options.EnablePerformanceMonitoring = true;
            options.PerformanceStatsSamplingInterval = TimeSpan.FromSeconds(30);
        });

        Console.WriteLine("✅ PulseRPC服务配置完成");
        Console.WriteLine();
    }

    /// <summary>
    /// 运行示例 - 展示各种功能的使用
    /// </summary>
    private static async Task RunExamplesAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();

        try
        {
            // 1. 服务发现示例
            await ServiceDiscoveryExample(services, logger);

            // 2. 负载均衡示例
            await LoadBalancingExample(services, logger);

            // 3. 健康检查示例
            await HealthCheckExample(services, logger);

            // 4. 客户端示例
            await ClientExample(services, logger);

            // 5. 性能统计示例
            await PerformanceStatsExample(services, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "示例执行过程中发生错误");
        }
    }

    /// <summary>
    /// 服务发现示例
    /// </summary>
    private static async Task ServiceDiscoveryExample(IServiceProvider services, ILogger logger)
    {
        logger.LogInformation("🔍 === 服务发现示例 ===");

        var serviceDiscovery = services.GetRequiredService<PulseRPC.ServiceDiscovery.IServiceDiscovery>();

        // 发现用户服务
        var userServices = await serviceDiscovery.DiscoverAsync("UserService");
        logger.LogInformation("发现用户服务端点数量: {Count}", userServices.Count);
        foreach (var endpoint in userServices)
        {
            logger.LogInformation("  - {ServiceId}: {EndPoint}", endpoint.ServiceId, endpoint.EndPoint);
        }

        // 发现订单服务
        var orderServices = await serviceDiscovery.DiscoverAsync("OrderService");
        logger.LogInformation("发现订单服务端点数量: {Count}", orderServices.Count);
        foreach (var endpoint in orderServices)
        {
            logger.LogInformation("  - {ServiceId}: {EndPoint}", endpoint.ServiceId, endpoint.EndPoint);
        }

        logger.LogInformation("✅ 服务发现示例完成\n");
    }

    /// <summary>
    /// 负载均衡示例
    /// </summary>
    private static async Task LoadBalancingExample(IServiceProvider services, ILogger logger)
    {
        logger.LogInformation("⚖️ === 负载均衡示例 ===");

        var serviceDiscovery = services.GetRequiredService<PulseRPC.ServiceDiscovery.IServiceDiscovery>();
        var loadBalancerFactory = services.GetRequiredService<PulseRPC.LoadBalancing.Extensions.ILoadBalancerFactory>();

        // 获取服务端点
        var endpoints = await serviceDiscovery.DiscoverAsync("OrderService");
        if (endpoints.Count == 0)
        {
            logger.LogWarning("未找到OrderService端点");
            return;
        }

        // 轮询负载均衡示例
        var roundRobinBalancer = loadBalancerFactory.Create(PulseRPC.Client.LoadBalancing.LoadBalancingStrategy.RoundRobin);
        logger.LogInformation("使用轮询负载均衡策略:");
        
        for (int i = 0; i < 6; i++)
        {
            var context = new PulseRPC.Client.LoadBalancing.LoadBalancingContext 
            { 
                RequestId = Guid.NewGuid().ToString() 
            };
            var selectedEndpoint = await roundRobinBalancer.SelectAsync(endpoints, context);
            logger.LogInformation("  第{Index}次选择: {EndPoint}", i + 1, selectedEndpoint?.EndPoint);
        }

        // 故障转移负载均衡示例
        var failoverBalancer = loadBalancerFactory.Create(PulseRPC.Client.LoadBalancing.LoadBalancingStrategy.Failover);
        logger.LogInformation("使用故障转移负载均衡策略:");

        var failoverContext = new PulseRPC.Client.LoadBalancing.LoadBalancingContext 
        { 
            RequestId = Guid.NewGuid().ToString() 
        };
        var failoverEndpoint = await failoverBalancer.SelectAsync(endpoints, failoverContext);
        logger.LogInformation("  故障转移选择: {EndPoint}", failoverEndpoint?.EndPoint);

        // 模拟故障报告
        if (failoverEndpoint != null)
        {
            failoverBalancer.ReportResult(failoverEndpoint, 
                PulseRPC.Client.LoadBalancing.LoadBalancingResult.ConnectionFailed, 
                TimeSpan.FromSeconds(5));
            logger.LogInformation("  已报告连接失败");
        }

        logger.LogInformation("✅ 负载均衡示例完成\n");
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
        var endpoints = await serviceDiscovery.DiscoverAsync("UserService");
        if (endpoints.Count == 0)
        {
            logger.LogWarning("未找到UserService端点进行健康检查");
            return;
        }

        // 执行健康检查
        logger.LogInformation("执行健康检查...");
        foreach (var endpoint in endpoints)
        {
            try
            {
                var healthResult = await healthChecker.CheckHealthAsync(endpoint);
                logger.LogInformation("  {ServiceId}: {Status} (响应时间: {ResponseTime}ms)", 
                    endpoint.ServiceId, 
                    healthResult.Status, 
                    healthResult.ResponseTime.TotalMilliseconds);
                
                if (!string.IsNullOrEmpty(healthResult.Details))
                {
                    logger.LogInformation("    详情: {Details}", healthResult.Details);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "健康检查失败: {ServiceId}", endpoint.ServiceId);
            }
        }

        // 批量健康检查
        logger.LogInformation("执行批量健康检查...");
        var batchResults = await healthChecker.CheckHealthBatchAsync(endpoints);
        var healthyCount = batchResults.Count(r => r.Status == PulseRPC.ServiceDiscovery.HealthStatus.Healthy);
        logger.LogInformation("批量检查结果: {HealthyCount}/{TotalCount} 健康", healthyCount, batchResults.Count);

        logger.LogInformation("✅ 健康检查示例完成\n");
    }

    /// <summary>
    /// 客户端示例
    /// </summary>
    private static async Task ClientExample(IServiceProvider services, ILogger logger)
    {
        logger.LogInformation("🔌 === 客户端示例 ===");

        var client = services.GetRequiredService<IPulseClient>();

        // 1. 连接到服务器
        logger.LogInformation("连接到PulseRPC服务器...");
        await client.ConnectAsync();
        logger.LogInformation("✅ 客户端连接成功");

        // 2. 获取服务代理
        logger.LogInformation("获取用户服务代理...");
        var userService = await client.GetServiceAsync<IUserService>("UserService");
        logger.LogInformation("✅ 用户服务代理获取成功");

        // 3. 使用服务发现客户端
        logger.LogInformation("使用服务发现客户端获取端点...");
        var context = new PulseRPC.Client.LoadBalancing.LoadBalancingContext 
        { 
            RequestId = Guid.NewGuid().ToString() 
        };
        
        var endpoint = await serviceDiscoveryClient.GetServiceEndpointAsync("UserService", context);
        if (endpoint != null)
        {
            logger.LogInformation("获取到端点: {EndPoint}", endpoint.EndPoint);
        }
        else
        {
            logger.LogWarning("未获取到可用端点");
        }

        // 4. 获取统计信息
        var stats = serviceDiscoveryClient.GetStatistics();
        logger.LogInformation("服务发现客户端统计信息:");
        foreach (var stat in stats)
        {
            logger.LogInformation("  {Key}: {Value}", stat.Key, stat.Value);
        }

        logger.LogInformation("✅ 客户端示例完成\n");
    }

    /// <summary>
    /// 性能统计示例
    /// </summary>
    private static async Task PerformanceStatsExample(IServiceProvider services, ILogger logger)
    {
        logger.LogInformation("📊 === 性能统计示例 ===");

        try
        {
            // 1. 健康检查统计
            var healthChecker = services.GetRequiredService<PulseRPC.ServiceDiscovery.IHealthChecker>();
            var healthStats = healthChecker.GetStatistics();
            logger.LogInformation("健康检查统计信息:");
            foreach (var stat in healthStats)
            {
                logger.LogInformation("  {Key}: {Value}", stat.Key, stat.Value);
            }

            // 2. 负载均衡统计
            var loadBalancerFactory = services.GetRequiredService<PulseRPC.LoadBalancing.Extensions.ILoadBalancerFactory>();
            var loadBalancer = loadBalancerFactory.Create(PulseRPC.Client.LoadBalancing.LoadBalancingStrategy.RoundRobin);
            var lbStats = loadBalancer.GetStatistics();
            logger.LogInformation("负载均衡统计信息:");
            foreach (var stat in lbStats)
            {
                logger.LogInformation("  {Key}: {Value}", stat.Key, stat.Value);
            }

            // 3. 服务发现客户端统计
            var serviceDiscoveryClient = services.GetRequiredService<PulseRPC.Client.ServiceDiscovery.ServiceDiscoveryClient>();
            var sdcStats = serviceDiscoveryClient.GetStatistics();
            logger.LogInformation("服务发现客户端统计信息:");
            foreach (var stat in sdcStats)
            {
                logger.LogInformation("  {Key}: {Value}", stat.Key, stat.Value);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取性能统计信息时发生错误");
        }

        logger.LogInformation("✅ 性能统计示例完成\n");
    }
}

/// <summary>
/// 用户服务接口
/// </summary>
public interface IUserService
{
    Task<string> GetUserAsync(int userId);
    Task<bool> CreateUserAsync(string username, string email);
}

/// <summary>
/// 用户服务实现
/// </summary>
public class UserService : IUserService
{
    private readonly ILogger<UserService> _logger;

    public UserService(ILogger<UserService> logger)
    {
        _logger = logger;
    }

    public async Task<string> GetUserAsync(int userId)
    {
        _logger.LogInformation("获取用户信息: {UserId}", userId);
        await Task.Delay(100); // 模拟异步操作
        return $"User_{userId}";
    }

    public async Task<bool> CreateUserAsync(string username, string email)
    {
        _logger.LogInformation("创建用户: {Username}, {Email}", username, email);
        await Task.Delay(200); // 模拟异步操作
        return true;
    }
}

/// <summary>
/// 计算器服务接口
/// </summary>
public interface ICalculatorService
{
    Task<double> AddAsync(double a, double b);
    Task<double> MultiplyAsync(double a, double b);
}

/// <summary>
/// 计算器服务实现
/// </summary>
public class CalculatorService : ICalculatorService
{
    private readonly ILogger<CalculatorService> _logger;

    public CalculatorService(ILogger<CalculatorService> logger)
    {
        _logger = logger;
    }

    public async Task<double> AddAsync(double a, double b)
    {
        _logger.LogInformation("执行加法运算: {A} + {B}", a, b);
        await Task.Delay(50); // 模拟异步操作
        return a + b;
    }

    public async Task<double> MultiplyAsync(double a, double b)
    {
        _logger.LogInformation("执行乘法运算: {A} * {B}", a, b);
        await Task.Delay(50); // 模拟异步操作
        return a * b;
    }
} 