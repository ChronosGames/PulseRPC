# PulseRPC

[![NuGet](https://img.shields.io/nuget/v/PulseRPC.Client.svg)](https://www.nuget.org/packages/PulseRPC.Client/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

基于现代 .NET 平台的高性能 RPC 框架，支持 TCP 和 KCP 传输协议，面向 Unity 游戏和微服务架构设计。

> 项目仍在积极开发中，部分接口可能发生变化。首次上手只参考经过 CI 端到端验证的三项目 [`samples/HelloRPC`](samples/HelloRPC/) 黄金路径。

## 🚀 核心特性

- **多传输协议**：TCP（可靠）与 KCP（低延迟）传输实现
- **集群发现**：`IDiscoveryProvider` / `IClusterMembership` 抽象，提供静态成员以及 Consul、Etcd、Kubernetes 后端
- **客户端负载均衡**：支持随机、轮询、最少连接、平滑加权轮询与一致性哈希；动态权重通过 `IConnectionWeightProvider` 提供，一致性哈希调用必须提供稳定的 `ServiceProxyOptions.StickyKey`，非法输入会明确失败
- **健康检查与故障转移**：客户端连接健康检查与服务端 `IPulseServiceHealthCheck` 支持
- **连接管理**：连接生命周期管理，支持自动重连
- **有界消息执行**：服务端使用固定数量的 worker shard；连接在其生命周期内绑定一个 shard，每个 shard 使用有界队列，队列满时立即拒绝新消息；有效调优入口为 `PulseServerOptions.MessageWorkerShardCount` 与 `MessageQueueCapacityPerShard`
- **代码生成**：基于 Source Generator 的客户端/服务端代理，客户端避免使用反射（兼容 Unity）
- **多节点 Actor / Gateway**：内置节点 TCP 数据面、wire v2 claims 与 lease fencing、Redis CAS + TTL 租约及严格授权路由（部署边界见专项指南）
- **高性能序列化**：优先使用 MemoryPack
- **可观测性基础**：服务端通过 `EngineStatistics` 与 `RuntimeQueueMetrics` 暴露消息处理和有界队列事实；当前未发布独立 `PulseRPC.Monitoring` / `PulseRPC.Tracing` 包

## 📦 项目结构

```
PulseRPC/
├── src/                          # 核心源代码
│   ├── PulseRPC.Abstractions/    # 抽象接口和基础类型
│   ├── PulseRPC.Client/          # 客户端实现
│   ├── PulseRPC.Server/          # 服务端实现
│   ├── PulseRPC.Client.Unity/    # Unity 客户端支持
│   ├── PulseRPC.Shared/          # 共享组件（压缩、网络缓冲池等）
│   ├── PulseRPC.Infrastructure/  # 核心基础设施实现
│   └── PulseRPC.Infrastructure.*/# 特定基础设施实现（Consul、Etcd、K8s 等）
├── perf/                         # 性能测试和基准测试
│   ├── BenchmarkApp/             # 系统级性能基准测试框架
│   ├── Microbenchmark/           # 方法级微基准测试
│   └── SourceGeneratorPerf/      # 源生成器性能测试
├── samples/                      # 示例应用（见 samples/README.md）
├── tests/                        # 单元测试和集成测试
└── docs/                         # 中文项目文档
```

## 🛠️ 环境要求

- **.NET 10 SDK**（版本以 [`global.json`](global.json) 为准）
- **Visual Studio 2022** 或 **JetBrains Rider**（推荐）
- **Unity 2022.3.12f1+ LTS**（Roslyn 4.3，用于 Unity 集成；客户端兼容 `netstandard2.1`）

## ⚡ 快速开始

### 构建和测试

```bash
# 恢复依赖（使用集中化包管理）
dotnet restore

# 构建整个解决方案
dotnet build

# 运行所有测试
dotnet test

# 构建发布版本
dotnet build -c Release
```

### 唯一黄金路径：HelloRPC

HelloRPC 的 Contracts、Server、Client 三个项目是 README、Quickstart 与 NuGet 包说明共用的唯一首次上手路径：

```bash
dotnet build samples/HelloRPC/HelloRPC.sln

# 终端 1
dotnet run --project samples/HelloRPC/HelloRPC.Server

# 终端 2
dotnet run --project samples/HelloRPC/HelloRPC.Client
```

成功时客户端输出 `Hello, PulseRPC!`。完整源码和说明见 [`samples/HelloRPC`](samples/HelloRPC/)；逐步说明见[快速开始](docs/getting-started/quickstart.md)。

## 📊 性能基准测试

[`perf/BenchmarkApp`](perf/BenchmarkApp/) 提供端到端的性能基准测试框架，支持延迟、吞吐量、流式传输等场景，并可导出 HTML/JSON/CSV 报告。使用方法请参阅 [`perf/BenchmarkApp/README.md`](perf/BenchmarkApp/README.md)。

## 📖 文档

所有面向用户的文档均位于 [`docs/`](docs/) 目录，入口见 [文档索引](docs/index.md)：

- [快速开始](docs/getting-started/quickstart.md)
- [客户端和服务端使用指南](docs/guides/client-server.md)
- [Unity 客户端教程](docs/getting-started/unity-client-tutorial.md)
- [架构总览](docs/concepts/architecture.md)
- [Actor 模型](docs/concepts/actor-model.md)
- [集群与路由](docs/concepts/clustering-and-routing.md)
- [经 Gateway 调用 Actor](docs/guides/gateway-actors.md)
- [参考手册](docs/reference/index.md)
- [示例项目](docs/samples.md)
- [变更日志](docs/changelog.md)

历史设计、阶段总结和旧路线图已归档到 [`docs/archive/`](docs/archive/)。

## 🧪 示例项目

首次上手只使用 [HelloRPC](samples/HelloRPC/)；专项功能和历史参考的完整清单见 [`samples/README.md`](samples/README.md)，它们不作为 Quickstart 的替代入口。

## 🔧 开发约定

- 启用 nullable reference types（Nullable 警告视为错误）
- 使用 `PublicAPI.Shipped.txt` 和 `PublicAPI.Unshipped.txt` 进行 API 兼容性管理
- 遵循异步编程模式，使用 `CancellationToken`
- 客户端实现避免使用反射，通过 Source Generator 进行代码生成
- 集中化包管理：[`Directory.Packages.props`](Directory.Packages.props)、[`Directory.Build.props`](Directory.Build.props)、[`global.json`](global.json)

## 🤝 贡献指南

欢迎社区贡献！

1. **Fork** 本仓库
2. 创建特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 开启 **Pull Request**

## 📄 许可证

本项目采用 [MIT 许可证](LICENSE)。

## 🏆 致谢

感谢所有贡献者和社区成员的支持！

特别感谢：
- **MemoryPack** 提供高性能序列化
- **Microsoft** 提供 .NET 平台
- **Unity Technologies** 提供游戏引擎支持

---

⭐ **如果这个项目对你有帮助，请给我们一个星标！**
