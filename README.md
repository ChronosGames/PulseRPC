# PulseRPC

PulseRPC 是一个基于 TCP 的现代 RPC 框架，专为 .NET 和 Unity 平台设计。它提供了高性能、易用性和可扩展性的完美平衡。

## 特性

- **高性能**：优化的消息序列化、压缩和传输机制
- **易用性**：自动代码生成，简化开发流程
- **可扩展**：灵活的插件系统和中间件支持
- **Unity支持**：专门的Unity客户端支持
- **开发工具**：完整的开发和调试工具链

## 快速开始

### 环境要求

- .NET 8 SDK
- Visual Studio 2022 或 JetBrains Rider
- Unity 2021.3 或更高版本（如果使用 Unity）

### 安装

通过 NuGet 安装：

```bash
# 安装服务端包
dotnet add package PulseRPC.Server

# 安装客户端包
dotnet add package PulseRPC.Client
```

Unity项目安装请参考[Unity集成指南](docs/samples/unity-integration.md)。

### 基本用法

```csharp
// 1. 定义消息
[Message(1)]
public class HelloRequest : IMessage
{
    public string Name { get; set; }
}

// 2. 实现服务
public class GreetingService
{
    [RpcHandler]
    public async Task<HelloResponse> HandleHello(HelloRequest request)
    {
        return new HelloResponse { Greeting = $"Hello, {request.Name}!" };
    }
}

// 3. 启动服务器
var server = new PulseServer();
server.RegisterService<GreetingService>();
await server.StartAsync("127.0.0.1", 5000);

// 4. 客户端调用
var client = new PulseClient();
await client.ConnectAsync("127.0.0.1", 5000);
var response = await client.SendAsync<HelloResponse>(new HelloRequest { Name = "World" });
```

## 项目结构

```
src/
├── PulseRPC.Core/                 # 核心库
├── PulseRPC.Client/              # 客户端库
├── PulseRPC.Server/              # 服务端库
├── PulseRPC.Client.Unity/        # Unity客户端
└── PulseRPC.Generators/          # 代码生成器
```

## 文档

### 入门指南
- [快速开始](docs/guide/getting-started.md)
- [基本概念](docs/guide/concepts.md)
- [安装配置](docs/guide/installation.md)

### 核心功能
- [消息系统](docs/features/messaging.md)
- [序列化](docs/features/serialization.md)
- [网络传输](docs/features/networking.md)
- [代码生成](docs/features/code-generation.md)

### 最佳实践
- [性能优化指南](docs/best-practices/performance.md)
- [错误处理](docs/best-practices/error-handling.md)
- [安全性考虑](docs/best-practices/security.md)

### 开发指南
- [架构设计](docs/development/architecture.md)
- [贡献指南](docs/development/contributing.md)
- [代码规范](docs/development/coding-standards.md)

### 示例项目
- [基础示例](docs/samples/basic.md)
- [高级特性](docs/samples/advanced.md)
- [Unity 集成](docs/samples/unity-integration.md)

## 版本信息

- [更新日志](docs/CHANGELOG.md)
- [版本规划](docs/ROADMAP.md)
- [常见问题](docs/faq.md)
- [故障排除](docs/troubleshooting.md)

## 贡献

欢迎贡献代码、报告问题或提出建议。详情请参考[贡献指南](docs/development/contributing.md)。

## 许可证

本项目采用 MIT 许可证 - 详见 [LICENSE](LICENSE) 文件
