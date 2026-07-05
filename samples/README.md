# PulseRPC 示例项目

本目录包含 PulseRPC 框架的各种使用示例，帮助开发者快速上手和理解框架的功能特性。

> 当前维护的可运行入口优先参考 ChatApp、JwtAuthentication、JsonTranscoding、ServiceFactoryExample 和 HubFactoryExample。BasicUsage、DnsExample、EtcdExample、MonitoringExample、TracingExample、ServiceRegistrationExample 保留为历史探索代码，仍引用旧的独立 ServiceDiscovery/Monitoring/Tracing 包名或旧接口，不能作为当前可运行示例使用。

## 📁 示例清单

| 示例 | 说明 |
|------|------|
| [ChatApp](ChatApp/) | 基于服务隔离架构的实时聊天/游戏示例，包含控制台与 Unity 客户端（见 [README](ChatApp/README.md)） |
| [GameApp](GameApp/) | 完整的游戏服务器示例（见 [README](GameApp/README.md)） |
| [DistributedGameApp](DistributedGameApp/) | 分布式游戏服务器示例（见 [README](DistributedGameApp/README.md)） |
| [JwtAuthentication](JwtAuthentication/) | JWT 身份验证集成示例（客户端 / 服务端 / 共享契约） |
| [JsonTranscoding](JsonTranscoding/) | JSON 协议转码示例 |
| [ServiceFactoryExample](ServiceFactoryExample/) | 服务工厂（`AddPulseServiceFactory`）用法示例 |
| [HubFactoryExample](HubFactoryExample/) | Hub 工厂用法示例 |

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
# 构建整个解决方案（仓库根目录）
dotnet build

# 运行某个示例（以 ChatApp.Server 为例）
cd samples/ChatApp/ChatApp.Server
dotnet run
```

依赖外部组件的示例可使用 Docker 启动对应服务后再运行，具体命令请参阅示例目录内的说明或源码注释。

## 🎯 使用场景

PulseRPC 适用于以下场景：

- **微服务架构** - 连接级负载均衡、健康检查与故障转移
- **分布式系统** - 静态成员或 Consul/Etcd/Kubernetes 驱动的集群成员发现
- **实时游戏** - KCP 低延迟传输、服务隔离与高并发消息处理

## 📖 更多文档

完整的中文文档位于仓库的 [`docs/`](../docs/) 目录，推荐先阅读：

- [PulseRPC 快速开始指南](../docs/使用指南/PulseRPC%20快速开始指南.md)
- [PulseRPC 客户端和服务端使用指南](../docs/使用指南/PulseRPC%20客户端和服务端使用指南.md)
- [PulseRPC 最佳实践指南](../docs/使用指南/PulseRPC%20最佳实践指南.md)

## 🤝 贡献指南

欢迎提交新的示例和改进现有示例：

1. Fork 项目仓库
2. 创建功能分支
3. 添加示例代码和文档
4. 提交 Pull Request

## 📄 许可证

本示例项目遵循 MIT 许可证。详情请参考 [LICENSE](../LICENSE) 文件。
