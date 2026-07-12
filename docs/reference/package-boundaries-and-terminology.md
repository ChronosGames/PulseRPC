# 包边界与核心术语

本页定义当前代码新增时必须遵守的包依赖、公开类型和命名规则。既有已发布 API 为保持二进制兼容暂时保留，不代表允许继续复制其历史布局。

## 包依赖方向

核心运行时依赖方向固定为：

```text
PulseRPC.Abstractions <- PulseRPC.Shared <- PulseRPC.Client
PulseRPC.Abstractions <- PulseRPC.Shared <- PulseRPC.Server
PulseRPC.Abstractions <- PulseRPC.Infrastructure*
PulseRPC.Abstractions <- PulseRPC.Backplane.Redis
```

`PulseRPC.Abstractions` 是最底层契约包，不得引用其它 `PulseRPC.*` 运行时程序集。它只承载跨包接口、属性、枚举、轻量 value object、错误和最小 wire DTO。TCP/KCP、缓冲池、队列、批处理、指标采集和生命周期管理属于实现包。

## 构建期门禁

`PulseRPC.Analyzers` 随仓库构建并作为 analyzer 接入 `PulseRPC.Abstractions` 与 `PulseRPC.Shared`：

| 诊断 | 触发条件 |
| --- | --- |
| `PRPC2001` | `PulseRPC.Abstractions` 引用了其它 `PulseRPC.*` 运行时程序集 |
| `PRPC2002` | 新公开类型继续写入不归当前程序集所有的历史命名空间 |
| `PRPC2003` | `PulseRPC.Abstractions` 新增公开 Manager、Pool、Transport、Provider 等实现型类型，或公开实现本包接口 |

旧类型通过对应 TFM 的 `PublicAPI.Shipped.txt` 获得兼容豁免；`PublicAPI.Unshipped.txt` 不能绕过边界检查。新增契约应进入 `PulseRPC.Abstractions.*` 命名空间，新增 Shared 类型不得进入 `PulseRPC.Abstractions.*`。

如果确实需要改变边界，应先给出包职责、迁移路径和兼容策略，不得只把新类型加入 Shipped 基线来消除诊断。

## 公开类型规则

- 默认使用 `internal`；只有跨程序集或用户代码必须直接引用的契约才公开。
- Abstractions 中优先公开接口、枚举、只读 value object 和最小 DTO，不公开运行时管理器或默认实现。
- 已公开但位置不合理的类型采用新增正确入口、`[Obsolete]` 迁移、最终 major 版本移除的顺序处理，不直接移动程序集。
- 新 API 避免 `ValidationResult`、`MessagePriority`、`State` 等无层级限定的重复短名；名称必须表达所属协议或生命周期。
- 所有运行时包继续由 `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` 冻结 ABI。

## 核心术语

| 术语 | 唯一含义 |
| --- | --- |
| Hub | RPC 契约和远程调用面；通常是继承 `IPulseHub` 的接口 |
| Service | 服务端承载 Hub 的运行时实现及其启动、停止、调度生命周期 |
| Actor | 由 `(Hub, Key)` 唯一寻址、可带状态和节点归属的 Service 实例；不是另一套 RPC 契约 |
| Channel | 在连接上提供请求关联、协议分派、取消和双向 RPC 的会话层 |
| Transport | TCP/KCP 等负责端点、连接状态和字节收发的 I/O 层 |
| Connection | Transport 建立的一条端点链路；描述一次连接身份和生命周期，不等同于 Channel |
| Receiver | 客户端接收服务端调用的方向角色；新契约仍使用 client-facing Hub 表达，不新增独立 Receiver 类型体系 |
| Backplane | 节点间 fan-out 和目录解析后端，不负责 Actor 业务状态持久化 |

代码、日志和文档应按上表选择术语。例如“Actor migration”描述 keyed Service 的节点归属迁移，“Transport reconnect”只描述底层链路重建，“Channel request”描述 RPC 请求关联。
