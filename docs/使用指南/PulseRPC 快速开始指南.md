# PulseRPC 快速开始指南

本指南将帮助您快速上手PulseRPC框架，从安装到运行您的第一个RPC服务。

## 📋 前置条件

- .NET 8.0 SDK
- Visual Studio 2022 或 JetBrains Rider（推荐）
- 可选：Docker（用于运行Consul等外部依赖）

## 🚀 第一步：创建项目

### 1. 创建解决方案

```bash
mkdir MyRpcApp
cd MyRpcApp
dotnet new sln
```

### 2. 创建服务端项目

```bash
dotnet new console -n MyRpcApp.Server
dotnet sln add MyRpcApp.Server
cd MyRpcApp.Server
```

### 3. 创建客户端项目

```bash
cd ..
dotnet new console -n MyRpcApp.Client
dotnet sln add MyRpcApp.Client
cd MyRpcApp.Client
```

### 4. 创建共享契约项目

```bash
cd ..
dotnet new classlib -n MyRpcApp.Contracts
dotnet sln add MyRpcApp.Contracts
cd MyRpcApp.Contracts
```

## 📦 第二步：安装NuGet包

### 服务端依赖

```bash
cd ../MyRpcApp.Server
dotnet add package PulseRPC
dotnet add package PulseRPC.ServiceDiscovery
dotnet add package PulseRPC.Monitoring
dotnet add package PulseRPC.Tracing
dotnet add package Microsoft.Extensions.Hosting
dotnet add reference ../MyRpcApp.Contracts
```

### 客户端依赖

```bash
cd ../MyRpcApp.Client
dotnet add package PulseRPC
dotnet add package PulseRPC.ServiceDiscovery
dotnet add package PulseRPC.Monitoring
dotnet add package PulseRPC.Tracing
dotnet add package Microsoft.Extensions.Hosting
dotnet add reference ../MyRpcApp.Contracts
```

## 🔧 第三步：定义服务契约

在 `MyRpcApp.Contracts/IUserService.cs` 中定义服务接口：

```csharp
namespace MyRpcApp.Contracts
{
    /// <summary>
    /// 用户服务接口
    /// </summary>
    public interface IUserService
    {
        /// <summary>
        /// 根据ID获取用户
        /// </summary>
        Task<User> GetUserAsync(int userId);

        /// <summary>
        /// 创建新用户
        /// </summary>
        Task<User> CreateUserAsync(CreateUserRequest request);

        /// <summary>
        /// 更新用户信息
        /// </summary>
        Task<User> UpdateUserAsync(int userId, UpdateUserRequest request);

        /// <summary>
        /// 删除用户
        /// </summary>
        Task<bool> DeleteUserAsync(int userId);

        /// <summary>
        /// 获取用户列表
        /// </summary>
        Task<UserListResponse> GetUsersAsync(GetUsersRequest request);
    }

    /// <summary>
    /// 用户实体
    /// </summary>
    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public UserStatus Status { get; set; }
    }

    /// <summary>
    /// 用户状态
    /// </summary>
    public enum UserStatus
    {
        Active = 1,
        Inactive = 2,
        Suspended = 3
    }

    /// <summary>
    /// 创建用户请求
    /// </summary>
    public class CreateUserRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    /// <summary>
    /// 更新用户请求
    /// </summary>
    public class UpdateUserRequest
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public UserStatus? Status { get; set; }
    }

    /// <summary>
    /// 获取用户列表请求
    /// </summary>
    public class GetUsersRequest
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public string? NameFilter { get; set; }
        public UserStatus? StatusFilter { get; set; }
    }

    /// <summary>
    /// 用户列表响应
    /// </summary>
    public class UserListResponse
    {
        public List<User> Users { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
    }
}
```

## 🖥️ 第四步：实现服务端

在 `MyRpcApp.Server/UserService.cs` 中实现服务：

