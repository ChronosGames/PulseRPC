using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Client.Extensions;
using PulseRPC.LoadBalancing.Extensions;
using PulseRPC.Server.Extensions;
using PulseRPC.ServiceDiscovery.Extensions;

namespace PulseRPC.Samples.EtcdExample;

/// <summary>
/// PulseRPC Etcd 服务发现示例
/// 演示如何使用 Etcd 作为服务注册与发现中心
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== PulseRPC Etcd 服务发现示例 ===");
        Console.WriteLine("注意：请确保 Etcd 服务已在 localhost:2379 运行");
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
            await RunEtcdExampleAsync(host.Services);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 示例执行失败: {ex.Message}");
            Console.WriteLine("请确保 Etcd 服务正在运行并可访问");
        }

        Console.WriteLine("\n示例执行完毕，按任意键退出...");
        Console.ReadKey();
    }

    /// <summary>
    /// 配置服务 - 使用 Etcd 作为服务发现中心
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        Console.WriteLine("📋 配置PulseRPC Etcd服务...");

        // 1. 添加日志记录
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // 2. 配置 Etcd 服务发现
        services.AddEtcdServiceDiscovery(options =>
        {
            options.Endpoints = new[] { "http://localhost:2379" };
            options.Username = null; // 可选：如果 Etcd 启用了认证
            options.Password = null; // 可选：如果 Etcd 启用了认证
            options.RequestTimeout = TimeSpan.FromSeconds(10);
            options.ServiceTtl = TimeSpan.FromSeconds(30);
            options.EnableCaching = true;
            options.RefreshInterval = TimeSpan.FromSeconds(15);
            options.WatchPollInterval = TimeSpan.FromSeconds(3);
        });

        // 3. 配置轮询负载均衡
        services.AddRoundRobinLoadBalancing();

        // 4. 配置健康检查
        services.AddHealthCheck(options =>
        {
            options.DefaultTimeout = TimeSpan.FromSeconds(5);
            options.RetryCount = 2;
            options.EnableConcurrentChecks = true;
        });

        // 5. 配置PulseRPC客户端
        services.AddPulseRpcClient(options =>
        {
            options.ServiceDiscoveryOptions = new()
            {
                RefreshInterval = TimeSpan.FromSeconds(20),
                CacheTimeout = TimeSpan.FromMinutes(3),
                EnableCaching = true
            };
            options.LoadBalancingOptions = new()
            {
                Strategy = PulseRPC.Client.LoadBalancing.LoadBalancingStrategy.RoundRobin,
                EnableHealthCheck = true,
                HealthCheckInterval = TimeSpan.FromSeconds(20)
            };
        });

        // 6. 配置PulseRPC服务器
        services.AddPulseRpcServer(options =>
        {
            options.ListenEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 9090);
            options.ServiceRegistryOptions = new()
            {
                ServiceName = "ApiGatewayService",
                ServiceVersion = "3.0.0",
                EnableHealthCheck = true,
                HealthCheckInterval = TimeSpan.FromSeconds(15),
                Tags = new Dictionary<string, string>
                {
                    ["environment"] = "staging",
                    ["version"] = "3.0.0",
                    ["region"] = "asia-east-1",
                    ["zone"] = "asia-east-1b",
                    ["protocol"] = "http2"
                },
                Metadata = new Dictionary<string, object>
                {
                    ["description"] = "API网关服务",
                    ["api_version"] = "v3",
                    ["max_connections"] = 2000,
                    ["features"] = new[] { "gateway", "routing", "authentication", "rate-limiting" },
                    ["protocols"] = new[] { "http", "https", "grpc" }
                }
            };
        });

        // 7. 注册示例服务
        services.AddPulseRpcService<IApiGatewayService, ApiGatewayService>();
        services.AddPulseRpcService<IAuthenticationService, AuthenticationService>();

        // 8. 添加服务发现工厂
        services.AddServiceDiscoveryFactory();

        Console.WriteLine("✅ PulseRPC Etcd服务配置完成");
        Console.WriteLine();
    }

    /// <summary>
    /// 运行 Etcd 示例
    /// </summary>
    private static async Task RunEtcdExampleAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();

        // 1. 服务注册示例
        await ServiceRegistrationExample(services, logger);

        // 2. 服务发现示例
        await ServiceDiscoveryExample(services, logger);

        // 3. 健康检查示例
        await HealthCheckExample(services, logger);

        // 4. 负载均衡示例
        await LoadBalancingExample(services, logger);

        // 5. 监听服务变化示例
        await WatchServicesExample(services, logger);

        // 6. 服务工厂示例
        await ServiceFactoryExample(services, logger);
    }

    /// <summary>
    /// 服务注册示例
    /// </summary>
    private static async Task ServiceRegistrationExample(IServiceProvider services, ILogger logger)
    {
        logger.LogInformation("📝 === 服务注册示例 ===");

        var serviceRegistry = services.GetRequiredService<PulseRPC.ServiceDiscovery.IServiceRegistry>();

        // 创建API网关服务端点
        var gatewayEndpoint1 = new PulseRPC.ServiceDiscovery.ServiceEndpoint
        {
            ServiceId = "api-gateway-001",
            ServiceName = "ApiGatewayService",
            EndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("127.0.0.1"), 9090),
            Tags = new Dictionary<string, string>
            {
                ["version"] = "3.0.0",
                ["region"] = "asia-east-1",
                ["zone"] = "asia-east-1b",
                ["protocol"] = "http2"
            },
            Metadata = new Dictionary<string, object>
            {
                ["health_check_url"] = "http://127.0.0.1:9090/health",
                ["max_connections"] = 2000,
                ["load_balancer_weight"] = 100
            }
        };

        // 注册第一个网关实例
        await serviceRegistry.RegisterAsync(gatewayEndpoint1);
        logger.LogInformation("✅ API网关服务已注册到 Etcd: {ServiceId}", gatewayEndpoint1.ServiceId);

        // 创建第二个API网关实例
        var gatewayEndpoint2 = new PulseRPC.ServiceDiscovery.ServiceEndpoint
        {
            ServiceId = "api-gateway-002",
            ServiceName = "ApiGatewayService",
            EndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("127.0.0.1"), 9091),
            Tags = new Dictionary<string, string>
            {
                ["version"] = "3.0.0",
                ["region"] = "asia-east-1",
                ["zone"] = "asia-east-1c",
                ["protocol"] = "http2"
            },
            Metadata = new Dictionary<string, object>
            {
                ["health_check_url"] = "http://127.0.0.1:9091/health",
                ["max_connections"] = 1500,
                ["load_balancer_weight"] = 80
            }
        };

        await serviceRegistry.RegisterAsync(gatewayEndpoint2);
        logger.LogInformation("✅ API网关服务第二个实例已注册: {ServiceId}", gatewayEndpoint2.ServiceId);

        // 注册认证服务
        var authEndpoint = new PulseRPC.ServiceDiscovery.ServiceEndpoint
        {
            ServiceId = "auth-service-001",
            ServiceName = "AuthenticationService",
            EndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("127.0.0.1"), 8080),
            Tags = new Dictionary<string, string>
            {
                ["version"] = "2.1.0",
                ["region"] = "asia-east-1",
                ["type"] = "auth"
            },
            Metadata = new Dictionary<string, object>
            {
                ["auth_methods"] = new[] { "jwt", "oauth2", "basic" },
                ["token_expiry"] = 3600
            }
        };

        await serviceRegistry.RegisterAsync(authEndpoint);
        logger.LogInformation("✅ 认证服务已注册: {ServiceId}", authEndpoint.ServiceId);

        logger.LogInformation("✅ 服务注册示例完成\n");
    }

    /// <summary>
    /// 服务发现示例
    /// </summary>
    private static async Task ServiceDiscoveryExample(IServiceProvider services, ILogger logger)
    {
        logger.LogInformation("🔍 === 服务发现示例 ===");

        var serviceDiscovery = services.GetRequiredService<PulseRPC.ServiceDiscovery.IServiceDiscovery>();

        // 发现API网关服务
        var gatewayServices = await serviceDiscovery.DiscoverAsync("ApiGatewayService");
        logger.LogInformation("从 Etcd 发现API网关服务实例数量: {Count}", gatewayServices.Count);

        foreach (var service in gatewayServices)
        {
            logger.LogInformation("  - {ServiceId}: {EndPoint}", service.ServiceId, service.EndPoint);
            if (service.Tags.Count > 0)
            {
                logger.LogInformation("    标签: {Tags}", string.Join(", ", service.Tags.Select(t => $"{t.Key}={t.Value}")));
            }
            if (service.Metadata.Count > 0)
            {
                logger.LogInformation("    元数据: {Metadata}", string.Join(", ", service.Metadata.Select(m => $"{m.Key}={m.Value}")));
            }
        }

        // 发现认证服务
        var authServices = await serviceDiscovery.DiscoverAsync("AuthenticationService");
        logger.LogInformation("发现认证服务实例数量: {Count}", authServices.Count);

        // 根据标签发现服务
        var versionTags = new Dictionary<string, string> { ["version"] = "3.0.0" };
        var versionFilteredServices = await serviceDiscovery.DiscoverByTagsAsync("ApiGatewayService", versionTags);
        logger.LogInformation("发现 v3.0.0 版本的API网关服务数量: {Count}", versionFilteredServices.Count);

        var regionTags = new Dictionary<string, string> { ["region"] = "asia-east-1" };
        var regionFilteredServices = await serviceDiscovery.DiscoverByTagsAsync("ApiGatewayService", regionTags);
        logger.LogInformation("发现 asia-east-1 区域的API网关服务数量: {Count}", regionFilteredServices.Count);

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

        // 获取所有服务端点
        var gatewayServices = await serviceDiscovery.DiscoverAsync("ApiGatewayService");
        var authServices = await serviceDiscovery.DiscoverAsync("AuthenticationService");
        
        var allServices = gatewayServices.Concat(authServices).ToList();
        
        if (allServices.Count == 0)
        {
            logger.LogWarning("未找到服务端点进行健康检查");
            return;
        }

        logger.LogInformation("对 {Count} 个服务端点执行健康检查...", allServices.Count);

        // 执行批量健康检查
        var healthResults = await healthChecker.CheckHealthBatchAsync(allServices);

        // 统计结果
        var healthyCount = 0;
        var unhealthyCount = 0;

        foreach (var result in healthResults)
        {
            var statusIcon = result.Status == PulseRPC.ServiceDiscovery.HealthStatus.Healthy ? "✅" : "❌";
            logger.LogInformation("  {Icon} {ServiceId}: {Status} (响应时间: {ResponseTime}ms)",
                statusIcon, result.ServiceId, result.Status, result.ResponseTime.TotalMilliseconds);

            if (result.Status == PulseRPC.ServiceDiscovery.HealthStatus.Healthy)
                healthyCount++;
            else
                unhealthyCount++;

            if (!string.IsNullOrEmpty(result.Details))
            {
                logger.LogInformation("    详情: {Details}", result.Details);
            }
        }

        logger.LogInformation("健康检查汇总: ✅健康 {Healthy} | ❌异常 {Unhealthy}", healthyCount, unhealthyCount);

        // 获取健康检查统计信息
        var stats = healthChecker.GetStatistics();
        logger.LogInformation("健康检查服务统计:");
        foreach (var stat in stats)
        {
            logger.LogInformation("  {Key}: {Value}", stat.Key, stat.Value);
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

        // 获取API网关服务端点
        var endpoints = await serviceDiscovery.DiscoverAsync("ApiGatewayService");
        if (endpoints.Count == 0)
        {
            logger.LogWarning("未找到ApiGatewayService端点进行负载均衡测试");
            return;
        }

        var roundRobinBalancer = loadBalancerFactory.Create(PulseRPC.Client.LoadBalancing.LoadBalancingStrategy.RoundRobin);
        logger.LogInformation("使用轮询负载均衡策略进行8次选择:");

        for (int i = 0; i < 8; i++)
        {
            var context = new PulseRPC.Client.LoadBalancing.LoadBalancingContext 
            { 
                RequestId = Guid.NewGuid().ToString() 
            };
            var selectedEndpoint = await roundRobinBalancer.SelectAsync(endpoints, context);
            logger.LogInformation("  第{Index}次选择: {ServiceId} -> {EndPoint}", 
                i + 1, selectedEndpoint?.ServiceId, selectedEndpoint?.EndPoint);

            // 模拟处理时间统计
            if (selectedEndpoint != null)
            {
                var responseTime = TimeSpan.FromMilliseconds(Random.Shared.Next(50, 200));
                roundRobinBalancer.ReportResult(selectedEndpoint, 
                    PulseRPC.Client.LoadBalancing.LoadBalancingResult.Success, 
                    responseTime);
            }
        }

        // 获取负载均衡统计
        var lbStats = roundRobinBalancer.GetStatistics();
        logger.LogInformation("负载均衡统计信息:");
        foreach (var stat in lbStats)
        {
            logger.LogInformation("  {Key}: {Value}", stat.Key, stat.Value);
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

        // 监听API网关服务变化（限制时间避免无限等待）
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        
        try
        {
            logger.LogInformation("开始监听 ApiGatewayService 变化（15秒）...");
            
            var watchCount = 0;
            await foreach (var serviceInstances in serviceDiscovery.WatchAsync("ApiGatewayService", cts.Token))
            {
                watchCount++;
                logger.LogInformation("第{Count}次检测到服务变化，当前实例数量: {InstanceCount}", watchCount, serviceInstances.Length);
                
                foreach (var instance in serviceInstances)
                {
                    logger.LogInformation("  - {ServiceId}: {EndPoint} (Zone: {Zone})", 
                        instance.ServiceId, 
                        instance.EndPoint,
                        instance.Tags.GetValueOrDefault("zone", "未知"));
                }
                
                // 限制监听次数避免输出过多
                if (watchCount >= 3)
                {
                    logger.LogInformation("已监听到3次变化，停止监听");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("监听超时，停止监听");
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

        // 创建不同类型的服务发现实例
        logger.LogInformation("使用服务工厂创建不同的服务发现实例:");

        try
        {
            var etcdDiscovery = serviceFactory.CreateServiceDiscovery(
                PulseRPC.ServiceDiscovery.Extensions.ServiceDiscoveryProviderType.Etcd);
            logger.LogInformation("✅ 创建 Etcd 服务发现实例成功");

            var etcdRegistry = serviceFactory.CreateServiceRegistry(
                PulseRPC.ServiceDiscovery.Extensions.ServiceDiscoveryProviderType.Etcd);
            logger.LogInformation("✅ 创建 Etcd 服务注册实例成功");

            // 使用工厂创建的实例进行服务发现
            var discoveredServices = await etcdDiscovery.DiscoverAsync("ApiGatewayService");
            logger.LogInformation("通过工厂创建的实例发现服务数量: {Count}", discoveredServices.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "服务工厂示例执行失败");
        }

        logger.LogInformation("✅ 服务工厂示例完成\n");
    }
}

/// <summary>
/// API网关服务接口
/// </summary>
public interface IApiGatewayService
{
    Task<RouteResponse> RouteRequestAsync(RouteRequest request);
    Task<bool> ValidateTokenAsync(string token);
    Task<RateLimitResponse> CheckRateLimitAsync(string clientId, string endpoint);
}

/// <summary>
/// API网关服务实现
/// </summary>
public class ApiGatewayService : IApiGatewayService
{
    private readonly ILogger<ApiGatewayService> _logger;

    public ApiGatewayService(ILogger<ApiGatewayService> logger)
    {
        _logger = logger;
    }

    public async Task<RouteResponse> RouteRequestAsync(RouteRequest request)
    {
        _logger.LogInformation("路由请求: {Method} {Path}", request.Method, request.Path);
        await Task.Delay(Random.Shared.Next(50, 150));

        return new RouteResponse
        {
            TargetService = "user-service",
            TargetEndpoint = "http://user-service:8080" + request.Path,
            RoutingTime = DateTime.UtcNow,
            Success = true
        };
    }

    public async Task<bool> ValidateTokenAsync(string token)
    {
        _logger.LogInformation("验证令牌: {Token}", token[..Math.Min(token.Length, 10)] + "...");
        await Task.Delay(Random.Shared.Next(20, 80));
        return !string.IsNullOrEmpty(token) && token.Length > 10;
    }

    public async Task<RateLimitResponse> CheckRateLimitAsync(string clientId, string endpoint)
    {
        _logger.LogInformation("检查速率限制: 客户端 {ClientId}, 端点 {Endpoint}", clientId, endpoint);
        await Task.Delay(Random.Shared.Next(10, 50));

        return new RateLimitResponse
        {
            Allowed = Random.Shared.NextDouble() > 0.1, // 90% 的请求被允许
            RemainingRequests = Random.Shared.Next(0, 100),
            ResetTime = DateTime.UtcNow.AddMinutes(1)
        };
    }
}

/// <summary>
/// 认证服务接口
/// </summary>
public interface IAuthenticationService
{
    Task<AuthResult> AuthenticateAsync(string username, string password);
    Task<TokenResponse> RefreshTokenAsync(string refreshToken);
    Task<bool> RevokeTokenAsync(string token);
}

/// <summary>
/// 认证服务实现
/// </summary>
public class AuthenticationService : IAuthenticationService
{
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(ILogger<AuthenticationService> logger)
    {
        _logger = logger;
    }

    public async Task<AuthResult> AuthenticateAsync(string username, string password)
    {
        _logger.LogInformation("用户认证: {Username}", username);
        await Task.Delay(Random.Shared.Next(100, 300));

        var success = !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password);
        return new AuthResult
        {
            Success = success,
            Token = success ? GenerateToken() : null,
            ExpiresAt = success ? DateTime.UtcNow.AddHours(1) : null,
            UserId = success ? Guid.NewGuid().ToString() : null
        };
    }

    public async Task<TokenResponse> RefreshTokenAsync(string refreshToken)
    {
        _logger.LogInformation("刷新令牌: {RefreshToken}", refreshToken[..Math.Min(refreshToken.Length, 8)] + "...");
        await Task.Delay(Random.Shared.Next(50, 150));

        return new TokenResponse
        {
            AccessToken = GenerateToken(),
            RefreshToken = GenerateToken(),
            ExpiresIn = 3600
        };
    }

    public async Task<bool> RevokeTokenAsync(string token)
    {
        _logger.LogInformation("撤销令牌: {Token}", token[..Math.Min(token.Length, 8)] + "...");
        await Task.Delay(Random.Shared.Next(30, 100));
        return true;
    }

    private string GenerateToken()
    {
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            $"token_{Guid.NewGuid()}_{DateTime.UtcNow.Ticks}"));
    }
}

#region Data Models

/// <summary>
/// 路由请求
/// </summary>
public class RouteRequest
{
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = new();
    public string? Body { get; set; }
}

/// <summary>
/// 路由响应
/// </summary>
public class RouteResponse
{
    public string TargetService { get; set; } = string.Empty;
    public string TargetEndpoint { get; set; } = string.Empty;
    public DateTime RoutingTime { get; set; }
    public bool Success { get; set; }
}

/// <summary>
/// 速率限制响应
/// </summary>
public class RateLimitResponse
{
    public bool Allowed { get; set; }
    public int RemainingRequests { get; set; }
    public DateTime ResetTime { get; set; }
}

/// <summary>
/// 认证结果
/// </summary>
public class AuthResult
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? UserId { get; set; }
}

/// <summary>
/// 令牌响应
/// </summary>
public class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
}

#endregion 