# UnifiedPulseServer DI 集成指南

## 概述

UnifiedPulseServer 完全支持 Microsoft.Extensions.DependencyInjection，可以无缝集成到 ASP.NET Core、通用主机（Generic Host）等基于 DI 的应用中。

## 三种 DI 集成方式

### 方式 1: 简单配置（推荐用于快速开始）

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PulseRPC.Server;
using PulseRPC.Server.Extensions;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((context, services) =>
{
    services.AddUnifiedPulseServer(options =>
    {
        options.Transports.Add(
            TransportChannelConfiguration.Tcp("tcp", 8080, isDefault: true));

        options.DefaultOperationTimeout = TimeSpan.FromSeconds(60);
        options.MaxConcurrentOperations = 5000;
    });
});

var host = builder.Build();
await host.RunAsync();
```

### 方式 2: 从配置文件加载（推荐用于生产环境）

**appsettings.json**:
```json
{
  "UnifiedPulseServer": {
    "Transports": [
      {
        "Name": "tcp",
        "Type": "Tcp",
        "Port": 8080,
        "IsDefault": true
      },
      {
        "Name": "kcp",
        "Type": "Kcp",
        "Port": 9090,
        "IsDefault": false
      }
    ],
    "DefaultOperationTimeout": "00:01:00",
    "MaxConcurrentOperations": 5000,
    "EnableDetailedLogging": false
  }
}
```

**Program.cs**:
```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PulseRPC.Server.Extensions;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((context, services) =>
{
    // 从配置节加载
    var serverConfig = context.Configuration.GetSection("UnifiedPulseServer");
    services.AddUnifiedPulseServer(serverConfig);
});

var host = builder.Build();
await host.RunAsync();
```

### 方式 3: 构建器模式（推荐用于复杂配置）

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PulseRPC.Server.Extensions;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((context, services) =>
{
    services.AddUnifiedPulseServerBuilder()
        .AddTcpTransport("tcp", 8080, isDefault: true, configure: tcpOptions =>
        {
            tcpOptions.NoDelay = true;
            tcpOptions.RecvBufferSize = 65536;
            tcpOptions.SendBufferSize = 65536;
        })
        .AddKcpTransport("kcp", 9090, configure: kcpOptions =>
        {
            kcpOptions.NoDelay = 1;
            kcpOptions.Interval = 10;
            kcpOptions.Resend = 2;
            kcpOptions.NoCongestionControl = 1;
        })
        .ConfigureOptions(options =>
        {
            options.DefaultOperationTimeout = TimeSpan.FromSeconds(60);
            options.MaxConcurrentOperations = 10000;
            options.MessageDispatcher.WorkerCount = Environment.ProcessorCount;
        })
        .Build();
});

var host = builder.Build();
await host.RunAsync();
```

## ASP.NET Core 集成

### 最小 API 集成

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using PulseRPC.Server;
using PulseRPC.Server.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 添加 UnifiedPulseServer
builder.Services.AddUnifiedPulseServer(options =>
{
    options.Transports.Add(
        TransportChannelConfiguration.Tcp("tcp", 8080, isDefault: true));
});

// 添加其他服务
builder.Services.AddControllers();

var app = builder.Build();

// UnifiedPulseServer 作为 IHostedService 自动启动
app.MapControllers();

await app.RunAsync();
```

### 服务注册示例

```csharp
using Microsoft.Extensions.DependencyInjection;
using PulseRPC.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddUnifiedPulseServer(options =>
{
    options.Transports.Add(
        TransportChannelConfiguration.Tcp("tcp", 8080, isDefault: true));
});

// 注册 RPC 服务
builder.Services.AddSingleton<IMyRpcService, MyRpcService>();

var app = builder.Build();

// 获取服务器实例并注册服务
var server = app.Services.GetRequiredService<UnifiedPulseServer>();
var myService = app.Services.GetRequiredService<IMyRpcService>();

server.RegisterService("MyService", myService);

await app.RunAsync();
```

## 高级场景

### 1. 使用启动过滤器注册服务

```csharp
public class RpcServiceStartupFilter : IStartupFilter
{
    private readonly UnifiedPulseServer _server;
    private readonly IServiceProvider _serviceProvider;

