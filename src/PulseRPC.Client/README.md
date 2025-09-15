# PulseRPC.Client

PulseRPC.Client is a high-performance, enterprise-grade network client library that provides intelligent connection management, advanced routing, and seamless service discovery for distributed applications.

## Features

- **Intelligent Connection Management**: Multi-strategy connection lifecycle with automatic pooling and cleanup
- **Advanced Service Discovery**: Support for Consul, Kubernetes, Etcd, DNS, and custom discovery mechanisms
- **Smart Load Balancing**: 8+ built-in strategies including consistent hashing, least connections, and custom algorithms
- **Connection Pooling**: High-performance connection pools with lease-based resource management
- **Dynamic Routing**: Rule-based intelligent request routing with context-aware decisions
- **Multi-Transport Support**: TCP and KCP protocols with pluggable transport architecture
- **Enterprise Reliability**: Health monitoring, automatic failover, and comprehensive retry policies
- **Real-time Monitoring**: Detailed statistics, performance metrics, and health reporting
- **Source Code Generation**: Zero-reflection service proxies via compile-time code generation
- **Cross-Platform**: Supports .NET 9+ and Unity (netstandard2.1)

## Architecture Overview

### Core Component Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    IPulseRPCClient                         │
├─────────────────────────────────────────────────────────────┤
│  ┌──────────────┐  ┌──────────────┐  ┌─────────────────┐   │
│  │   Service    │  │   Event      │  │    Routing      │   │
│  │   Proxies    │  │  Listeners   │  │   & Discovery   │   │
│  └──────────────┘  └──────────────┘  └─────────────────┘   │
├─────────────────────────────────────────────────────────────┤
│  ┌──────────────┐  ┌──────────────┐  ┌─────────────────┐   │
│  │ Connection   │  │ Connection   │  │  Load Balancer  │   │
│  │  Registry    │  │  Lifecycle   │  │   & Router      │   │
│  └──────────────┘  └──────────────┘  └─────────────────┘   │
├─────────────────────────────────────────────────────────────┤
│  ┌──────────────────────────────────────────────────────┐   │
│  │            Connection Pool Manager                   │   │
│  │  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐   │   │
│  │  │TCP Pool │ │KCP Pool │ │Session  │ │OnDemand │   │   │
│  │  └─────────┘ └─────────┘ └─────────┘ └─────────┘   │   │
│  └──────────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────────┤
│  ┌──────────────────────────────────────────────────────┐   │
│  │              Service Discovery Layer                 │   │
│  │  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐   │   │
│  │  │ Consul  │ │   K8s   │ │  Etcd   │ │ Custom  │   │   │
│  │  └─────────┘ └─────────┘ └─────────┘ └─────────┘   │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

### Key Interfaces

- **`IPulseRPCClient`**: Unified client entry point with comprehensive management capabilities
- **`IConnectionRegistry`**: Connection registration, querying, and lifecycle tracking
- **`IConnectionLifecycleManager`**: Connection creation, destruction, and health management
- **`IConnectionPool`**: High-performance connection pooling with lease-based resource control
- **`IConnectionRouter`**: Intelligent routing with rule-based decision making
- **`IServiceDiscovery`**: Dynamic service discovery with real-time change monitoring
- **`ILoadBalancer`**: Advanced load balancing with performance-based selection

## Quick Start

### Basic Client Setup

```csharp
using PulseRPC.Client.Core;

// Create and configure client
var client = new PulseRPCClientBuilder()
    .AddConnection(new ConnectionDescriptor
    {
        Id = "main-service",
        Name = "api-service",
        ServiceName = "user-api-service",
        Transport = TransportType.Tcp,
        Strategy = ConnectionStrategy.Persistent,
        AutoReconnect = true
    })
    .WithServiceDiscovery(new ConsulServiceDiscovery("http://consul:8500"))
    .WithLoadBalancing(LoadBalancingStrategy.WeightedRoundRobin)
    .Build();

// Initialize and connect
await client.InitializeAsync();

// Get service proxy
var userService = await client.GetServiceAsync<IUserService>();
var user = await userService.GetUserAsync(123);

// Graceful shutdown
await client.StopAsync();
```

### Service Discovery Integration

```csharp
var client = new PulseRPCClientBuilder()
    .WithServiceDiscovery(new KubernetesServiceDiscovery())
    .WithLoadBalancing(LoadBalancingStrategy.LeastConnections)
    .Configure(options =>
    {
        options.DefaultTimeout = TimeSpan.FromSeconds(30);
        options.EnableStatistics = true;
    })
    .Build();

await client.InitializeAsync();

// Dynamic service connection
var orderConnection = await client.ConnectToServiceAsync("order-service", 
    new ServiceConnectionOptions
    {
        Strategy = ConnectionStrategy.Session,
        PreferredTransport = TransportType.Tcp,
        LoadBalancingHint = LoadBalancingHint.LeastConnections,
        Tags = new Dictionary<string, string> { ["version"] = "v2.0" }
    });

var orderService = await orderConnection.GetServiceAsync<IOrderService>();
```

