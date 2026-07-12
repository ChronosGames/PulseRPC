# 传输与连接模型

本文只描述当前实现。首次运行 RPC 请使用三项目 [HelloRPC](../../samples/HelloRPC/)；这里解释它背后的传输、连接和消息所有权。

## 当前数据路径

```text
客户端生成代理
  -> IClientChannel
  -> TCP / KCP
  -> Server listener
  -> ServerTransportChannel
  -> ServerChannelManager（唯一连接注册表）
  -> MessageEngine（固定 worker shard + 每 shard 有界队列）
  -> IServiceRoutingTable
  -> ResponseProcessor
  -> 原连接 generation
```

这条路径有三个明确边界：

1. 传输负责建立连接、收发字节和关闭 socket。
2. Channel 负责连接身份、认证上下文、请求与响应关联。
3. 生成的 Hub 代理和路由表负责强类型 RPC。

运行时不会为每条连接创建消息 worker，也不会构造历史的 L1/L2/L3 消息管线、adaptive scheduler 或 `TieredMemoryPool`。

## 服务端连接所有权

每个 `ServerRuntime` 只有一个 `ServerChannelManager`。Listener 接受物理连接后，将其包装为 `ServerTransportChannel` 并发布到这个注册表；消息引擎、响应处理器、广播和管理查询都读取同一个实例。

```text
ServerRuntime
  ├─ listeners
  ├─ ServerChannelManager  <- 唯一连接注册表
  ├─ MessageEngine         <- 订阅连接与消息事件
  └─ ResponseProcessor     <- 按精确 channel generation 回应
```

连接注册成功后，注册表取得 channel 和底层 transport 的所有权：

- 连接断开、超时、Server Stop 或 DI provider Dispose 都会从注册表移除并释放 channel。
- 同一连接 ID 重连时，旧 channel 的迟到事件不能删除新 channel。
- 旧 generation 的取消帧、认证上下文和响应不能作用于同 ID 的新连接。
- `IPulseServer.ChannelPool` 只保留为只读兼容视图；写操作会抛出 `NotSupportedException`。新代码使用 `GetChannel` 和 `GetAllChannels`。

`StopAsync` 是资源边界：它先停止 listener、等待接入任务，再关闭注册表中的活动连接，最后停止消息引擎。返回后不应再有该 server 拥有的活动 socket 或消息 worker。

## 固定 shard 与背压

`PulseServerOptions` 是服务端消息运行时的有效配置入口：

```csharp
services.AddPulseServer(options =>
{
    options.AddTcp("tcp", 9090, isDefault: true);
    options.AddKcp("kcp", 9091);
    options.MessageWorkerShardCount = Math.Max(1, Environment.ProcessorCount);
    options.MessageQueueCapacityPerShard = 1024;
});
```

连接建立时以 round-robin 固定绑定一个 shard。worker 数只由 `MessageWorkerShardCount` 决定，不随连接数增长。每个 shard 的队列容量固定；队列满时新消息立即拒绝，载荷和请求取消状态在拒绝路径释放。

可通过 `RuntimeQueueMetrics` 观察真实 capacity、depth、high watermark、saturation 和 rejected enqueue，再用相同 workload 调整 shard 数或容量。

## 客户端黄金路径

客户端使用 `PulseClientBuilder` 创建一个生命周期明确的 `IPulseClient`，再显式建立 `IClientChannel`：

```csharp
using var client = new PulseClientBuilder().Build();
await client.InitializeAsync();

var channel = await client.ConnectToServerAsync("127.0.0.1", 9090);
try
{
    var hub = channel.GetHub<IMyHub>();
    var result = await hub.GetDataAsync("value");
}
finally
{
    await channel.DisconnectAsync();
    await client.StopAsync();
}
```

`GetHub<T>()` 由 Client Source Generator 提供；契约需继承 `IPulseHub`，并在客户端程序集使用 `PulseClientGeneration` 声明生成目标。完整接线见 [HelloRPC Client](../../samples/HelloRPC/HelloRPC.Client/)。

### 当前不支持的 Builder 能力

以下已发布入口尚未接入传输主路径，现带有 `[Obsolete]`，并在 `Build()` 时 fail-fast：

| 入口 | 当前状态 |
|---|---|
| `WithAuthentication` | 尚未接入客户端握手，不能保证令牌发送 |
| `WithConnectionPooling` | 尚未接入 `ConnectionManager` |
| `WithRetryPolicy` | 尚未接入连接或请求执行路径 |
| `WithLoadBalancing(strategy, options)` 的松散 `options` | 只有 `strategy` 生效；非空松散选项会失败 |

不要通过这些入口表达安全性或可靠性承诺。认证接线完成前，应在应用层或受控网关建立身份边界。

## TCP 与 KCP

当前枚举成员是 `TransportType.TCP` 和 `TransportType.KCP`。

- TCP 提供可靠、有序字节流，是 HelloRPC 和一般业务的默认选择。
- KCP 面向需要低延迟、可调 UDP 可靠性的场景；部署前需要验证 MTU、拥塞参数和网络策略。

服务端通过 `AddTcp` / `AddKcp` 配置 listener；客户端通过 `ConnectToServerAsync(..., transport: TransportType.TCP/KCP)` 选择传输。两者最终进入相同的 Channel Registry 和固定 shard 消息路径。

## Standard、Named 与 Factory

- `AddPulseServer`：推荐的单 server Generic Host 入口，自动 Start/Stop。
- `AddNamedPulseServer`：多个独立 runtime；每个名称拥有独立注册表、shard 和队列，统一 HostedService 顺序启动、逆序停止。
- `PulseServerFactory`：仅作兼容保留并带 `[Obsolete]`；内部仍复用 standard 组合根并拥有其 DI provider。

无论入口如何，运行时组件都由 `ServerRuntimeComponentFactory` 创建；连接表不会复制为第二个 pool。

## 相关文档

- [服务端运行时](server-runtime.md)
- [客户端和服务端使用指南](../guides/client-server.md)
- [命名服务器](../guides/named-server.md)
- [迁移指南](../guides/migration.md)