    public RpcServiceStartupFilter(
        UnifiedPulseServer server,
        IServiceProvider serviceProvider)
    {
        _server = server;
        _serviceProvider = serviceProvider;
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            // 自动注册所有标记为 RPC 服务的类
            var services = _serviceProvider.GetServices<IRpcService>();
            foreach (var service in services)
            {
                var serviceName = service.GetType().Name;
                _server.RegisterService(serviceName, service);
            }

            next(app);
        };
    }
}

// 注册启动过滤器
builder.Services.AddSingleton<IStartupFilter, RpcServiceStartupFilter>();
```

### 2. 健康检查集成

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;

public class UnifiedPulseServerHealthCheck : IHealthCheck
{
    private readonly UnifiedPulseServer _server;

    public UnifiedPulseServerHealthCheck(UnifiedPulseServer server)
    {
        _server = server;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_server.IsRunning)
            {
                var metrics = _server.GetPerformanceMetrics();
                var data = new Dictionary<string, object>
                {
                    ["ActiveConnections"] = metrics.ActiveConnections,
                    ["TotalMessagesProcessed"] = metrics.TotalMessagesProcessed,
                    ["MemoryUsageMB"] = metrics.MemoryUsageMB
                };

                return Task.FromResult(
                    HealthCheckResult.Healthy("Server is running", data));
            }

            return Task.FromResult(
                HealthCheckResult.Unhealthy("Server is not running"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                HealthCheckResult.Unhealthy("Health check failed", ex));
        }
    }
}

// 注册健康检查
builder.Services.AddHealthChecks()
    .AddCheck<UnifiedPulseServerHealthCheck>("pulserpc-server");

app.MapHealthChecks("/health");
```

### 3. 监控和指标导出

```csharp
using System.Diagnostics.Metrics;

public class UnifiedPulseServerMetricsCollector : BackgroundService
{
    private readonly UnifiedPulseServer _server;
    private readonly Meter _meter;
    private readonly ILogger<UnifiedPulseServerMetricsCollector> _logger;

    public UnifiedPulseServerMetricsCollector(
        UnifiedPulseServer server,
        IMeterFactory meterFactory,
        ILogger<UnifiedPulseServerMetricsCollector> logger)
    {
        _server = server;
        _logger = logger;
        _meter = meterFactory.Create("PulseRPC.Server");

        // 创建可观测指标
        _meter.CreateObservableGauge(
            "pulserpc.server.connections.active",
            () => _server.ActiveConnectionCount,
            description: "Current number of active connections");

        _meter.CreateObservableCounter(
            "pulserpc.server.messages.processed",
            () => _server.GetPerformanceMetrics().TotalMessagesProcessed,
            description: "Total number of messages processed");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var metrics = _server.GetPerformanceMetrics();
            _logger.LogInformation(
                "Server Metrics: Connections={Connections}, Messages={Messages}, Memory={Memory:F2}MB",
                metrics.ActiveConnections,
                metrics.TotalMessagesProcessed,
                metrics.MemoryUsageMB);

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}

// 注册指标收集器
builder.Services.AddHostedService<UnifiedPulseServerMetricsCollector>();
```

### 4. 配置热重载

```csharp
using Microsoft.Extensions.Options;

public class UnifiedPulseServerConfigurationMonitor : BackgroundService
{
    private readonly IOptionsMonitor<UnifiedServerOptions> _optionsMonitor;
    private readonly ILogger<UnifiedPulseServerConfigurationMonitor> _logger;

    public UnifiedPulseServerConfigurationMonitor(
        IOptionsMonitor<UnifiedServerOptions> optionsMonitor,
        ILogger<UnifiedPulseServerConfigurationMonitor> logger)
    {
        _optionsMonitor = optionsMonitor;
        _logger = logger;

        // 监听配置变化
        _optionsMonitor.OnChange(options =>
        {
            _logger.LogInformation("Server configuration changed");
            // 注意：某些配置需要重启服务器才能生效
        });
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }
}
```

## 完整示例：高性能 RPC 服务器

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Server;
using PulseRPC.Server.Extensions;

