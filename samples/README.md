# PulseRPC 示例项目

本目录包含 PulseRPC 的黄金路径、专项示例和历史参考。

> 首次上手只有一个入口：三项目 [HelloRPC](HelloRPC/)。README、Quickstart、NuGet README 和 CI smoke 都引用这条路径。其他维护样例只解释专项能力，不作为新项目模板。

## 黄金路径

| 示例 | 说明 |
|------|------|
| [HelloRPC](HelloRPC/) | Contracts / Server / Client 三项目，执行一次真实 TCP RPC；由 CI 端到端运行 |

## 专项示例

| 示例 | 说明 |
|------|------|
| [ChatApp](ChatApp/) | 基于服务隔离架构的实时聊天/游戏示例，包含控制台与 Unity 客户端（见 [README](ChatApp/README.md)） |
| [GameApp](GameApp/) | 完整的游戏服务器示例（见 [README](GameApp/README.md)） |
| [DistributedGameApp](DistributedGameApp/) | 分布式游戏服务器示例（见 [README](DistributedGameApp/README.md)） |
| [JwtAuthentication](JwtAuthentication/) | JWT 身份验证集成示例（客户端 / 服务端 / 共享契约） |
| [HubFactoryExample](HubFactoryExample/) | 历史项目名；代码已迁移为无状态 Hub + `IServiceAccessor` |
| [ServiceFactoryExample](ServiceFactoryExample/) | 当前 `AddPulseService` / `IServiceAccessor` 服务实例管理示例 |
| [JsonTranscoding](JsonTranscoding/) | 同一 Hub 实现同时提供 PulseRPC 和显式 ASP.NET JSON 网关（非自动转码） |

历史探索示例：

| 示例 | 当前状态 |
|------|----------|
| [BasicUsage](BasicUsage/) | 使用旧 `PulseRPC.ServiceDiscovery` / `PulseRPC.LoadBalancing` 命名空间，待迁移 |
| [ServiceRegistrationExample](ServiceRegistrationExample/) | 引用已不存在的旧服务发现项目，待迁移 |
| [MonitoringExample](MonitoringExample/) | 引用旧 `PulseRPC.Monitoring` 项目，当前仓库未发布该独立包 |
| [TracingExample](TracingExample/) | 引用旧 `PulseRPC.Tracing` 项目，当前仓库未发布该独立包 |
| [DnsExample](DnsExample/) | 引用旧 DNS 服务发现实现，当前 `src/` 未包含对应项目 |
| [EtcdExample](EtcdExample/) | 引用旧 `PulseRPC.ServiceDiscovery` / `PulseRPC.LoadBalancing` 包名；当前 Etcd 后端在 `PulseRPC.Infrastructure.Etcd` 中 |

## 🚀 运行示例

```bash
# 构建黄金路径（仓库根目录）
dotnet build samples/HelloRPC/HelloRPC.sln

# 分别在两个终端运行
dotnet run --project samples/HelloRPC/HelloRPC.Server
dotnet run --project samples/HelloRPC/HelloRPC.Client
```

依赖外部组件的示例可使用 Docker 启动对应服务后再运行，具体命令请参阅示例目录内的说明或源码注释。

## 🎯 使用场景

PulseRPC 适用于以下场景：

- **微服务架构** - 连接级负载均衡、健康检查与故障转移
- **分布式系统** - 静态成员或 Consul/Etcd/Kubernetes 驱动的集群成员发现
- **实时游戏** - KCP 低延迟传输、服务隔离与高并发消息处理

## 📖 更多文档

完整的中文文档位于仓库的 [`docs/`](../docs/) 目录，推荐先阅读：

- [PulseRPC 快速开始指南](../docs/getting-started/quickstart.md)
- [PulseRPC 客户端和服务端使用指南](../docs/guides/client-server.md)
- [示例项目说明](../docs/samples.md)

## 🤝 贡献指南

欢迎提交新的示例和改进现有示例：

1. Fork 项目仓库
2. 创建功能分支
3. 添加示例代码和文档
4. 提交 Pull Request

## 📄 许可证

本示例项目遵循 MIT 许可证。详情请参考 [LICENSE](../LICENSE) 文件。