```csharp
using MyRpcApp.Contracts;
using Microsoft.Extensions.Logging;

namespace MyRpcApp.Server
{
    /// <summary>
    /// 用户服务实现
    /// </summary>
    public class UserService : IUserService
    {
        private readonly ILogger<UserService> _logger;
        private static readonly List<User> _users = new();
        private static int _nextId = 1;

        public UserService(ILogger<UserService> logger)
        {
            _logger = logger;
            InitializeData();
        }

        public async Task<User> GetUserAsync(int userId)
        {
            _logger.LogInformation("获取用户信息: {UserId}", userId);
            
            await Task.Delay(50); // 模拟数据库查询延迟
            
            var user = _users.FirstOrDefault(u => u.Id == userId);
            if (user == null)
            {
                throw new ArgumentException($"用户不存在: {userId}");
            }

            return user;
        }

        public async Task<User> CreateUserAsync(CreateUserRequest request)
        {
            _logger.LogInformation("创建用户: {Name}, {Email}", request.Name, request.Email);
            
            // 验证邮箱唯一性
            if (_users.Any(u => u.Email == request.Email))
            {
                throw new InvalidOperationException($"邮箱已存在: {request.Email}");
            }

            await Task.Delay(100); // 模拟数据库写入延迟

            var user = new User
            {
                Id = _nextId++,
                Name = request.Name,
                Email = request.Email,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Status = UserStatus.Active
            };

            _users.Add(user);
            return user;
        }

        public async Task<User> UpdateUserAsync(int userId, UpdateUserRequest request)
        {
            _logger.LogInformation("更新用户: {UserId}", userId);
            
            var user = _users.FirstOrDefault(u => u.Id == userId);
            if (user == null)
            {
                throw new ArgumentException($"用户不存在: {userId}");
            }

            await Task.Delay(80); // 模拟数据库更新延迟

            if (!string.IsNullOrEmpty(request.Name))
                user.Name = request.Name;
            
            if (!string.IsNullOrEmpty(request.Email))
            {
                if (_users.Any(u => u.Email == request.Email && u.Id != userId))
                {
                    throw new InvalidOperationException($"邮箱已存在: {request.Email}");
                }
                user.Email = request.Email;
            }

            if (request.Status.HasValue)
                user.Status = request.Status.Value;

            user.UpdatedAt = DateTime.UtcNow;
            return user;
        }

        public async Task<bool> DeleteUserAsync(int userId)
        {
            _logger.LogInformation("删除用户: {UserId}", userId);
            
            await Task.Delay(60); // 模拟数据库删除延迟
            
            var user = _users.FirstOrDefault(u => u.Id == userId);
            if (user == null)
            {
                return false;
            }

            _users.Remove(user);
            return true;
        }

        public async Task<UserListResponse> GetUsersAsync(GetUsersRequest request)
        {
            _logger.LogInformation("获取用户列表: 页码{PageIndex}, 页大小{PageSize}", 
                request.PageIndex, request.PageSize);
            
            await Task.Delay(120); // 模拟数据库查询延迟

            var query = _users.AsQueryable();

            // 应用过滤器
            if (!string.IsNullOrEmpty(request.NameFilter))
            {
                query = query.Where(u => u.Name.Contains(request.NameFilter));
            }

            if (request.StatusFilter.HasValue)
            {
                query = query.Where(u => u.Status == request.StatusFilter.Value);
            }

            var totalCount = query.Count();
            var users = query
                .Skip((request.PageIndex - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToList();

            return new UserListResponse
            {
                Users = users,
                TotalCount = totalCount,
                PageIndex = request.PageIndex,
                PageSize = request.PageSize
            };
        }

        private static void InitializeData()
        {
            if (_users.Count == 0)
            {
                _users.AddRange(new[]
                {
                    new User { Id = _nextId++, Name = "张三", Email = "zhangsan@example.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Status = UserStatus.Active },
                    new User { Id = _nextId++, Name = "李四", Email = "lisi@example.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Status = UserStatus.Active },
                    new User { Id = _nextId++, Name = "王五", Email = "wangwu@example.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Status = UserStatus.Inactive }
                });
            }
        }
    }
}
```