## Advanced Features

### Connection Pool Management

```csharp
var poolFactory = new ConnectionPoolFactory();

// Create high-performance connection pool
var dbPool = poolFactory.CreatePool(
    "database-pool",
    new ConnectionDescriptor
    {
        Id = "db-template",
        Name = "database",
        ServiceName = "postgres-cluster",
        Transport = TransportType.Tcp,
        Strategy = ConnectionStrategy.Pooled
    },
    new ConnectionPoolOptions
    {
        Strategy = PoolingStrategy.Dynamic,
        MinSize = 5,
        MaxSize = 50,
        AcquireTimeout = TimeSpan.FromSeconds(10),
        IdleTimeout = TimeSpan.FromMinutes(5),
        ValidateOnAcquire = true
    });

await dbPool.InitializeAsync();

// Lease-based resource management
using (var lease = await dbPool.AcquireAsync())
{
    var dbService = await lease.Connection.GetServiceAsync<IDatabaseService>();
    var result = await dbService.ExecuteQueryAsync("SELECT * FROM users");
    // Connection automatically returned to pool
}

// Monitor pool health
var stats = dbPool.GetStatistics();
Console.WriteLine($"Pool utilization: {stats.ActiveConnections}/{stats.CurrentSize}");
```

### Intelligent Routing

```csharp
// Register custom routing rules
client.Router.RegisterRule(new RoutingRule
{
    Id = "admin-routing",
    Name = "Route admin requests to premium servers",
    Matcher = (key, context) => context?.Tags.GetValueOrDefault("user_type") == "admin",
    Selector = (connections, context) => 
        connections.FirstOrDefault(c => c.Descriptor.Tags.GetValueOrDefault("tier") == "premium"),
    Priority = 100
});

client.Router.RegisterRule(new RoutingRule
{
    Id = "region-affinity",
    Name = "Route by geographic region",
    Matcher = (key, context) => !string.IsNullOrEmpty(context?.PreferredRegion),
    Selector = (connections, context) => 
        connections.FirstOrDefault(c => c.Descriptor.Tags.GetValueOrDefault("region") == context?.PreferredRegion),
    Priority = 50
});

// Use routing context for intelligent decisions
var routingContext = new RoutingContext
{
    UserId = "admin-user-123",
    Tags = new Dictionary<string, string> { ["user_type"] = "admin" },
    PreferredRegion = "us-west-2",
    LoadBalancingHint = LoadBalancingHint.ConsistentHash
};

var connection = await client.Router.RouteAsync("api-service", routingContext);
var apiService = await connection.GetServiceAsync<IApiService>();
```

### Load Balancing Strategies

```csharp
// Configure advanced load balancing
var client = new PulseRPCClientBuilder()
    .WithLoadBalancing(LoadBalancingStrategy.ConsistentHash, new Dictionary<string, object>
    {
        ["virtual_nodes"] = 100,
        ["hash_function"] = "sha256"
    })
    .Build();

// Custom load balancing strategy
var loadBalancerFactory = new LoadBalancerFactory();
loadBalancerFactory.RegisterCustomStrategy("weighted-response-time", options =>
{
    return new WeightedResponseTimeLoadBalancer(options);
});

var customClient = new PulseRPCClientBuilder()
    .WithLoadBalancing(LoadBalancingStrategy.Custom, new Dictionary<string, object>
    {
        ["strategy_name"] = "weighted-response-time",
        ["response_time_window"] = TimeSpan.FromMinutes(5)
    })
    .Build();
```

### Event-Driven Architecture

```csharp
// Register event listeners with advanced options
var eventHandler = new SystemEventHandler();
var subscription = await client.RegisterEventListenerAsync<ISystemEventHandler>(
    eventHandler,
    new EventListenerOptions
    {
        EventFilter = evt => evt.GetType().Name.Contains("Critical"),
        ErrorHandling = EventErrorHandlingStrategy.Log
    });

// Watch service changes in real-time
var serviceWatcher = await client.ServiceDiscovery.WatchAsync("payment-service", change =>
{
    Console.WriteLine($"Service change: {change.ChangeType} for {change.ServiceName}");
    
    if (change.ChangeType == ServiceChangeType.EndpointAdded && change.Endpoint != null)
    {
        // Dynamically add new connection
        _ = Task.Run(async () =>
        {
            await client.ConnectToServiceAsync(change.ServiceName);
        });
    }
});
```

### Health Monitoring and Reliability

