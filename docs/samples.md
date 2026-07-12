# 示例项目

示例按“唯一黄金路径”“专项示例”和“历史探索代码”区分。

## 唯一黄金路径

首次运行只使用三项目 HelloRPC：

```bash
dotnet build samples/HelloRPC/HelloRPC.sln
```

| 示例 | 用途 | 阅读入口 |
| --- | --- | --- |
| HelloRPC | Contracts / Server / Client 三项目真实 TCP RPC；由 CI 端到端运行 | [samples/HelloRPC/README.md](../samples/HelloRPC/README.md) |

## 专项示例

| 示例 | 用途 | 阅读入口 |
| --- | --- | --- |
| ChatApp | 实时聊天/游戏房间、服务隔离、控制台与 Unity 客户端 | [samples/ChatApp/README.md](../samples/ChatApp/README.md) |
| JwtAuthentication | 连接级 JWT 登录、连接身份写入、后续 RPC 鉴权示例 | [samples/JwtAuthentication](../samples/JwtAuthentication/) |
| HubFactoryExample | 历史项目名；代码已迁移为无状态 Hub + `IServiceAccessor` | [samples/HubFactoryExample](../samples/HubFactoryExample/) |
| ServiceFactoryExample | `AddPulseService` / `IServiceAccessor` 服务实例生命周期 | [samples/ServiceFactoryExample](../samples/ServiceFactoryExample/) |
| JsonTranscoding | PulseRPC Hub 与显式 ASP.NET JSON 网关；不声称自动 wire 转码 | [samples/JsonTranscoding/README.md](../samples/JsonTranscoding/README.md) |

## 游戏后端样例

| 示例 | 状态 | 说明 |
| --- | --- | --- |
| GameApp | 综合样例 | 包含 Auth/Game/Battle 等模块，文档较多，部分内容是历史蓝图。 |
| DistributedGameApp | 分布式游戏后端样例 | 展示多服务器类型、基础设施集成和启动编排，部分设计文档是历史记录。 |

## 历史探索示例

`samples/README.md` 中列出的 BasicUsage、DnsExample、EtcdExample、MonitoringExample、TracingExample、ServiceRegistrationExample 等示例保留为历史探索代码。它们可能引用已移除项目、旧包名或旧接口，不应作为当前新项目模板。