在 `MyRpcApp.Server/Program.cs` 中配置服务端：

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyRpcApp.Contracts;
using MyRpcApp.Server;
using PulseRPC.ServiceDiscovery.Extensions;
using PulseRPC.Monitoring.Extensions;
using PulseRPC.Tracing.Extensions;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((context, services) =>
{
    // 添加日志
    services.AddLogging(builder => 
        builder.AddConsole().SetMinimumLevel(LogLevel.Information));

    // 注册业务服务
    services.AddScoped<IUserService, UserService>();
    
    // 添加PulseRPC服务端
    services.AddPulseRpcServer(options =>
    {
        options.Host = "localhost";
        options.Port = 8080;
        options.MaxConnections = 1000;
    });
    
    // 添加服务注册 (使用内存注册中心进行演示)
    services.AddPulseRpcServiceRegistration(options =>
    {
        options.ServiceName = "UserService";
        options.ServiceVersion = "1.0.0";
        options.Host = "localhost";
        options.Port = 8080;
        options.DiscoveryType = ServiceDiscoveryType.InMemory; // 简单起见使用内存注册
    });
    
    // 添加性能监控
    services.AddPulseRpcMonitoring(options =>
    {
        options.Performance.Enabled = true;
        options.Performance.SamplingInterval = TimeSpan.FromSeconds(10);
    });
    
    // 添加链路追踪
    services.AddPulseRpcTracing(options =>
    {
        options.Enabled = true;
        options.ServiceName = "UserService";
        options.SamplingRate = 1.0; // 100%采样用于演示
        options.Exporter.Type = TracingExporterType.Console;
    });
});

var host = builder.Build();

Console.WriteLine("启动 PulseRPC 用户服务...");
Console.WriteLine("服务地址: http://localhost:8080");
Console.WriteLine("按 Ctrl+C 退出");

