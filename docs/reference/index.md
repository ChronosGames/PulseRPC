# 参考手册

本页聚合当前维护者最常查的事实型信息。详细教程请看 `getting-started/` 和 `guides/`。

## 包和项目

| 项目 | 作用 |
| --- | --- |
| `PulseRPC.Abstractions` | 公共契约、基础类型、认证、路由、传输抽象 |
| `PulseRPC.Client` | 客户端运行时、连接管理、负载均衡 |
| `PulseRPC.Server` | 服务端运行时、Hub/Service 调度、集群路由 |
| `PulseRPC.Shared` | TCP/KCP、缓冲池、批处理等共享组件 |
| `PulseRPC.Infrastructure` | 发现和集群成员通用基础设施 |
| `PulseRPC.Infrastructure.*` | Consul、Etcd、Kubernetes 等后端 |
| `PulseRPC.*.SourceGenerator` | 客户端/服务端代码生成 |

## 常用配置入口

| 入口 | 用途 |
| --- | --- |
| `PulseClientBuilder` | 创建客户端、添加连接、负载均衡、日志 |
| `ConnectionConfig` / `ConnectionDescriptor` | 客户端连接描述 |
| `AddPulseServer(...)` | 注册默认服务端 |
| `AddNamedPulseServer(...)` | 注册命名服务端 |
| `TransportChannelConfiguration` | 服务端 TCP/KCP 监听配置 |
| `AddPulseClustering(...)` | 注册集群路由和节点配置 |
| `AddConsulDiscovery` / `AddEtcdDiscovery` / `AddKubernetesDiscovery` | 覆盖默认成员发现 |

## 协议和传输事实

- TCP/KCP 实现位于 `PulseRPC.Shared`。
- 传输抽象为 `ITransport`、`IClientTransport`、`IServerTransport`、`IServerListener`。
- 当前帧头、握手和协议常量以 `src/PulseRPC.Abstractions/Transport/ProtocolConstants.cs` 以及 `PulseRPC.Shared` 中实现为准。
- `TransportOptions.SmallPacketThreshold` 和 `ChunkSize` 当前保留兼容，已不作为应用层分片主路径依据。

## Public API

- 公共 API 使用 `PublicAPI.Shipped.txt` 和 `PublicAPI.Unshipped.txt` 管理。
- 新增公共类型或成员后需要更新对应 Unshipped 文件。
- 删除或重命名公共 API 需要明确迁移路径，优先使用 `[Obsolete]` 分阶段替代。

## 术语

| 术语 | 含义 |
| --- | --- |
| Hub | RPC 契约接口或服务调用面 |
| Actor | 带身份和状态的 Hub/Service 实例 |
| ServiceName | 服务或 Actor 类型名，用于注册和路由 |
| ServiceId | 具体 Actor 实例身份 |
| Receiver | 服务端推送到客户端的回调接口 |
| NodeId | 集群节点身份 |
| Backplane | 节点间广播/路由的共享通道 |
| L2 | 多节点 Actor 属主选择和路由 |
| L3 | 可选 Actor 状态迁移 |

