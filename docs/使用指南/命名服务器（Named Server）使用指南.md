# 命名服务器（Named Server）使用指南

## 概述

`INamedPulseServer` 是 PulseRPC 的命名服务器功能，支持在同一进程中运行多个独立的服务器实例。这对于需要运行多个服务器（例如：对外服务器和对内服务器）的场景非常有用。

## 功能特性

- ✅ 支持在同一进程中运行多个独立的服务器实例
- ✅ 每个服务器拥有独立的配置和传输层
- ✅ 使用 .NET Keyed Services 实现完全隔离
- ✅ 符合 .NET 最佳实践

## 使用方法

### 1. 基本用法（使用委托配置）

```csharp
using Microsoft.Extensions.DependencyInjection;
using PulseRPC.Server.Extensions;
using PulseRPC.Shared;

var services = new ServiceCollection();

// 注册对外服务器（TCP 8080）
services.AddNamedPulseServer("ExternalServer", options =>
{
    options.Transports.Add(new TransportChannelConfiguration
    {
        Name = "ExternalTcp",
        Type = TransportType.Tcp,
        Port = 8080,
        IsDefault = true
    });
});

// 注册对内服务器（TCP 9080）
services.AddNamedPulseServer("InternalServer", options =>
{
    options.Transports.Add(new TransportChannelConfiguration
    {
        Name = "InternalTcp",
        Type = TransportType.Tcp,
        Port = 9080,
        IsDefault = true
    });
});

var serviceProvider = services.BuildServiceProvider();

// 获取并启动服务器
var externalServer = serviceProvider.GetRequiredKeyedService<INamedPulseServer>("ExternalServer");
var internalServer = serviceProvider.GetRequiredKeyedService<INamedPulseServer>("InternalServer");

await externalServer.StartAsync();
await internalServer.StartAsync();

Console.WriteLine($"External Server: {externalServer.ServerName}");
Console.WriteLine($"Internal Server: {internalServer.ServerName}");
```

### 2. 使用 IConfiguration 配置

#### appsettings.json

```json
{
  "ExternalServer": {
    "Transports": [
      {
        "Name": "ExternalTcp",
        "Type": "Tcp",
        "Port": 8080,
        "IsDefault": true
      },
      {
        "Name": "ExternalKcp",
        "Type": "Kcp",
        "Port": 8081,
        "IsDefault": false
      }
    ]
  },
  "InternalServer": {
    "Transports": [
      {
        "Name": "InternalTcp",
        "Type": "Tcp",
        "Port": 9080,
        "IsDefault": true
      }
    ]
  }
}
```

#### Program.cs

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PulseRPC.Server.Extensions;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var services = new ServiceCollection();

// 使用配置注册服务器
services.AddNamedPulseServer(
    "ExternalServer",
    configuration.GetSection("ExternalServer"));

services.AddNamedPulseServer(
    "InternalServer",
    configuration.GetSection("InternalServer"));

var serviceProvider = services.BuildServiceProvider();
```

### 3. 在 ASP.NET Core 中使用

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using PulseRPC.Server.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 注册对外服务器
builder.Services.AddNamedPulseServer(
    "ExternalServer",
    builder.Configuration.GetSection("ExternalServer"));

// 注册对内服务器
builder.Services.AddNamedPulseServer(
    "InternalServer",
    builder.Configuration.GetSection("InternalServer"));

var app = builder.Build();

// 手动启动服务器（如需要）
var externalServer = app.Services.GetRequiredKeyedService<INamedPulseServer>("ExternalServer");
var internalServer = app.Services.GetRequiredKeyedService<INamedPulseServer>("InternalServer");

await Task.WhenAll(
    externalServer.StartAsync(),
    internalServer.StartAsync()
);

await app.RunAsync();
```

### 4. 完整的 DistributedGameApp 示例

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Extensions;
using PulseRPC.Shared;

var services = new ServiceCollection();

// 添加日志
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

// 注册对外服务器（处理客户端连接）
services.AddNamedPulseServer("ExternalServer", options =>
{
    // TCP 传输
    options.Transports.Add(new TransportChannelConfiguration
    {
        Name = "ExternalTcp",
        Type = TransportType.Tcp,
        Port = 8080,
        IsDefault = true
    });

    // KCP 传输
    options.Transports.Add(new TransportChannelConfiguration
    {
        Name = "ExternalKcp",
        Type = TransportType.Kcp,
        Port = 8081,
        IsDefault = false
    });

    // 配置其他选项
    options.DefaultOperationTimeout = TimeSpan.FromSeconds(30);
    options.MaxConcurrentOperations = 10000;
});

