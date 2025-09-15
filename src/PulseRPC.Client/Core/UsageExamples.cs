using PulseRPC.Client.Core;
using PulseRPC.Transport;

namespace PulseRPC.Client.Core.Examples;

/// <summary>
/// 新设计的完整使用示例
/// </summary>
public class CompleteUsageExamples
{
    /// <summary>
    /// 示例1：基础网络库使用 - 企业级应用
    /// </summary>
    public async Task EnterpriseApplicationExample()
    {
        // 构建客户端 - 声明式配置
        var client = new PulseRPCClientBuilder()
            // 核心服务连接（持久连接）
            .AddConnection(new ConnectionDescriptor
            {
                Id = "auth-service-primary",
                Name = "auth-service",
                ServiceName = "authentication-service",
                Transport = TransportType.Tcp,
                Strategy = ConnectionStrategy.Persistent,
                AutoReconnect = true,
                Tags = new Dictionary<string, string> { ["type"] = "core", ["service"] = "auth" }
            })
            .AddConnection(new ConnectionDescriptor
            {
                Id = "user-service-primary",
                Name = "user-service",
                ServiceName = "user-management-service",
                Transport = TransportType.Tcp,
                Strategy = ConnectionStrategy.Persistent,
                AutoReconnect = true,
                Tags = new Dictionary<string, string> { ["type"] = "core", ["service"] = "user" }
            })
            // 配置服务发现
            .WithServiceDiscovery(new ConsulServiceDiscovery("http://consul.company.com:8500"))
            .WithLoadBalancing(LoadBalancingStrategy.WeightedRoundRobin)
            .WithConnectionPooling(new ConnectionPoolOptions
            {
                Strategy = PoolingStrategy.Dynamic,
                MinSize = 2,
                MaxSize = 20,
                IdleTimeout = TimeSpan.FromMinutes(10)
            })
            .WithRetryPolicy(new RetryPolicy
            {
                MaxRetries = 3,
                BaseDelay = TimeSpan.FromMilliseconds(100),
                BackoffStrategy = BackoffStrategy.Exponential
            })
            .Configure(options =>
            {
                options.DefaultTimeout = TimeSpan.FromSeconds(30);
                options.MaxConcurrentConnections = 100;
                options.EnableStatistics = true;
            })
            .Build();

        // 初始化客户端
        await client.InitializeAsync();

        // 获取认证服务（自动路由到最佳连接）
        var authService = await client.GetServiceAsync<IAuthenticationService>();
        var loginResult = await authService.LoginAsync("user@company.com", "password");

        // 获取用户服务
        var userService = await client.GetServiceAsync<IUserService>();
        var userProfile = await userService.GetUserProfileAsync(loginResult.UserId);

        // 停止客户端
        await client.StopAsync();
    }

    /// <summary>
    /// 示例2：微服务架构 - 动态服务发现
    /// </summary>
    public async Task MicroservicesExample()
    {
        var client = new PulseRPCClientBuilder()
            .WithServiceDiscovery(new KubernetesServiceDiscovery())
            .WithLoadBalancing(LoadBalancingStrategy.LeastConnections)
            .Build();

        await client.InitializeAsync();

        // 动态连接到订单服务
        var orderConnection = await client.ConnectToServiceAsync("order-service", new ServiceConnectionOptions
        {
            Strategy = ConnectionStrategy.Session,
            PreferredTransport = TransportType.Tcp,
            LoadBalancingHint = LoadBalancingHint.LeastConnections,
            Tags = new Dictionary<string, string> { ["region"] = "us-west-2" }
        });

        var orderService = await orderConnection.GetServiceAsync<IOrderService>();
        var orders = await orderService.GetOrdersAsync(customerId: "123");

        // 断开特定服务连接
        await client.DisconnectAsync(orderConnection.Id);
    }