```csharp
// Configure comprehensive reliability features
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
        options.MaxConcurrentConnections = 200;
    })
    .Build();

// Monitor client health
var healthResult = await client.CheckHealthAsync();
Console.WriteLine($"Overall health: {healthResult.OverallHealth}");

foreach (var connHealth in healthResult.ConnectionResults)
{
    Console.WriteLine($"Connection {connHealth.ConnectionId}: {connHealth.Health} " +
                     $"(Response: {connHealth.ResponseTime.TotalMilliseconds:F1}ms)");
}

// Get comprehensive statistics
var stats = client.GetStatistics();
Console.WriteLine($"""
    Client Performance:
    - Uptime: {stats.Uptime}
    - Active Connections: {stats.ActiveConnections}/{stats.TotalConnections}
    - Request Success Rate: {(double)stats.SuccessfulRequests / stats.TotalRequests:P2}
    - Average Response Time: {stats.AverageResponseTimeMs:F2}ms
    - Throughput: {stats.BytesSent + stats.BytesReceived:N0} bytes
    """);
```

### Batch Operations and Management

```csharp
// Batch connection management
var disconnectedCount = await client.DisconnectAsync(
    conn => conn.Descriptor.Tags.GetValueOrDefault("environment") == "staging",
    graceful: true);

// Query connections by criteria
var coreServices = client.Registry.GetConnectionsByTags(
    new Dictionary<string, string> { ["type"] = "core-service" });

var healthyConnections = client.Registry.GetConnectionsByState(
    new HashSet<ConnectionState> { ConnectionState.Connected });

// Bulk health checks
var healthResults = await client.Lifecycle.PerformHealthChecksAsync();
var unhealthyCount = healthResults.Count(r => r.Health != ConnectionHealth.Healthy);

// Automated cleanup
var cleanedCount = await client.Lifecycle.CleanupIdleConnectionsAsync(TimeSpan.FromMinutes(30));
var expiredCount = await client.Lifecycle.CleanupExpiredConnectionsAsync();

Console.WriteLine($"Maintenance: Cleaned {cleanedCount} idle, {expiredCount} expired connections");
```

## Configuration Options

### Connection Strategies

```csharp
// Persistent connections for core services
new ConnectionDescriptor
{
    Strategy = ConnectionStrategy.Persistent,  // Long-lived, auto-reconnect
    AutoReconnect = true,
    TimeToLive = null  // No expiration
}

// Session-based connections for user interactions
new ConnectionDescriptor
{
    Strategy = ConnectionStrategy.Session,     // Medium-lived, context-aware
    IdleTimeout = TimeSpan.FromMinutes(10),
    TimeToLive = TimeSpan.FromHours(2)
}

// On-demand connections for sporadic operations
new ConnectionDescriptor
{
    Strategy = ConnectionStrategy.OnDemand,    // Created when needed
    IdleTimeout = TimeSpan.FromMinutes(1),
    AutoReconnect = false
}

// Pooled connections for high-throughput scenarios
new ConnectionDescriptor
{
    Strategy = ConnectionStrategy.Pooled,      // Managed by connection pool
    TimeToLive = TimeSpan.FromMinutes(30)
}
```

### Transport Configuration

```csharp
// TCP for reliable, ordered communication
.WithTransportOptions(TransportType.Tcp, new TransportOptions
{
    ConnectionTimeout = 10000,
    KeepAlive = true,
    NoDelay = true,
    ReceiveBufferSize = 65536,
    SendBufferSize = 65536
})

// KCP for low-latency, real-time communication  
.WithTransportOptions(TransportType.Kcp, new TransportOptions
{
    Kcp = new KcpOptions
    {
        NoDelay = 1,        // Disable Nagle-like algorithm
        Interval = 10,      // 10ms update interval
        Resend = 2,         // Fast resend mode
        DisableFlowControl = false
    }
})
```

## Service Proxy Generation

PulseRPC.Client uses compile-time source generation for efficient service proxies:

```csharp
// Define service interface
[PulseService]
public interface ICalculatorService : IPulseService
{
    Task<int> AddAsync(int a, int b);
    Task<double> DivideAsync(double a, double b);
    Task<ComplexResult> ComplexOperationAsync(ComplexInput input);
}

// Generated proxy usage (zero reflection overhead)
var calculator = await client.GetServiceAsync<ICalculatorService>();
var result = await calculator.AddAsync(5, 3);

// Event handler interface
[PulseEventHandler]
public interface INotificationHandler : IPulseEventHandler
{
    Task OnMessageReceivedAsync(string message);
    Task OnUserConnectedAsync(string userId);
}

// Event handler registration
var handler = new NotificationHandler();
var subscription = await client.RegisterEventListenerAsync<INotificationHandler>(handler);
```

## Enterprise Integration

### Dependency Injection

