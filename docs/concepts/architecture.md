# 架构总览

本文描述当前代码的稳定边界。第一次接入请从 [HelloRPC](../../samples/HelloRPC/) 开始；本页用于理解组件职责和扩展成本，不是未来路线图。

## 一条主路径

PulseRPC 的推荐编程模型只有一条：

1. 共享契约继承 `IPulseHub`。
2. Server Source Generator 生成入站路由表和响应序列化器。
3. Client Source Generator 生成 `IClientChannel.GetHub<T>()` 代理。
4. 服务端通过 `AddPulseServer` 进入同一个 `ServerRuntime`。
5. 有状态实例只通过 `PulseServiceManager` 和 `IServiceAccessor<T>` 管理。

```text
Contracts (IPulseHub)
     │
     ├── Client Source Generator ──> generated proxy ──> IClientChannel
     │
     └── Server Source Generator ──> IServiceRoutingTable
                                              ▲
TCP / KCP listener ──> Channel Registry ──> fixed worker shards
                                              │
                                      ResponseProcessor
```

## 包边界

| 包 | 当前职责 |
|---|---|
| `PulseRPC.Abstractions` | Hub、消息、认证、地址和传输契约 |
| `PulseRPC.Shared` | TCP/KCP 与共享 wire 实现 |
| `PulseRPC.Client` | 客户端生命周期、连接管理、负载均衡和通道 |
| `PulseRPC.Server` | Host、连接注册表、消息执行、Hub/Service、路由和集群入口 |
| `PulseRPC.*.SourceGenerator` | 客户端代理、服务端路由、清单与响应序列化 |
| `PulseRPC.Infrastructure.*` | Consul、etcd、Kubernetes 等成员发现后端 |
| `PulseRPC.Backplane.Redis` | Redis backplane 与租约存储实现 |

业务项目不应依赖 `Processing.Engine` 下的历史 tiered 类型。公开但未接线的兼容入口均通过 `[Obsolete]` 给出迁移方向。

## 服务端运行时

`ServerRuntime` 是 standard、named 和 compatibility factory 的共同运行时核心，组件均由 `ServerRuntimeComponentFactory` 创建。

每个 runtime 拥有：

- 一组 listener；
- 一个权威 `ServerChannelManager`；
- 一个固定 shard `MessageEngine`；
- 一个 `MessageDispatcher`；
- 一个 `ResponseProcessor`。

`IPulseServer.ChannelPool` 不再保存第二份集合，只是权威注册表的只读兼容视图。Server Stop 会停止 listener、等待接入任务、关闭活动 channel，再停止消息引擎；完成后不保留该 server 的活动 socket 或 worker。

详细顺序见[服务端运行时](server-runtime.md)和[传输模型](transport-model.md)。

## 消息执行与资源上界

消息引擎在构造时创建固定数量的 shard。连接只保存轻量 generation lease，并在本次生命周期内绑定一个 shard。

| 资源 | 上界来源 |
|---|---|
| worker 数 | `PulseServerOptions.MessageWorkerShardCount` |
| 消息排队数 | shard 数 × `MessageQueueCapacityPerShard` |
| 响应排队数 | 内部有界响应队列 |
| 连接对象 | 当前权威 Channel Registry 的活动连接数 |

队列满时立即拒绝，不通过无限等待、每连接后台任务或 adaptive batching 隐藏压力。断开会取消该 generation 的在途工作；同 ID 重连不会继承旧取消、认证上下文或响应。

## Hub 与 Service

Hub 是无状态 RPC 入口，按标准 DI 注册。需要按玩家、房间或 Actor key 保留状态时，把状态放入实现 `IPulseService` 的类型：

```csharp
services.AddPulseService<PlayerService>();
services.AddTransient<IPlayerHub, PlayerHub>();
```

Hub 注入 `IServiceAccessor<PlayerService>`，通过 key 获取受管理实例。`PulseServiceManager` 负责创建、Start、缓存、Stop、Dispose 和可选租约；`TService` 本身不直接注册到 DI，避免产生绕过 Manager 的平行实例。

旧 `PulseServiceFactory` / `PulseHubFactory` 只作兼容保留。迁移方式见[迁移指南](../guides/migration.md)。

## Standard、Named 与 Factory

- `AddPulseServer` 是单实例 Generic Host 推荐入口，自动 Start/Stop。
- `AddNamedPulseServer` 为每个名称创建独立 runtime、Channel Registry、Router、shard 和队列；统一 HostedService 顺序启动、逆序停止。
- `PulseServerFactory` 已废弃，只为兼容无 Host 的旧调用；内部仍复用 standard DI 组合并拥有 provider。

Named runtime 共享应用级业务 DI 和生成路由表，因此不是任意不可信租户之间的安全沙箱。

## 路由与集群

单节点 `AddPulseServer` 默认注册 `LocalPulseRouter` 和 `InProcessBackplane`。它支持本地 Connection、Group、User 与 Actor 寻址。

`AddPulseClustering` 在此基础上增加成员视图、一致性哈希、节点链路、Actor 目录和租约。动态发现由 Infrastructure 包接入；多节点生产不能使用进程内租约存储。

集群能力是 opt-in。单节点应用不需要理解 node wire、租约或状态迁移。详细事实见[集群与路由](clustering-and-routing.md)。

## 客户端边界

客户端主路径是：

```csharp
using var client = new PulseClientBuilder().Build();
await client.InitializeAsync();
var channel = await client.ConnectToServerAsync("127.0.0.1", 5055);
var hub = channel.GetHub<IMyHub>();
```

客户端 Builder 只保留已接线的连接、负载均衡、日志、序列化和传输选项；旧认证、连接池、重试和预设入口已在破坏性版本中移除，避免未接线配置形成虚假的运行时承诺。

## 复杂度控制原则

- 新功能优先接入现有 `ServerRuntime`，不创建第二套 standard/named 依赖图。
- 连接状态只存在于一个 Channel Registry。
- 运行时配置只暴露实际被读取的选项。
- 后台任务必须有明确 owner，Stop/Dispose 必须等待同一个缓存生命周期任务。
- 新用户文档只引用 HelloRPC；专项样例不复制另一套 Quickstart。
- 集群、Gateway、迁移和兼容 Factory 保持 opt-in，不进入最小单节点认知路径。

## 相关文档

- [RPC 模型](rpc-model.md)
- [服务端运行时](server-runtime.md)
- [传输模型](transport-model.md)
- [Actor 模型](actor-model.md)
- [迁移指南](../guides/migration.md)