    /// <summary>
    /// 示例3：连接池管理 - 高并发场景
    /// </summary>
    public async Task HighConcurrencyExample()
    {
        var poolFactory = new ConnectionPoolFactory();
        
        // 创建数据库连接池
        var dbPool = poolFactory.CreatePool(
            "database-pool",
            new ConnectionDescriptor
            {
                Id = "db-connection-template",
                Name = "database",
                ServiceName = "database-service",
                Transport = TransportType.Tcp,
                Strategy = ConnectionStrategy.Pooled
            },
            new ConnectionPoolOptions
            {
                Strategy = PoolingStrategy.FixedSize,
                MinSize = 10,
                MaxSize = 50,
                ValidateOnAcquire = true,
                ValidateWhileIdle = true
            });

        await dbPool.InitializeAsync();

        // 使用连接池
        using (var lease = await dbPool.AcquireAsync(timeout: TimeSpan.FromSeconds(5)))
        {
            var dbService = await lease.Connection.GetServiceAsync<IDatabaseService>();
            var result = await dbService.ExecuteQueryAsync("SELECT * FROM users");
            // 连接自动归还到池中
        }

        // 监控连接池状态
        var stats = dbPool.GetStatistics();
        Console.WriteLine($"Pool: {stats.ActiveConnections}/{stats.CurrentSize} connections");

        await dbPool.ShutdownAsync();
    }

    /// <summary>
    /// 示例4：路由和负载均衡 - 智能请求分发
    /// </summary>
    public async Task SmartRoutingExample()
    {
        var client = new PulseRPCClientBuilder()
            .WithLoadBalancing(LoadBalancingStrategy.ConsistentHash)
            .Build();

        await client.InitializeAsync();

        // 注册路由规则
        client.Router.RegisterRule(new RoutingRule
        {
            Id = "admin-rule",
            Name = "Route admin requests to dedicated servers",
            Matcher = (routingKey, context) => context?.Tags.GetValueOrDefault("user_type") == "admin",
            Selector = (connections, context) => 
                connections.FirstOrDefault(c => c.Descriptor.Tags.GetValueOrDefault("tier") == "premium"),
            Priority = 100
        });

        client.Router.RegisterRule(new RoutingRule
        {
            Id = "region-rule",
            Name = "Route by user region",
            Matcher = (routingKey, context) => !string.IsNullOrEmpty(context?.PreferredRegion),
            Selector = (connections, context) => 
                connections.FirstOrDefault(c => c.Descriptor.Tags.GetValueOrDefault("region") == context?.PreferredRegion),
            Priority = 50
        });

        // 使用路由上下文
        var routingContext = new RoutingContext
        {
            UserId = "user123",
            Tags = new Dictionary<string, string> { ["user_type"] = "admin" },
            PreferredRegion = "us-east-1",
            LoadBalancingHint = LoadBalancingHint.ConsistentHash
        };

        var connection = await client.Router.RouteAsync("api-service", routingContext);
        var apiService = await connection.GetServiceAsync<IApiService>();
    }

    /// <summary>
    /// 示例5：事件驱动架构 - 实时监听
    /// </summary>
    public async Task EventDrivenExample()
    {
        var client = new PulseRPCClientBuilder()
            .AddConnection(new ConnectionDescriptor
            {
                Id = "event-bus",
                Name = "event-service",
                ServiceName = "event-bus-service",
                Transport = TransportType.Kcp, // KCP适合实时通信
                Strategy = ConnectionStrategy.Persistent
            })
            .Build();

        await client.InitializeAsync();

        // 注册事件监听器
        var eventHandler = new SystemEventHandler();
        var subscription = await client.RegisterEventListenerAsync<ISystemEventHandler>(
            eventHandler, 
            new EventListenerOptions
            {
                EventFilter = evt => evt.GetType().Name.Contains("Critical"),
                ErrorHandling = EventErrorHandlingStrategy.Log
            });

        // 监听服务发现变化
        var serviceWatcher = await client.ServiceDiscovery.WatchAsync("payment-service", change =>
        {
            Console.WriteLine($"Service change: {change.ChangeType} - {change.ServiceName}");
            if (change.ChangeType == ServiceChangeType.EndpointAdded)
            {
                // 动态添加新的连接
                _ = Task.Run(async () =>
                {
                    await client.ConnectToServiceAsync(change.ServiceName);
                });
            }
        });

        // 清理
        await subscription.UnsubscribeAsync();
        await serviceWatcher.StopAsync();
    }