```csharp
using Microsoft.Extensions.DependencyInjection;

// Configure services
services.AddSingleton<IPulseRPCClient>(provider =>
{
    return new PulseRPCClientBuilder()
        .WithServiceDiscovery(provider.GetRequiredService<IServiceDiscovery>())
        .WithLogging(provider.GetRequiredService<ILoggerFactory>())
        .WithAuthentication(provider.GetRequiredService<IAuthenticationProvider>())
        .Configure(options =>
        {
            options.DefaultTimeout = TimeSpan.FromSeconds(30);
            options.EnableStatistics = true;
        })
        .Build();
});

// Use in controllers/services
public class OrderController : ControllerBase
{
    private readonly IPulseRPCClient _client;
    
    public OrderController(IPulseRPCClient client)
    {
        _client = client;
    }
    
    public async Task<IActionResult> GetOrder(int id)
    {
        var orderService = await _client.GetServiceAsync<IOrderService>();
        var order = await orderService.GetOrderAsync(id);
        return Ok(order);
    }
}
```

### Microservices Architecture

```csharp
// Service mesh integration
var client = new PulseRPCClientBuilder()
    .WithServiceDiscovery(new ServiceMeshDiscovery())
    .WithLoadBalancing(LoadBalancingStrategy.LeastConnections)
    .Configure(options =>
    {
        options.Name = "order-service-client";
        options.EnablePerformanceMonitoring = true;
    })
    .Build();

// Circuit breaker pattern
client.Registry.ConnectionRegistered += (sender, args) =>
{
    var connection = args.Connection;
    var circuitBreaker = new CircuitBreaker(connection);
    
    connection.StateChanged += (connSender, connArgs) =>
    {
        if (connArgs.CurrentState == ConnectionState.Failed)
        {
            circuitBreaker.TripCircuit();
        }
    };
};
```

## Performance Characteristics

- **Zero Reflection**: Service proxies generated at compile time
- **Connection Pooling**: Efficient resource reuse with lease-based management
- **Smart Routing**: Context-aware connection selection
- **Async Throughout**: Fully asynchronous operation with cancellation support
- **Memory Efficient**: Object pooling and buffer reuse
- **Network Optimized**: Protocol-specific optimizations (TCP/KCP)

## Platform Support

### .NET Applications
- **.NET 9.0+**: Full feature set with latest performance optimizations
- **ASP.NET Core**: Seamless integration with dependency injection
- **Worker Services**: Background service and long-running application support

### Unity Game Development
- **Unity 2022.3+ LTS**: Full compatibility with netstandard2.1
- **IL2CPP**: Ahead-of-time compilation support
- **Mobile Platforms**: iOS and Android optimization

```csharp
// Unity integration example
public class GameNetworkManager : MonoBehaviour
{
    private IPulseRPCClient _client;
    
    async void Start()
    {
        _client = new PulseRPCClientBuilder()
            .AddConnection(new ConnectionDescriptor
            {
                Id = "game-server",
                Name = "game",
                Endpoint = new EndpointAddress { Host = "game.company.com", Port = 9090 },
                Transport = TransportType.Kcp,  // Low latency for games
                Strategy = ConnectionStrategy.Persistent
            })
            .Configure(options =>
            {
                options.DefaultTimeout = TimeSpan.FromSeconds(15);
                options.EnableStatistics = false;  // Reduce overhead on mobile
            })
            .Build();
            
        await _client.InitializeAsync();
    }
    
    void OnDestroy()
    {
        _client?.Dispose();
    }
}
```

## Dependencies

### Core Dependencies
- **PulseRPC.Abstractions**: Core interfaces and types
- **Microsoft.Extensions.Logging.Abstractions**: Logging infrastructure
- **System.Text.Json**: High-performance JSON serialization

### Development Dependencies
- **PulseRPC.Client.SourceGenerator**: Compile-time proxy generation
- **Microsoft.CodeAnalysis.PublicApiAnalyzers**: API compatibility validation

### Optional Dependencies
- **Microsoft.Extensions.DependencyInjection**: IoC container integration
- **Microsoft.Extensions.Options**: Configuration binding
- **Microsoft.Extensions.Hosting**: Hosted service integration

## Source Generator Integration

The client library includes an integrated Source Generator for maximum performance:

```xml
<ItemGroup>
  <ProjectReference Include="..\PulseRPC.Client.SourceGenerator\PulseRPC.Client.SourceGenerator.csproj" 
                    ReferenceOutputAssembly="false" 
                    OutputItemType="Analyzer" />
</ItemGroup>
```

Generated proxies are compatible with C# 9.0+ and follow strict performance guidelines.

## License

This project is part of the PulseRPC framework. See the main project LICENSE file for details.

## Contributing

Please refer to the main PulseRPC project for contribution guidelines and development setup instructions.