await host.RunAsync();
```

## 💻 第五步：实现客户端

在 `MyRpcApp.Client/Program.cs` 中实现客户端：

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyRpcApp.Contracts;
using PulseRPC.ServiceDiscovery.Extensions;
using PulseRPC.Monitoring.Extensions;
using PulseRPC.Tracing.Extensions;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((context, services) =>
{
    // 添加日志
    services.AddLogging(builder => 
        builder.AddConsole().SetMinimumLevel(LogLevel.Information));
    
    // 添加PulseRPC客户端
    services.AddPulseRpcClient();
    
    // 配置服务发现
    services.AddPulseRpcServiceDiscovery(options =>
    {
        options.DefaultDiscoveryType = ServiceDiscoveryType.InMemory;
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
        options.Timeout = TimeSpan.FromSeconds(30);
        options.RetryCount = 3;
    });
    
    // 添加监控
    services.AddPulseRpcMonitoring();
    
    // 添加链路追踪
    services.AddPulseRpcTracing(options =>
    {
        options.Enabled = true;
        options.ServiceName = "UserServiceClient";
        options.SamplingRate = 1.0;
        options.Exporter.Type = TracingExporterType.Console;
    });
});

var host = builder.Build();
var serviceProvider = host.Services;

// 演示客户端使用
await DemoUserServiceClient(serviceProvider);

async static Task DemoUserServiceClient(IServiceProvider serviceProvider)
{
    var userService = serviceProvider.GetRequiredService<IUserService>();
    var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
    
    Console.WriteLine("=== PulseRPC 用户服务客户端演示 ===\n");

    try
    {
        // 1. 获取用户列表
        Console.WriteLine("1. 获取用户列表");
        var userList = await userService.GetUsersAsync(new GetUsersRequest
        {
            PageIndex = 1,
            PageSize = 10
        });
        
        Console.WriteLine($"总用户数: {userList.TotalCount}");
        foreach (var user in userList.Users)
        {
            Console.WriteLine($"  ID: {user.Id}, 姓名: {user.Name}, 邮箱: {user.Email}, 状态: {user.Status}");
        }
        Console.WriteLine();

        // 2. 获取单个用户
        Console.WriteLine("2. 获取单个用户 (ID: 1)");
        var singleUser = await userService.GetUserAsync(1);
        Console.WriteLine($"用户信息: {singleUser.Name} ({singleUser.Email}) - {singleUser.Status}");
        Console.WriteLine();

        // 3. 创建新用户
        Console.WriteLine("3. 创建新用户");
        var newUser = await userService.CreateUserAsync(new CreateUserRequest
        {
            Name = "赵六",
            Email = "zhaoliu@example.com"
        });
        Console.WriteLine($"创建成功: ID={newUser.Id}, 姓名={newUser.Name}");
        Console.WriteLine();

        // 4. 更新用户
        Console.WriteLine("4. 更新用户信息");
        var updatedUser = await userService.UpdateUserAsync(newUser.Id, new UpdateUserRequest
        {
            Name = "赵六（已更新）",
            Status = UserStatus.Active
        });
        Console.WriteLine($"更新成功: 姓名={updatedUser.Name}, 状态={updatedUser.Status}");
        Console.WriteLine();

        // 5. 再次获取用户列表查看变化
        Console.WriteLine("5. 获取更新后的用户列表");
        var updatedList = await userService.GetUsersAsync(new GetUsersRequest
        {
            PageIndex = 1,
            PageSize = 10
        });
        Console.WriteLine($"更新后总用户数: {updatedList.TotalCount}");
        foreach (var user in updatedList.Users)
        {
            Console.WriteLine($"  ID: {user.Id}, 姓名: {user.Name}, 邮箱: {user.Email}, 状态: {user.Status}");
        }
        Console.WriteLine();

        // 6. 删除用户
        Console.WriteLine("6. 删除用户");
        var deleteResult = await userService.DeleteUserAsync(newUser.Id);
        Console.WriteLine($"删除结果: {(deleteResult ? "成功" : "失败")}");
        Console.WriteLine();

        // 7. 演示错误处理
        Console.WriteLine("7. 演示错误处理 - 尝试获取不存在的用户");
        try
        {
            await userService.GetUserAsync(999);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"预期的错误: {ex.Message}");
        }
        Console.WriteLine();

        // 8. 演示过滤查询
        Console.WriteLine("8. 演示过滤查询 - 查找活跃用户");
        var activeUsers = await userService.GetUsersAsync(new GetUsersRequest
        {
            PageIndex = 1,
            PageSize = 10,
            StatusFilter = UserStatus.Active
        });
        Console.WriteLine($"活跃用户数: {activeUsers.TotalCount}");
        foreach (var user in activeUsers.Users)
        {
            Console.WriteLine($"  活跃用户: {user.Name} ({user.Email})");
        }

        Console.WriteLine("\n=== 演示完成 ===");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "客户端演示过程中发生错误");
        Console.WriteLine($"错误: {ex.Message}");
    }
}
```

## ⚡ 第六步：运行示例

### 1. 启动服务端

```bash
cd MyRpcApp.Server
dotnet run
```

您应该看到类似的输出：
```
启动 PulseRPC 用户服务...
服务地址: http://localhost:8080
按 Ctrl+C 退出
info: MyRpcApp.Server.UserService[0]
      服务已注册到注册中心
```

### 2. 启动客户端

在新的终端窗口中：

```bash
cd MyRpcApp.Client
dotnet run
```

您将看到完整的客户端演示输出，包括用户的增删改查操作。

## 🔧 第七步：配置文件

### 服务端配置 (appsettings.json)