    /// <summary>
    /// 示例6：故障恢复和监控 - 企业级可靠性
    /// </summary>
    public async Task ReliabilityExample()
    {
        var client = new PulseRPCClientBuilder()
            .WithRetryPolicy(new RetryPolicy
            {
                MaxRetries = 5,
                BaseDelay = TimeSpan.FromMilliseconds(200),
                MaxDelay = TimeSpan.FromSeconds(10),
                BackoffStrategy = BackoffStrategy.Exponential,
                JitterFactor = 0.2,
                ShouldRetry = ex => ex is TimeoutException or SocketException
            })
            .Configure(options =>
            {
                options.HealthCheckInterval = TimeSpan.FromSeconds(30);
                options.EnablePerformanceMonitoring = true;
            })
            .Build();

        // 监听客户端状态变化
        client.StateChanged += (sender, args) =>
        {
            Console.WriteLine($"Client state: {args.PreviousState} -> {args.CurrentState}");
            if (args.Exception != null)
            {
                Console.WriteLine($"Error: {args.Exception.Message}");
            }
        };

        // 监听连接状态变化
        client.Registry.ConnectionRegistered += (sender, args) =>
        {
            Console.WriteLine($"Connection registered: {args.Connection.Id}");
            
            // 为每个连接添加状态监听
            args.Connection.StateChanged += (connSender, connArgs) =>
            {
                if (connArgs.CurrentState == ConnectionState.Failed)
                {
                    Console.WriteLine($"Connection {connArgs.ConnectionId} failed: {connArgs.Exception?.Message}");
                    // 触发故障恢复流程
                    _ = Task.Run(() => HandleConnectionFailure(connArgs.ConnectionId));
                }
            };
        };

        await client.InitializeAsync();

        // 定期健康检查
        var healthCheckTimer = new Timer(async _ =>
        {
            var healthResult = await client.CheckHealthAsync();
            Console.WriteLine($"Overall health: {healthResult.OverallHealth}");
            
            foreach (var connHealth in healthResult.ConnectionResults)
            {
                if (connHealth.Health != ConnectionHealth.Healthy)
                {
                    Console.WriteLine($"Unhealthy connection: {connHealth.ConnectionId} - {connHealth.Message}");
                }
            }
        }, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));

        // 获取统计信息
        var stats = client.GetStatistics();
        Console.WriteLine($"""
            Client Statistics:
            - Total Connections: {stats.TotalConnections}
            - Active Connections: {stats.ActiveConnections}
            - Total Requests: {stats.TotalRequests}
            - Success Rate: {(double)stats.SuccessfulRequests / stats.TotalRequests:P2}
            - Average Response Time: {stats.AverageResponseTimeMs:F2}ms
            """);

