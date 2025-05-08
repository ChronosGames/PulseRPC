# 快速开始

## 环境要求

- .NET 8 SDK
- Visual Studio 2022 或 JetBrains Rider
- Unity 2021.3 或更高版本（如果使用 Unity）

## 安装

### 通过 NuGet 安装

```bash
# 安装服务端包
dotnet add package PulseRPC.Server

# 安装客户端包
dotnet add package PulseRPC.Client
```

### Unity 项目安装

1. 在 Unity 包管理器中添加以下包：
   ```
   com.pulserpc.client
   ```

2. 或者直接在 `Packages/manifest.json` 中添加：
   ```json
   {
     "dependencies": {
       "com.pulserpc.client": "1.0.0"
     }
   }
   ```

## 基本用法

### 1. 定义消息

```csharp
using PulseRPC.Protocol.Attributes;

[Message(1)]
public class HelloRequest : IMessage
{
    public string Name { get; set; }
}

[Message(2)]
public class HelloResponse : IMessage
{
    public string Greeting { get; set; }
}
```

### 2. 实现服务端

```csharp
using PulseRPC.Server;

public class GreetingService
{
    [RpcHandler]
    public async Task<HelloResponse> HandleHello(HelloRequest request)
    {
        return new HelloResponse
        {
            Greeting = $"Hello, {request.Name}!"
        };
    }
}

// 启动服务器
var server = new PulseServer();
server.RegisterService<GreetingService>();
await server.StartAsync("127.0.0.1", 5000);
```

### 3. 实现客户端

```csharp
using PulseRPC.Client;

// 创建客户端
var client = new PulseClient();
await client.ConnectAsync("127.0.0.1", 5000);

// 发送请求
var response = await client.SendAsync<HelloResponse>(new HelloRequest
{
    Name = "World"
});

Console.WriteLine(response.Greeting); // 输出: Hello, World!
```

## 高级特性

- 消息压缩
- 消息批处理
- 消息分片
- 优先级队列
- 性能监控

详细信息请参考[高级特性](../features/advanced.md)文档。

## 下一步

- 了解[基本概念](./concepts.md)
- 查看[示例项目](../samples/basic.md)
- 阅读[API 文档](../api/README.md)
- 探索[最佳实践](../best-practices/performance.md)
