# 仓库地图

## 核心源码

| 路径 | 职责 |
| --- | --- |
| `src/PulseRPC.Abstractions` | 公共契约、认证、协议常量、路由、集群、传输抽象 |
| `src/PulseRPC.Client` | 客户端、连接管理、负载均衡、客户端传输 |
| `src/PulseRPC.Server` | 服务端 Host、Hub/Service 调度、路由、集群、认证、Gateway |
| `src/PulseRPC.Shared` | TCP/KCP、缓冲池、批处理、共享传输实现 |
| `src/PulseRPC.Infrastructure` | 发现抽象和通用集群成员实现 |
| `src/PulseRPC.Infrastructure.Consul` | Consul 成员发现 |
| `src/PulseRPC.Infrastructure.Etcd` | Etcd 成员发现 |
| `src/PulseRPC.Infrastructure.Kubernetes` | Kubernetes 成员发现 |
| `src/PulseRPC.Client.SourceGenerator` | 客户端代理和扩展生成 |
| `src/PulseRPC.Server.SourceGenerator` | 服务端路由、服务清单、响应序列化和分析器 |

## 测试

| 路径 | 覆盖 |
| --- | --- |
| `tests/PulseRPC.Client.Tests` | 客户端连接、通道、取消、生命周期 |
| `tests/PulseRPC.Server.Tests` | 服务端调度、路由、集群、Gateway、消息处理 |
| `tests/PulseRPC.SourceGenerator.Tests` | 协议号、生成器、分析器 |
| `tests/PulseRPC.Infrastructure.Tests` | 发现和成员解析 |
| `tests/PulseRPC.Backplane.Redis.Tests` | Redis backplane，通常需要容器环境 |

## 示例

| 路径 | 状态 |
| --- | --- |
| `samples/ChatApp` | 推荐维护样例 |
| `samples/JwtAuthentication` | 推荐认证样例 |
| `samples/JsonTranscoding` | 推荐转码样例 |
| `samples/GameApp` | 综合游戏样例，含历史蓝图 |
| `samples/DistributedGameApp` | 分布式游戏样例，含历史蓝图 |

## 文档

| 路径 | 用途 |
| --- | --- |
| `docs/index.md` | 人类读者入口 |
| `docs/getting-started` | 教程 |
| `docs/concepts` | 概念解释 |
| `docs/guides` | 任务指南 |
| `docs/reference` | 事实参考 |
| `docs/archive` | 历史资料 |
| `.agent` | Agent 操作文档 |

