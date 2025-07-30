# PulseRPC 客户端API使用指南

PulseRPC现在提供两套客户端API，满足不同场景的需求：

## 1. DI版本 - 适用于服务端和控制台应用

### 特点
- 完整的依赖注入支持
- 与.NET生态系统深度集成
- 支持配置文件和选项模式
- 自动生命周期管理

### 使用方法

```csharp
// Program.cs
var host = Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        // 方式1: 简单配置
        services.AddPulseRpcTcpClient("localhost", 8000);
        
        // 方式2: 复杂配置
        services.AddPulseRpcClient(client =>
        {
            client.ConfigureClient(options =>
            {
                options.ClientName = "MyClient";
                options.ConnectTimeout = TimeSpan.FromSeconds(10);
            })
            .AddTcp("reliable", "localhost", 7000, isDefault: true)
            .AddKcp("gaming", "localhost", 7001);
        });
    })
    .Build();

// 使用客户端
var client = host.Services.GetRequiredService<IPulseClient>();
await client.ConnectAsync();
var service = client.GetService<IMyService>();
```

## 2. 非DI版本 - 适用于Unity和其他场景

### 特点  
- 无需依赖注入容器
- Unity友好
- 工厂模式创建
- 流式配置API

### 使用方法

```csharp
// 方式1: 简单创建
using var client = PulseRpcClientFactory.CreateTcpClient("localhost", 8000);
await client.ConnectAsync();

// 方式2: 复杂配置
using var client = PulseRpcClientFactory.CreateBuilder()
    .WithLogger(loggerFactory)
    .WithOptions(options =>
    {
        options.ClientName = "UnityClient";
        options.EnableAutoReconnect = true;
    })
    .AddTcp("reliable", "localhost", 7000, isDefault: true)
    .AddKcp("gaming", "localhost", 7001)
    .Build();

await client.ConnectAsync();
var service = client.GetService<IMyService>();
```

## Unity集成示例

```csharp
public class GameClient : MonoBehaviour
{
    private IPulseClient? _client;
    
    private async void Start()
    {
        // 创建客户端
        _client = PulseRpcClientFactory.CreateClient(builder =>
        {
            builder.AddTcp("main", "game-server.com", 8000, isDefault: true)
                   .AddKcp("realtime", "game-server.com", 8001);
        });
        
        await _client.ConnectAsync();
        
        // 获取游戏服务
        var gameService = _client.GetService<IGameService>();
    }
    
    private async void OnDestroy()
    {
        await _client?.DisconnectAsync();
        _client?.Dispose();
    }
}
```

## 双通道架构

两种API都支持多传输通道配置：

- **TCP通道**: 用于可靠性要求高的操作（登录、聊天、交易等）
- **KCP通道**: 用于低延迟要求高的操作（位置同步、战斗数据等）
- **WebSocket通道**: 用于Web客户端连接

```csharp
// 配置示例
.AddTcp("reliable", host, tcpPort, options =>
{
    options.NoDelay = true;
}, isDefault: false)
.AddKcp("gaming", host, kcpPort, options =>
{
    options.Kcp = new KcpOptions
    {
        NoDelay = 1,
        Interval = 10,
        Resend = 2,
        DisableFlowControl = true
    };
}, isDefault: true)
```

## 最佳实践

1. **服务端应用**: 使用DI版本，享受完整的.NET生态支持
2. **Unity客户端**: 使用非DI版本，避免依赖注入复杂性
3. **控制台工具**: 两种版本都可以，根据项目架构选择
4. **混合架构**: 可在同一项目中使用两种API，适应不同模块需求

## 示例项目

- `ChatApp.Client.Console`: 控制台客户端，演示DI和非DI两种用法
- `ChatApp.Unity`: Unity客户端，演示游戏环境下的使用
- `MinimalClient`: 最简单的客户端示例