        healthCheckTimer.Dispose();
    }

    /// <summary>
    /// 示例7：批量操作和管理 - 运维友好
    /// </summary>
    public async Task BatchOperationsExample()
    {
        var client = new PulseRPCClientBuilder()
            .AddConnection(CreateConnection("service-a", "service-a.local", 8001, "region-1"))
            .AddConnection(CreateConnection("service-b", "service-b.local", 8002, "region-1"))
            .AddConnection(CreateConnection("service-c", "service-c.local", 8003, "region-2"))
            .AddConnection(CreateConnection("service-d", "service-d.local", 8004, "region-2"))
            .Build();

        await client.InitializeAsync();

        // 批量断开某个区域的连接
        var disconnectedCount = await client.DisconnectAsync(
            conn => conn.Descriptor.Tags.GetValueOrDefault("region") == "region-1",
            graceful: true);
        Console.WriteLine($"Disconnected {disconnectedCount} connections in region-1");

        // 查询特定类型的连接
        var coreConnections = client.Registry.GetConnectionsByTags(
            new Dictionary<string, string> { ["type"] = "core" });
        Console.WriteLine($"Found {coreConnections.Count} core connections");

        // 批量健康检查
        var healthResults = await client.Lifecycle.PerformHealthChecksAsync();
        var unhealthyConnections = healthResults.Where(r => r.Health != ConnectionHealth.Healthy);
        Console.WriteLine($"Found {unhealthyConnections.Count()} unhealthy connections");

        // 清理空闲连接
        var cleanedCount = await client.Lifecycle.CleanupIdleConnectionsAsync(TimeSpan.FromMinutes(30));
        Console.WriteLine($"Cleaned up {cleanedCount} idle connections");
    }

    // 辅助方法
    private ConnectionDescriptor CreateConnection(string id, string host, int port, string region)
    {
        return new ConnectionDescriptor
        {
            Id = id,
            Name = id.Split('-')[0],
            Endpoint = new EndpointAddress { Host = host, Port = port },
            Transport = TransportType.Tcp,
            Strategy = ConnectionStrategy.Session,
            Tags = new Dictionary<string, string>
            {
                ["region"] = region,
                ["type"] = "service"
            }
        };
    }

    private async Task HandleConnectionFailure(string connectionId)
    {
        // 故障恢复逻辑
        await Task.Delay(TimeSpan.FromSeconds(5)); // 等待一段时间
        // 尝试重连或切换到备用连接
    }
}

// 示例服务接口
public interface IAuthenticationService : IPulseService
{
    Task<LoginResult> LoginAsync(string email, string password);
}

public interface IUserService : IPulseService
{
    Task<UserProfile> GetUserProfileAsync(string userId);
}

public interface IOrderService : IPulseService
{
    Task<IReadOnlyList<Order>> GetOrdersAsync(string customerId);
}

public interface IDatabaseService : IPulseService
{
    Task<QueryResult> ExecuteQueryAsync(string sql);
}

public interface IApiService : IPulseService
{
    Task<ApiResponse> ProcessRequestAsync(ApiRequest request);
}

// 示例事件处理器
public interface ISystemEventHandler : IPulseEventHandler
{
    Task OnCriticalErrorAsync(string message);
    Task OnServiceStatusChangedAsync(string serviceName, string status);
}

public class SystemEventHandler : ISystemEventHandler
{
    public Task OnCriticalErrorAsync(string message)
    {
        Console.WriteLine($"Critical error: {message}");
        return Task.CompletedTask;
    }

    public Task OnServiceStatusChangedAsync(string serviceName, string status)
    {
        Console.WriteLine($"Service {serviceName} status changed to {status}");
        return Task.CompletedTask;
    }
}

// 示例数据类型
public record LoginResult(string UserId, string Token);
public record UserProfile(string Id, string Name, string Email);
public record Order(string Id, string CustomerId, decimal Amount);
public record QueryResult(IReadOnlyList<Dictionary<string, object>> Rows);
public record ApiRequest(string Method, string Path, object Data);
public record ApiResponse(int StatusCode, object Data);

// 示例服务发现实现
public class ConsulServiceDiscovery : IServiceDiscovery
{
    private readonly string _consulAddress;

    public ConsulServiceDiscovery(string consulAddress)
    {
        _consulAddress = consulAddress;
    }

    public Task<ServiceDiscoveryResult> DiscoverAsync(ServiceDiscoveryQuery query, CancellationToken cancellationToken = default)
    {
        // Consul服务发现实现
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<ServiceEndpoint>> DiscoverAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        // 简化的服务发现实现
        throw new NotImplementedException();
    }

    public Task<IServiceWatcher> WatchAsync(string serviceName, Action<ServiceChangeEvent> callback, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<string>> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<bool> ExistsAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task RefreshAsync(string? serviceName = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public void Dispose() { }
}

public class KubernetesServiceDiscovery : IServiceDiscovery
{
    public Task<ServiceDiscoveryResult> DiscoverAsync(ServiceDiscoveryQuery query, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<ServiceEndpoint>> DiscoverAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<IServiceWatcher> WatchAsync(string serviceName, Action<ServiceChangeEvent> callback, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<string>> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<bool> ExistsAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task RefreshAsync(string? serviceName = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public void Dispose() { }
}