// 创建主机
var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // 配置 UnifiedPulseServer
        services.AddUnifiedPulseServerBuilder()
            .AddTcpTransport("tcp", 8080, isDefault: true, configure: opts =>
            {
                opts.NoDelay = true;
                opts.RecvBufferSize = 131072;  // 128 KB
                opts.SendBufferSize = 131072;  // 128 KB
            })
            .ConfigureOptions(options =>
            {
                options.DefaultOperationTimeout = TimeSpan.FromSeconds(30);
                options.MaxConcurrentOperations = 10000;

                // 配置消息分发器
                options.MessageDispatcher.WorkerCount = Environment.ProcessorCount * 2;
                options.MessageDispatcher.MaxQueueDepthPerPriority = 5000;

                // 配置背压策略
                options.BackpressurePolicy.ThrottleThreshold = 0.7;
                options.BackpressurePolicy.RejectThreshold = 0.9;

                // 配置连接管理
                options.ConnectionManager.MaxConnections = 10000;
                options.ConnectionManager.ConnectionTimeout = TimeSpan.FromMinutes(5);
            })
            .Build();

        // 注册业务服务
        services.AddSingleton<IUserService, UserService>();
        services.AddSingleton<IOrderService, OrderService>();

        // 注册健康检查
        services.AddHealthChecks()
            .AddCheck<UnifiedPulseServerHealthCheck>("pulserpc");

        // 注册指标收集
        services.AddHostedService<UnifiedPulseServerMetricsCollector>();
    })
    .ConfigureLogging((context, logging) =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    });

var host = builder.Build();

// 注册 RPC 服务
var server = host.Services.GetRequiredService<UnifiedPulseServer>();
var userService = host.Services.GetRequiredService<IUserService>();
var orderService = host.Services.GetRequiredService<IOrderService>();

server.RegisterService("UserService", userService);
server.RegisterService("OrderService", orderService);

// 启动主机（UnifiedPulseServer 作为 IHostedService 自动启动）
Console.WriteLine("Starting RPC Server...");
await host.RunAsync();
```

## 生命周期管理

### 自动启动和停止

UnifiedPulseServer 通过 `UnifiedPulseServerHostedService` 实现 `IHostedService`，因此：

1. **应用启动时**：自动调用 `StartAsync()`，启动所有传输监听器
2. **应用停止时**：自动调用 `StopAsync()`，优雅关闭服务器
3. **异常处理**：启动失败时会阻止应用启动，停止失败会记录错误

### 手动控制（不推荐）

如果需要手动控制服务器生命周期（不使用 IHostedService）：

```csharp
builder.Services.Configure<UnifiedServerOptions>(options => { /* ... */ });
builder.Services.AddSingleton<ITransportIntegrationManager, TransportIntegrationManager>();
builder.Services.AddSingleton<IServerChannelManager, ServerChannelManager>();
builder.Services.AddSingleton<UnifiedPulseServer>();

var host = builder.Build();
var server = host.Services.GetRequiredService<UnifiedPulseServer>();

// 手动启动
await server.StartAsync();

// 应用逻辑
// ...

// 手动停止
await server.StopAsync();
```

## 性能调优建议

### 1. Worker 线程数量

```csharp
options.MessageDispatcher.WorkerCount = Environment.ProcessorCount; // CPU 密集型
options.MessageDispatcher.WorkerCount = Environment.ProcessorCount * 2; // I/O 密集型
```

### 2. 缓冲区大小

```csharp
.AddTcpTransport("tcp", 8080, isDefault: true, configure: opts =>
{
    opts.RecvBufferSize = 131072;  // 128 KB for high throughput
    opts.SendBufferSize = 131072;
});
```

### 3. 连接限制

```csharp
options.ConnectionManager.MaxConnections = 10000; // 根据服务器资源调整
```

### 4. 背压策略

```csharp
options.BackpressurePolicy.ThrottleThreshold = 0.7;  // 70% 开始限流
options.BackpressurePolicy.RejectThreshold = 0.9;    // 90% 拒绝新请求
```

## 故障排查

### 服务器未自动启动

检查是否正确注册了 `IHostedService`：

```csharp
services.AddHostedService<UnifiedPulseServerHostedService>();
```

### 端口冲突

检查端口是否已被占用，或使用端口 0 让系统自动分配：

```csharp
options.Transports.Add(
    TransportChannelConfiguration.Tcp("tcp", 0, isDefault: true)); // 自动分配端口
```

### 配置验证失败

确保配置满足验证规则：
- 至少配置一个传输
- 有且仅有一个默认传输
- 传输名称唯一
- 端口范围 1-65535
- 超时时间为正数

## 另请参阅

- [UnifiedPulseServer 使用指南](UnifiedPulseServer.README.md)
- [配置参考](Configuration-Reference.md)
- [性能调优](Performance-Tuning.md)
