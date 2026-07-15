# 迁移指南

本文记录当前推荐迁移方向。历史设计文档中的 `BaseService`、`ConcurrentServiceBase`、旧 ServiceDiscovery 包名和旧 Receiver 模型不再作为新代码入口。

## 迁移到统一 IPulseHub

1. 把服务契约收敛到继承 `IPulseHub` 的接口。
2. 服务端实现使用当前 `PulseServiceBase` / 服务注册 API。
3. 客户端通过 Source Generator 生成代理调用。
4. 服务端推送接口按当前 Hub/Receiver 生成器约定整理。

## 迁移服务发现

旧独立 `PulseRPC.ServiceDiscovery`、`PulseRPC.LoadBalancing` 命名空间不作为当前入口。新代码优先使用：

- `PulseRPC.Infrastructure`
- `PulseRPC.Infrastructure.Consul`
- `PulseRPC.Infrastructure.Etcd`
- `PulseRPC.Infrastructure.Kubernetes`

## 迁移历史示例

`samples/README.md` 已标记历史探索示例。迁移这些示例前，先确认当前公共 API，并补最小构建测试。

## 迁移服务端消息引擎配置

服务端消息引擎已收敛为固定数量 worker shard 和每 shard 一个有界队列。新代码只通过 `PulseServerOptions` 配置：

```csharp
services.AddPulseServer(options =>
{
    options.AddTcp(7000);
    options.MessageWorkerShardCount = Math.Max(1, Environment.ProcessorCount);
    options.MessageQueueCapacityPerShard = 1024;
});
```

连接会在当前生命周期内绑定一个 shard；队列满时立即拒绝新消息。断开连接会取消该 connection generation 的在途工作，旧积压不会被同 ID 的新连接继续处理。运行时不再创建每连接处理器、L1/L2/L3 管线、adaptive scheduler 或分层内存池。

以下已发布 API 仅为兼容保留，并已标记 `[Obsolete]`：

