# PulseRPC 示例项目

本目录包含 PulseRPC 框架的各种使用示例，帮助开发者快速上手和理解框架的功能特性。

> 各示例均为解决方案的一部分，可直接在仓库根目录构建。运行单个示例时进入对应目录执行 `dotnet run` 即可。部分示例（如服务发现相关）需要额外的外部依赖（如 Consul、Etcd），请参阅示例内的说明。

## 📁 示例清单

| 示例 | 说明 |
|------|------|
| [BasicUsage](BasicUsage/) | 服务发现、负载均衡、健康检查等基础用法演示 |
| [ChatApp](ChatApp/) | 基于服务隔离架构的实时聊天/游戏示例，包含控制台与 Unity 客户端（见 [README](ChatApp/README.md)） |
| [GameApp](GameApp/) | 完整的游戏服务器示例（见 [README](GameApp/README.md)） |
| [DistributedGameApp](DistributedGameApp/) | 分布式游戏服务器示例（见 [README](DistributedGameApp/README.md)） |
| [JwtAuthentication](JwtAuthentication/) | JWT 身份验证集成示例（客户端 / 服务端 / 共享契约） |
| [JsonTranscoding](JsonTranscoding/) | JSON 协议转码示例 |
| [ServiceRegistrationExample](ServiceRegistrationExample/) | 服务注册相关用法示例 |
| [ServiceFactoryExample](ServiceFactoryExample/) | 服务工厂（`AddPulseServiceFactory`）用法示例 |
| [HubFactoryExample](HubFactoryExample/) | Hub 工厂用法示例 |
| [MonitoringExample](MonitoringExample/) | 性能监控（Monitoring）扩展用法示例 |
| [TracingExample](TracingExample/) | 链路追踪（Tracing）扩展用法示例 |
| [DnsExample](DnsExample/) | 基于 DNS 的服务发现示例 |
| [EtcdExample](EtcdExample/) | 基于 Etcd 的服务发现示例 |

## 🚀 运行示例

```bash
# 构建整个解决方案（仓库根目录）
dotnet build

# 运行某个示例（以 BasicUsage 为例）
cd samples/BasicUsage
dotnet run
```

依赖外部组件的示例（例如需要 Etcd）可使用 Docker 启动对应服务后再运行，具体命令请参阅示例目录内的说明或源码注释。

## 🎯 使用场景

PulseRPC 适用于以下场景：

- **微服务架构** - 自动服务发现、智能负载均衡、健康检查与故障转移
- **分布式系统** - 服务注册中心、服务标签与动态更新
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
