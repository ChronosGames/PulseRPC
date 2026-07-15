# PulseRPC.Client

PulseRPC.Client 是 PulseRPC 框架的高性能网络客户端库，提供连接管理、路由、连接级负载均衡与健康检查能力，面向分布式应用与 Unity 游戏客户端设计。

> 客户端 API 仍在演进中。首次上手只参考经过 CI 端到端验证的三项目 [HelloRPC](../../samples/HelloRPC/) 黄金路径。

## 特性

- **连接管理**：显式连接注册、查询、健康检查与断开生命周期
- **服务发现边界**：客户端当前使用显式连接，服务端集群发现位于 `PulseRPC.Infrastructure.*`
- **负载均衡**：支持随机、轮询、最少连接、平滑加权轮询与一致性哈希；动态权重和 sticky key 缺失等非法输入会明确失败
- **上下文路由**：`ServiceProxyOptions.LoadBalancingHint` 与 `StickyKey` 会进入连接选择
- **多传输协议**：TCP 与 KCP，可插拔的传输架构
- **可靠性边界**：提供连接健康检查；重试和池化应在业务层显式建模，不再保留未接线的 Builder 配置入口
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

完整客户端接线位于 [`HelloRPC.Client`](../../samples/HelloRPC/HelloRPC.Client/)；它建立真实 TCP 连接，通过生成的 `IHelloHub` 代理发起请求，并校验服务端返回值。运行方式见 [快速开始](../../docs/getting-started/quickstart.md)。

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

Unity 集成的详细步骤请参阅 [Unity Source Generator 集成指南](../../docs/getting-started/unity-client-tutorial.md)。

## 依赖

- **PulseRPC.Abstractions**：核心接口与共享连接抽象
- **Microsoft.Extensions.Logging.Abstractions**：日志基础设施
- **System.Text.Json**：JSON 序列化
- **PulseRPC.Client.SourceGenerator**（开发期）：编译期代理生成

## 相关文档

完整文档位于仓库 [`docs/`](../../docs/) 目录：

- [PulseRPC 快速开始指南](../../docs/getting-started/quickstart.md)
- [PulseRPC 客户端和服务端使用指南](../../docs/guides/client-server.md)
- [PulseRPC 最佳实践指南](../../docs/guides/best-practices.md)
- [IPulseHub 统一架构使用指南](../../docs/concepts/rpc-model.md)

## 许可证

本项目是 PulseRPC 框架的一部分，许可证详见仓库根目录的 [LICENSE](../../LICENSE) 文件。

## 贡献

贡献指南与开发环境配置请参阅主项目 [README](../../README.md)。