在 `MyRpcApp.Server` 项目中创建 `appsettings.json`：

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "PulseRPC": "Debug"
    }
  },
  "PulseRPC": {
    "Server": {
      "Host": "localhost",
      "Port": 8080,
      "MaxConnections": 1000,
      "KeepAliveInterval": "00:01:00"
    },
    "ServiceRegistration": {
      "ServiceName": "UserService",
      "ServiceVersion": "1.0.0",
      "DiscoveryType": "InMemory",
      "HealthCheck": {
        "Enabled": true,
        "Interval": "00:00:30",
        "Timeout": "00:00:05"
      }
    },
    "Monitoring": {
      "Performance": {
        "Enabled": true,
        "SamplingInterval": "00:00:10"
      },
      "Metrics": {
        "Enabled": true,
        "Categories": ["rpc", "system", "business"]
      }
    },
    "Tracing": {
      "Enabled": true,
      "ServiceName": "UserService",
      "SamplingRate": 1.0,
      "Exporter": {
        "Type": "Console"
      }
    }
  }
}
```

### 客户端配置 (appsettings.json)

在 `MyRpcApp.Client` 项目中创建 `appsettings.json`：

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "PulseRPC": "Debug"
    }
  },
  "PulseRPC": {
    "Client": {
      "DefaultTimeout": "00:00:30",
      "RetryCount": 3,
      "CircuitBreakerOptions": {
        "Enabled": true,
        "FailureThreshold": 5,
        "RecoveryTimeout": "00:01:00"
      }
    },
    "ServiceDiscovery": {
      "DefaultType": "InMemory",
      "RefreshInterval": "00:00:30"
    },
    "LoadBalancing": {
      "DefaultStrategy": "RoundRobin"
    },
    "Services": {
      "UserService": {
        "LoadBalancingStrategy": "RoundRobin",
        "Timeout": "00:00:30",
        "RetryCount": 3
      }
    },
    "Monitoring": {
      "Enabled": true
    },
    "Tracing": {
      "Enabled": true,
      "ServiceName": "UserServiceClient",
      "SamplingRate": 1.0,
      "Exporter": {
        "Type": "Console"
      }
    }
  }
}
```

## 🎯 第八步：高级配置

### 使用Consul服务发现

1. **启动Consul**:
```bash
docker run -d --name consul -p 8500:8500 consul:latest
```

2. **更新服务端配置**:
```json
{
  "PulseRPC": {
    "ServiceRegistration": {
      "DiscoveryType": "Consul",
      "ConsulOptions": {
        "Host": "localhost",
        "Port": 8500
      }
    }
  }
}
```

3. **更新客户端配置**:
```json
{
  "PulseRPC": {
    "ServiceDiscovery": {
      "DefaultType": "Consul",
      "ConsulOptions": {
        "Host": "localhost",
        "Port": 8500
      }
    }
  }
}
```

### 集成Jaeger链路追踪

1. **启动Jaeger**:
```bash
docker run -d --name jaeger \
  -p 16686:16686 \
  -p 14268:14268 \
  -p 6831:6831/udp \
  jaegertracing/all-in-one:latest
```

2. **更新追踪配置**:
```json
{
  "PulseRPC": {
    "Tracing": {
      "Exporter": {
        "Type": "Jaeger",
        "Jaeger": {
          "AgentHost": "localhost",
          "AgentPort": 6831
        }
      }
    }
  }
}
```

### 性能优化配置

```json
{
  "PulseRPC": {
    "Client": {
      "ConnectionPool": {
        "MaxConnections": 100,
        "MaxIdleTime": "00:05:00",
        "ConnectionTimeout": "00:00:30"
      },
      "Compression": {
        "Enabled": true,
        "Algorithm": "Gzip",
        "Level": "Optimal"
      }
    }
  }
}
```

## ✅ 验证部署

### 1. 健康检查

访问 `http://localhost:8080/health` 检查服务健康状态。

### 2. 监控指标

访问 `http://localhost:8080/metrics` 查看性能指标。

### 3. 链路追踪

如果使用Jaeger，访问 `http://localhost:16686` 查看追踪信息。

## 🎉 恭喜！

您已经成功创建并运行了第一个PulseRPC应用！接下来您可以：

- 查看[架构文档](Architecture.md)了解更多设计细节
- 阅读[配置参考](Configuration.md)了解所有配置选项
- 参考[最佳实践](BestPractices.md)优化您的应用
- 浏览示例项目获取更多灵感

## 📚 下一步

- [学习负载均衡策略](LoadBalancing.md)
- [配置服务发现](ServiceDiscovery.md)
- [设置监控系统](Monitoring.md)
- [实现链路追踪](Tracing.md)
- [性能调优指南](Performance.md) 