| 旧 API | 迁移方式 |
| --- | --- |
| `MessageEngineConfiguration` | 使用 `PulseServerOptions.MessageWorkerShardCount` 和 `MessageQueueCapacityPerShard` |
| `TieredEngineManagerOptions` / `TieredMessageProcessorOptions` | 不再配置每连接队列、batch 或 L1/L2/L3；改用两个 shard 选项 |
| `AddTieredMessageEngine(...)` / tiered monitoring options | 使用 `AddPulseServer(...)`；指标读取 `EngineStatistics` 与 `RuntimeQueueMetrics` |
| `AdaptiveBatchScheduler` 及其 batch 参数 | 不再接入服务端运行时；通过固定 shard 数和有界容量控制并发与背压 |
| `AddServiceScheduler(...)` / `InvokeWithSchedulerAsync(...)` | 该独立 scheduler 不被 `MessageEngine` 消费；删除注册，使用固定 shard 配置 |
| `ServerPreset` / `ServerPresets` / `PulseServerOptions.UsePreset(...)` | 显式调用 `AddTcp` / `AddKcp` 并设置两个 shard 选项 |
| `PulseServerOptions.BackpressurePolicy` / `DefaultOperationTimeout` / `MaxConcurrentOperations` / `EnableDetailedLogging` | 背压使用有界 shard 队列；并发使用 shard 数；日志使用 `Microsoft.Extensions.Logging` |
| `BackpressureStrategy` | 未接入固定 shard 引擎；队列满固定为立即拒绝，通过队列容量和拒绝指标调优 |
| `ServerOptions` / `ServerConfigurationBuilder` | 使用 `PulseServerOptions` 和 `AddPulseServer(...)` |
| `ResponseProcessorOptions` | 不再作为 `AddPulseServer` 配置入口；响应处理由内部运行时组合 |
| `IPulseServer.ChannelPool` mutation | 使用 `GetChannel` / `GetAllChannels` 查询；连接由 runtime 独占，停止服务器时统一释放 |
| `AddPulseServiceFactory<T>` / `PulseServiceFactoryOptions` / `IPulseServiceFactoryMetrics` | 使用 `AddPulseService<T>`、`PulseServiceManagerOptions` 与 `IServiceAccessor<T>` |
| `ServiceLifecycleOptions` | 未被统一管理器消费；使用 `PulseServiceManagerOptions` 配置回收与缓存生命周期 |
| `AddPulseHubFactory<THub,TService>` / `IPulseHubFactory<THub,TService>` | Hub 保持无状态并使用标准 DI；通过 `IServiceAccessor<TService>` 访问有状态实例 |
| `WithAuthentication` / `WithConnectionPooling` / `WithRetryPolicy` | 已移除；删除这些配置，认证、池化与重试需要在业务层或未来已接线 API 中显式建模 |
| `ClientPresets` / `UseGameClientPreset` 等 | 已移除；改为显式配置连接 transport，并通过 `ClientOptions.LoadBalancing` 配置有效负载均衡输入 |
| `ClientOptions.DefaultTimeout` / `MaxConcurrentConnections` / `EnableDebugMode` / `EnableStatistics` / `AutoCleanupInterval` / `Settings` | 已移除；RPC 超时使用生成方法的 `CancellationToken`，连接/握手参数放到各 `ConnectionDescriptor.TransportOptions`，日志使用 `Microsoft.Extensions.Logging` |
| `ChannelPresets` | 已移除；使用 `ConnectionDescriptor.TransportOptions`（TCP/KCP 强类型派生项） |
| `ConnectionPoolOptions` / `PoolingStrategy` | 已移除；注册显式 `ConnectionDescriptor`，不要创建池配置 DTO |
| `Configuration.RetryPolicy` | 已移除；使用业务层有界重试并确保幂等 |
| `ServiceConnectionOptions` | 已移除；直接配置 `ConnectionDescriptor` |
| `ServiceProxyOptions` 中除 `LoadBalancingHint` / `StickyKey` 外的字段 | 已移除；使用显式 `connectionId` 重载或 transport/channel 的有效配置 |
| `EventListenerOptions` | 已移除；新代码直接使用 `IClientChannel.RegisterReceiver<T>()` 或无 options 的生成注册入口 |
| `ServiceDiscoveryOptions` | 已移除；注册显式连接，服务端集群发现使用 `PulseRPC.Infrastructure.*` |

不要把旧字段机械映射成新的高数值。先使用默认值，再以 `message-engine.shard` 队列指标和固定 workload 决定是否调整。

## 迁移到严格 Hub 路由和 node wire v2

- 重新生成客户端代理；新代理要求通道实现 `IHubAddressedClientChannel` 并始终发送 canonical Hub，不再静默回退为空 Hub 调用。
- 手写 `IClientChannel.InvokeRawAsync` / `SendCommandAsync` 调用无法提供 Hub，现已标记为 `[Obsolete]` 并给出迁移诊断。需要进入严格网络入口的代码应改用 `IHubAddressedClientChannel.InvokeHubRawAsync` / `SendHubCommandAsync`。
- 所有集群节点先部署支持能力协商的版本，再保持 `ClusterNodeWireOptions.AllowLegacyActorProtocol=false`。如滚动升级必须短期开启 legacy，应接受该窗口没有 claims 传播与 lease fencing，并在升级完成后关闭。
- 多节点环境注册共享 `IActorLeaseStore`；默认进程内实现不再被多成员拓扑接受。
- 显式设置 `TcpNodeTransportOptions.SecurityMode`。生产选择 `ExternalMutualTls` 前必须先让节点端口实际处于 mTLS 保护层之后；本机测试才可使用 `InsecureDevelopment`。
- wire v2 的 Send 现在等待远端执行 ACK。容量规划应重新测量延迟/吞吐；跨进程 exactly-once 仍需持久 inbox 或业务幂等，不能只依赖进程内去重窗口。

## 相关文档

- [RPC 模型](../concepts/rpc-model.md)
- [Source Generator 模型](../concepts/source-generation.md)
- [测试指南](testing.md)
- [性能指南](performance.md)
- [传输与消息执行模型](../concepts/transport-model.md)
- [历史归档](../archive/)
