# PulseRPC

[![NuGet](https://img.shields.io/nuget/v/PulseRPC.svg)](https://www.nuget.org/packages/PulseRPC/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)

一个基于TCP的现代高性能RPC框架，专为.NET平台设计，同时支持Unity开发。

## ✨ 主要特性

### 🚀 核心功能
- **高性能TCP通信**：基于.NET高性能网络栈，支持异步I/O
- **自动服务发现**：集成Consul、Etcd、DNS等多种服务发现机制
- **智能负载均衡**：支持轮询、随机、加权、最少连接、一致性哈希等策略
- **连接池管理**：自动连接复用和生命周期管理
- **健康检查**：实时监控服务状态和自动故障转移

### 📊 监控与追踪
- **性能监控**：全面的系统和应用性能指标收集
- **分布式追踪**：基于OpenTelemetry标准的链路追踪
- **指标收集**：计数器、仪表、直方图、计时器等多种指标类型
- **实时仪表板**：可视化监控面板

### 🛠️ 开发体验
- **代码生成**：基于Source Generator的客户端代码自动生成
- **依赖注入**：原生支持.NET依赖注入容器
- **配置驱动**：灵活的配置系统支持多种配置源
- **跨平台支持**：支持.NET和Unity平台

## 🏗️ 系统架构

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   Client App    │    │   Service App   │    │  Service App    │
│                 │    │                 │    │                 │
│ ┌─────────────┐ │    │ ┌─────────────┐ │    │ ┌─────────────┐ │
│ │ PulseRPC    │ │    │ │ PulseRPC    │ │    │ │ PulseRPC    │ │
│ │ Client      │ │    │ │ Server      │ │    │ │ Server      │ │
│ └─────────────┘ │    │ └─────────────┘ │    │ └─────────────┘ │
└─────────────────┘    └─────────────────┘    └─────────────────┘
         │                       │                       │
         └───────────────────────┼───────────────────────┘
                                 │
         ┌───────────────────────┼───────────────────────┐
         │                       │                       │
         ▼                       ▼                       ▼
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│ Service         │    │ Load Balancer   │    │ Health Monitor  │
│ Discovery       │    │                 │    │                 │
│ • Consul        │    │ • Round Robin   │    │ • TCP Check     │
│ • Etcd          │    │ • Random        │    │ • HTTP Check    │
│ • DNS           │    │ • Weighted      │    │ • Custom Check  │
│ • Zookeeper     │    │ • Consistent    │    │                 │
└─────────────────┘    └─────────────────┘    └─────────────────┘
```

## 🚀 快速开始

### 安装

```bash
# 安装核心包
dotnet add package PulseRPC

# 安装服务发现
dotnet add package PulseRPC.ServiceDiscovery

# 安装监控
dotnet add package PulseRPC.Monitoring

# 安装链路追踪
dotnet add package PulseRPC.Tracing
```

### 1. 定义服务契约

```csharp
public interface IUserService
{
    Task<User> GetUserAsync(int userId);
    Task<User> CreateUserAsync(CreateUserRequest request);
    Task<bool> DeleteUserAsync(int userId);
}

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class CreateUserRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
```

### 2. 实现服务端

```csharp
public class UserService : IUserService
{
    public async Task<User> GetUserAsync(int userId)
    {
        // 实际的业务逻辑
        await Task.Delay(10); // 模拟异步操作
        return new User { Id = userId, Name = $"User{userId}", Email = $"user{userId}@example.com" };
    }

    public async Task<User> CreateUserAsync(CreateUserRequest request)
    {
        // 实际的业务逻辑
        await Task.Delay(50);
        return new User { Id = new Random().Next(1000, 9999), Name = request.Name, Email = request.Email };
    }

    public async Task<bool> DeleteUserAsync(int userId)
    {
        // 实际的业务逻辑
        await Task.Delay(20);
        return true;
    }
}

// 配置服务端
var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((context, services) =>
{
    // 注册业务服务
    services.AddScoped<IUserService, UserService>();
    
    // 添加PulseRPC服务端
    services.AddPulseRpcServer(options =>
    {
        options.Host = "localhost";
        options.Port = 8080;
    });
    
    // 添加服务注册
    services.AddPulseRpcServiceRegistration(options =>
    {
        options.ServiceName = "UserService";
        options.ServiceVersion = "1.0.0";
        options.DiscoveryType = ServiceDiscoveryType.Consul;
        options.ConsulOptions = new ConsulOptions
        {
            Host = "localhost",
            Port = 8500
        };
    });
    
    // 添加监控
    services.AddPulseRpcMonitoring();
    
    // 添加链路追踪
    services.AddPulseRpcTracing(options =>
    {
        options.ServiceName = "UserService";
        options.SamplingRate = 0.1;
    });
});

var host = builder.Build();
await host.RunAsync();
```

### 3. 实现客户端

```csharp
var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((context, services) =>
{
    // 添加PulseRPC客户端
    services.AddPulseRpcClient();
    
    // 配置服务发现
    services.AddPulseRpcServiceDiscovery(options =>
    {
        options.DefaultDiscoveryType = ServiceDiscoveryType.Consul;
        options.ConsulOptions = new ConsulOptions
        {
            Host = "localhost",
            Port = 8500
        };
    });
    
    // 配置负载均衡
    services.AddPulseRpcLoadBalancing(options =>
    {
        options.DefaultStrategy = LoadBalancingStrategy.RoundRobin;
    });
    
    // 注册服务客户端
    services.AddPulseRpcService<IUserService>(options =>
    {
        options.ServiceName = "UserService";
        options.LoadBalancingStrategy = LoadBalancingStrategy.RoundRobin;
    });
});

var host = builder.Build();
var serviceProvider = host.Services;

// 使用服务
var userService = serviceProvider.GetRequiredService<IUserService>();

// 调用远程服务
var user = await userService.GetUserAsync(123);
Console.WriteLine($"用户: {user.Name} ({user.Email})");

var newUser = await userService.CreateUserAsync(new CreateUserRequest 
{ 
    Name = "张三", 
    Email = "zhangsan@example.com" 
});
Console.WriteLine($"创建的用户: {newUser.Id}");

var deleted = await userService.DeleteUserAsync(123);
Console.WriteLine($"删除结果: {deleted}");
```

## 📖 详细文档

- [服务发现配置](docs/ServiceDiscovery.md)
- [负载均衡策略](docs/LoadBalancing.md)
- [健康检查配置](docs/HealthCheck.md)
- [性能监控指南](docs/Monitoring.md)
- [链路追踪使用](docs/Tracing.md)
- [配置参考](docs/Configuration.md)
- [最佳实践](docs/BestPractices.md)

## 🎯 使用场景

### 微服务架构
```csharp
// 用户服务
services.AddPulseRpcService<IUserService>("UserService");

// 订单服务
services.AddPulseRpcService<IOrderService>("OrderService");

// 支付服务
services.AddPulseRpcService<IPaymentService>("PaymentService");
```

### 分布式系统
```csharp
// 自动服务发现和负载均衡
services.AddPulseRpcServiceDiscovery(options =>
{
    options.DefaultDiscoveryType = ServiceDiscoveryType.Consul;
    options.HealthCheckInterval = TimeSpan.FromSeconds(30);
});
```

### 高并发场景
```csharp
// 连接池配置
services.AddPulseRpcClient(options =>
{
    options.ConnectionPool = new ConnectionPoolOptions
    {
        MaxConnections = 100,
        MaxIdleTime = TimeSpan.FromMinutes(5),
        ConnectionTimeout = TimeSpan.FromSeconds(30)
    };
});
```

## 📊 性能特性

- **高吞吐量**：单机支持数万并发连接
- **低延迟**：毫秒级响应时间
- **高可用性**：自动故障转移和服务恢复
- **可扩展性**：水平扩展支持

## 🧪 示例项目

项目包含了丰富的示例代码：

- [基础RPC示例](samples/BasicExample/)
- [服务发现示例](samples/ServiceDiscoveryExample/)
- [负载均衡示例](samples/LoadBalancingExample/)
- [监控系统示例](samples/MonitoringExample/)
- [链路追踪示例](samples/TracingExample/)
- [完整应用示例](samples/CompleteExample/)

## 🔧 配置示例

### appsettings.json
```json
{
  "PulseRPC": {
    "Server": {
      "Host": "localhost",
      "Port": 8080,
      "MaxConnections": 1000
    },
    "ServiceDiscovery": {
      "DefaultType": "Consul",
      "Consul": {
        "Host": "localhost",
        "Port": 8500
      }
    },
    "LoadBalancing": {
      "DefaultStrategy": "RoundRobin"
    },
    "Monitoring": {
      "Performance": {
        "Enabled": true,
        "SamplingInterval": "00:00:10"
      }
    },
    "Tracing": {
      "Enabled": true,
      "SamplingRate": 0.1,
      "ServiceName": "MyService"
    }
  }
}
```

## 🤝 贡献

欢迎贡献代码、报告问题或提出建议！

1. Fork 项目
2. 创建功能分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 开启 Pull Request

## 📄 许可证

本项目采用 MIT 许可证 - 查看 [LICENSE](LICENSE) 文件了解详情。

## 📞 支持

- 📧 Email: support@pulserpc.com
- 📖 文档: https://docs.pulserpc.com
- 🐛 问题反馈: https://github.com/pulserpc/pulserpc/issues
- 💬 讨论: https://github.com/pulserpc/pulserpc/discussions

---

⭐ 如果这个项目对你有帮助，请给我们一个星标！ 