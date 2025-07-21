s# ChatApp 示例 - 使用简化的PulseRPC API

这个示例展示了如何使用新的简化PulseRPC API来构建一个高性能的聊天游戏应用。

## 更新内容

### 服务器端改进

现在服务器配置更加简洁，只需要一个方法调用即可完成所有配置：

```csharp
// 使用新的简化配置API
services.AddPulseRpcServer(server =>
{
    server
        .ConfigureServer(options =>
        {
            options.ServiceName = "ChatGameServer";
            options.ServiceVersion = "2.0.0";
            options.MaxConnections = 1000;
            options.HeartbeatInterval = TimeSpan.FromSeconds(30);
        })
        .AddTcp("TcpChannel", 7000, options =>
        {
            options.NoDelay = true;
        }, isDefault: true)
        .AddKcp("KcpChannel", 7001, options =>
        {
            options.KcpOptions = new()
            {
                NoDelay = 1,        // 无延迟模式
                Interval = 10,      // 10ms更新间隔
                Resend = 2,         // 快重传
                NoCongestion = 1    // 关闭拥塞控制
            };
        });
});

// 注册业务服务 - 注意使用Singleton生命周期
services.AddPulseRpcService<IPlayerHub, PlayerHub>(ServiceLifetime.Singleton);
```

### 启动服务器

```csharp
// 获取服务器实例并启动
var server = host.Services.GetRequiredService<IPulseRpcServer>();
await server.StartAsync();

// 停止服务器
await server.StopAsync();
```

### 客户端改进

客户端代码已经优化，具有更好的注释和配置：

#### 控制台客户端
- 简化了KCP配置，添加了详细注释说明每个参数的用途
- 优化了错误处理和日志输出

#### Unity客户端  
- 更清晰的传输通道用途说明
- TCP用于可靠消息传输（聊天、登录等）
- KCP用于低延迟游戏数据传输（移动、状态更新等）

## 主要特性

### 双通道设计
- **TCP通道**：用于可靠消息传输，如聊天消息、登录认证
- **KCP通道**：用于低延迟游戏数据，如角色移动、实时状态

### 性能优化
- KCP配置针对游戏场景优化：无延迟模式、快重传、关闭拥塞控制
- 批量处理玩家移动数据，减少网络开销
- 自动重连和故障恢复机制

### 简化的API
- 服务器配置统一在`AddPulseRpcServer`中完成
- 不再需要手动创建和配置ServerManager
- 自动注册传输通道和业务服务

### 重要注意事项
⚠️ **服务生命周期**：PulseRPC服务必须使用`Singleton`生命周期，因为服务器启动时需要从根容器解析服务。

```csharp
// ✅ 正确 - 使用Singleton生命周期
services.AddPulseRpcService<IPlayerHub, PlayerHub>(ServiceLifetime.Singleton);

// ❌ 错误 - 默认Scoped会导致启动失败
services.AddPulseRpcService<IPlayerHub, PlayerHub>(); // 会抛出异常
```

## 快速开始

### 启动服务器
```bash
cd samples/ChatApp/ChatApp.Server
dotnet run
```

### 启动控制台客户端
```bash
cd samples/ChatApp/ChatApp.Console  
dotnet run
```

### Unity客户端
1. 打开Unity项目：`samples/ChatApp/ChatApp.Unity`
2. 运行ChatScene场景
3. 点击"连接服务器"按钮

## 代码结构

### 服务器端
- `Program.cs` - 使用新的简化API配置服务器
- `PlayerHub.cs` - 游戏业务逻辑实现
- `PlayerMovementBatcher.cs` - 移动数据批量处理器

### 客户端
- `GameConsoleClient.cs` - 控制台客户端实现
- `ChatComponent.cs` - Unity客户端组件
- 双通道配置：TCP（可靠）+ KCP（低延迟）

这个示例展示了PulseRPC新API的强大功能和易用性，适合构建高性能的实时多人游戏和聊天应用。