# PulseRPC.Client

PulseRPC.Client 是 PulseRPC 框架的高性能网络客户端库，提供连接管理、路由、连接级负载均衡与健康检查能力，面向分布式应用与 Unity 游戏客户端设计。

> 客户端 API 仍在演进中，部分接口可能发生变化。完整、可运行的用法请以仓库 [`samples/`](../../samples/)（尤其是 [ChatApp](../../samples/ChatApp/)）和 [`docs/`](../../docs/) 中的中文文档为准。

## 特性

- **连接管理**：多策略连接生命周期管理，支持连接池与自动清理
- **服务端发现边界**：客户端保留 `ServiceDiscoveryOptions` 配置模型；当前动态集群发现实现位于 `PulseRPC.Infrastructure.*`，服务端通过 `IDiscoveryProvider` / `IClusterMembership` 接入 Consul、Etcd、Kubernetes
- **负载均衡**：内置随机、轮询、最少连接、加权轮询、一致性哈希、故障转移等多种策略
- **连接池**：基于租约（lease）的连接池资源管理
- **动态路由**：基于规则的请求路由
- **多传输协议**：TCP 与 KCP，可插拔的传输架构
- **可靠性**：健康监测、自动故障转移与重试策略
- **源代码生成**：编译期生成服务代理，客户端调用避免使用反射
- **跨平台**：支持 .NET 10+ 与 Unity（`netstandard2.1`）

## 架构概览

### 三层抽象架构

PulseRPC.Client 采用三层抽象架构设计，实现清晰的职责分离和代码复用：

```
┌─────────────────────────────────────────────────────────────┐
│                 应用层 (Application Layer)                    │
│   Service Proxies  ·  Event Listeners  ·  Routing/Discovery   │
│   IClientChannel（客户端专用能力，如获取服务代理、健康检查）  │
├─────────────────────────────────────────────────────────────┤
│                 会话层 (Session Layer)                        │
│   ISessionChannel（认证上下文、属性字典等，与服务端共享抽象） │
├─────────────────────────────────────────────────────────────┤
│                 传输层 (Transport Layer)                      │
│   ITransportConnection（TCP / KCP 等具体连接的共享基础）      │
└─────────────────────────────────────────────────────────────┘
```

### 核心组件

- **`IPulseClient`**：客户端统一入口，负责初始化、连接与生命周期管理
- **`IConnectionManager`**：连接注册、查询、路由与生命周期管理（统一入口）
- **`ILoadBalancer`**：连接选择与负载均衡
- **`IClientChannel`**：面向业务的客户端连接抽象，提供服务代理获取等能力
- **集群发现层**：Consul / Kubernetes / Etcd 后端位于 `PulseRPC.Infrastructure.*`，服务端集群通过 `IDiscoveryProvider` 接入

### 服务调用调用链（概念）

```
应用代码
   ↓ 获取服务代理（Source Generator 生成）
IClientChannel（经负载均衡/路由选择）
   ↓ 代理方法构造 RpcRequest 并发送
会话层（序列化、请求-响应映射）
   ↓
传输层（TCP / KCP）
   ↓
网络
```

## 快速开始

以下示例基于 [`samples/ChatApp`](../../samples/ChatApp/)，演示如何创建客户端并建立连接：

```csharp
using Microsoft.Extensions.Logging;
using PulseRPC.Client;
using PulseRPC.Client.Configuration;

// 通过 Builder 配置并创建客户端
var client = new PulseClientBuilder()
    .AddConnection(ConnectionConfig.Tcp(name: "ChatServer", host: "127.0.0.1", port: 7000).ToDescriptor())
    .WithLogging(LoggerFactory.Create(b => b.AddConsole()))
    .Build();

// 初始化并建立连接
await client.InitializeAsync();

// 通过 Source Generator 生成的代理调用远程服务
// （代理及其扩展方法由 PulseRPC.Client.SourceGenerator 生成）

// 优雅关闭
await client.StopAsync();
```

`PulseClientBuilder` 同时提供若干便捷预设，可根据场景选择，例如低延迟游戏场景的 `UseGameClientPreset()` 与开发调试用的 `UseDevelopmentPreset()`。更多配置项（连接策略、传输参数、重试策略、服务发现、负载均衡等）请参阅下文文档链接与示例。

## 服务代理生成

PulseRPC.Client 使用编译期源代码生成为服务接口生成代理，客户端调用无需反射。将 Source Generator 作为分析器引用即可：

```xml
<ItemGroup>
  <ProjectReference Include="..\PulseRPC.Client.SourceGenerator\PulseRPC.Client.SourceGenerator.csproj"
                    ReferenceOutputAssembly="false"
                    OutputItemType="Analyzer" />
</ItemGroup>
```

生成的代理遵循 C# 9.0 及以下语法规范，以兼容 Unity。

## 平台支持

- **.NET 10.0+**：完整功能，可与 ASP.NET Core、Worker Service 等无缝集成
- **Unity 2022.3+ LTS**：通过 `netstandard2.1` 兼容，支持 IL2CPP 与移动平台

Unity 集成的详细步骤请参阅 [Unity Source Generator 集成指南](../../docs/使用指南/Unity%20Source%20Generator%20集成指南.md)。

## 依赖

- **PulseRPC.Abstractions**：核心接口与共享连接抽象
- **Microsoft.Extensions.Logging.Abstractions**：日志基础设施
- **System.Text.Json**：JSON 序列化
- **PulseRPC.Client.SourceGenerator**（开发期）：编译期代理生成

## 相关文档

完整文档位于仓库 [`docs/`](../../docs/) 目录：

- [PulseRPC 快速开始指南](../../docs/使用指南/PulseRPC%20快速开始指南.md)
- [PulseRPC 客户端和服务端使用指南](../../docs/使用指南/PulseRPC%20客户端和服务端使用指南.md)
- [PulseRPC 最佳实践指南](../../docs/使用指南/PulseRPC%20最佳实践指南.md)
- [IPulseHub 统一架构使用指南](../../docs/架构设计与分析/IPulseHub%20统一架构使用指南.md)

## 许可证

本项目是 PulseRPC 框架的一部分，许可证详见仓库根目录的 [LICENSE](../../LICENSE) 文件。

## 贡献

贡献指南与开发环境配置请参阅主项目 [README](../../README.md)。