// 注册对内服务器（处理节点间通信）
services.AddNamedPulseServer("InternalServer", options =>
{
    options.Transports.Add(new TransportChannelConfiguration
    {
        Name = "InternalTcp",
        Type = TransportType.Tcp,
        Port = 9080,
        IsDefault = true
    });

    options.DefaultOperationTimeout = TimeSpan.FromSeconds(10);
    options.MaxConcurrentOperations = 1000;
});

var serviceProvider = services.BuildServiceProvider();

// 获取并启动服务器
var externalServer = serviceProvider.GetRequiredKeyedService<INamedPulseServer>("ExternalServer");
var internalServer = serviceProvider.GetRequiredKeyedService<INamedPulseServer>("InternalServer");

Console.WriteLine("Starting servers...");

await Task.WhenAll(
    externalServer.StartAsync(),
    internalServer.StartAsync()
);

Console.WriteLine($"External Server ({externalServer.ServerName}): Running");
Console.WriteLine($"  - Transports: {string.Join(", ", externalServer.GetTransports().Keys)}");
Console.WriteLine($"Internal Server ({internalServer.ServerName}): Running");
Console.WriteLine($"  - Transports: {string.Join(", ", internalServer.GetTransports().Keys)}");

// 等待用户输入
Console.WriteLine("Press any key to stop servers...");
Console.ReadKey();

// 停止服务器
await Task.WhenAll(
    externalServer.StopAsync(),
    internalServer.StopAsync()
);

Console.WriteLine("Servers stopped.");
```

## API 参考

### INamedPulseServer 接口

继承自 `IPulseServer`，添加了服务器名称属性：

```csharp
public interface INamedPulseServer : IPulseServer
{
    /// <summary>
    /// 服务器名称（唯一标识）
    /// </summary>
    string ServerName { get; }
}
```

### 扩展方法

#### AddNamedPulseServer (使用委托配置)

```csharp
public static IServiceCollection AddNamedPulseServer(
    this IServiceCollection services,
    string serverName,
    Action<UnifiedServerOptions> configureOptions)
```

#### AddNamedPulseServer (使用 IConfiguration)

```csharp
public static IServiceCollection AddNamedPulseServer(
    this IServiceCollection services,
    string serverName,
    IConfiguration configuration)
```

## 注意事项

1. **服务器名称唯一性**：每个服务器的名称必须唯一
2. **端口不冲突**：确保不同服务器的端口不冲突
3. **.NET 版本要求**：需要 .NET 8.0 或更高版本（Keyed Services 支持）
4. **独立依赖**：每个命名服务器拥有独立的 `MessageEngine`、`ChannelManager` 等组件
5. **共享依赖**：`TransportIntegrationManager` 和序列化器等核心依赖在所有服务器间共享

## 与传统方式的对比

### 传统方式（单服务器）

```csharp
// ❌ 只能注册一个服务器
services.AddUnifiedPulseServer(options => { ... });

// ❌ 无法再注册第二个服务器
```

### 命名服务器方式（多实例）

```csharp
// ✅ 可以注册多个独立的服务器
services.AddNamedPulseServer("Server1", options => { ... });
services.AddNamedPulseServer("Server2", options => { ... });
services.AddNamedPulseServer("Server3", options => { ... });
```

## 故障排除

### 问题：服务器启动失败

**可能原因**：端口已被占用

**解决方案**：检查端口是否已被其他进程使用，或更改配置中的端口号

### 问题：找不到 INamedPulseServer

**可能原因**：未添加正确的 using 指令

**解决方案**：添加 `using PulseRPC.Server;` 和 `using PulseRPC.Server.Extensions;`

### 问题：GetRequiredKeyedService 不可用

**可能原因**：.NET 版本过低

**解决方案**：升级到 .NET 8.0 或更高版本

## 更多资源

- [PulseRPC 文档](../README.md)
- [配置选项详解](./UnifiedServerOptions.md)
- [传输层配置](./Transport-Configuration.md)
- [DistributedGameApp 示例](../samples/DistributedGameApp/)
