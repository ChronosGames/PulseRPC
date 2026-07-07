# 03 运行时管线与传输层评审

## 总体判断

运行时最大风险不是单个性能点，而是“设计上存在高性能/多级/全双工/优先级等概念，但真实请求路径没有完全闭合”。应先把真实路径修正、测稳，再做性能优化。

## 客户端通道问题

### C1：`async void` 处理路径不利于生命周期控制

证据：

- `TransportChannel.ProcessPing` 是 `async void`：`src/PulseRPC.Client/Channels/TransportChannel.cs:559`。
- `TransportChannel.ProcessReverseRequest` 是 `async void`：`TransportChannel.cs:793`。

影响：

- 调用方无法等待处理完成，也无法在断连/Dispose 时统一取消。
- 虽然内部 catch 了异常，但测试和生命周期协调困难。

建议：

- 改为返回 `Task`，由消息分发处统一 fire-and-track。
- Dispose/Disconnect 时等待或取消已登记的后台任务。

### C2：请求失败丢失原始异常

证据：

- `InvokeRawAsync` catch 中调用 `_responseManager.TrySetException(messageId, new InvalidOperationException("Request failed"))`：`TransportChannel.cs:679-682`。

影响：

- pending response 看到的是泛化异常，原始 send/serialize/cancel 失败原因丢失。
- 日志与调用方异常不一致。

建议：

- catch 捕获 `Exception ex`，把 `ex` 或包装后的 inner exception 传给 response manager。

### C3：取消语义只在本地生效

证据：

- `InvokeRawAsync` 注释明确“不会向服务端发送 MessageType.Cancel 帧”：`TransportChannel.cs:625-628`。

影响：

- 用户取消请求后，服务端仍可能继续执行业务方法。
- 对长耗时调用、限流、资源释放语义不完整。

建议：

- P1：定义端到端取消协议：客户端取消 -> Cancel frame -> 服务端 request context cancellation -> response processor 丢弃/返回 canceled。
- P0：文档和 XML 注释明确当前是本地取消。

### C4：断连不立即完成 pending response

证据：

- `TransportChannel.DisconnectAsync` 直接 `_transport.DisconnectAsync`：`TransportChannel.cs:149-151`。
- pending responses 主要依赖超时/响应路径完成。

影响：

- 断连后调用方可能等到默认超时才失败。
- fail-fast 和故障转移变慢。

建议：

- 断连/transport closed 事件中枚举 pending responses，统一设置 `ConnectionClosedException`。
- 补测试：发起请求后断开连接，请求应在短时间内失败。

## 服务端接入与通道管理

### C5：新连接事件使用错误对象

证据：

- `_channelManager.AddChannel(e.Transport)` 返回值未使用：`src/PulseRPC.Server/PulseServer.cs:276`。
- `ClientConnectedEventArgs` 接收 `e.Transport as IServerChannel`：`PulseServer.cs:278-279`。

影响：

- 新连接可能被注册后又因强转失败被关闭。
- 事件订阅者拿不到真实 channel。

建议：

- 使用 `var channel = _channelManager.AddChannel(e.Transport);`，事件传 `channel`。
- 这是 P0 修复。

### C6：`IPulseServer` 的 channel 查询与实际通道模型脱节

证据：

- `PulseServer.GetChannel` 返回 null：`PulseServer.cs:406-411`。
- `PulseServer.GetAllChannels` 返回空数组：`PulseServer.cs:415-420`。
- `ServerChannelManager` 实际持有 `IServerChannel` 集合，并有 `GetChannel/GetAllChannels/BroadcastAsync`。

影响：

- 公开 API 无法访问真实通道。
- 服务端全双工能力、反向 Ask 与管理面查询割裂。

建议：

- 统一 `IServerChannel` 与 `ITransportChannel` 的边界：要么 `IServerChannel` 实现 `ITransportChannel`，要么公开 `IServerChannel` 查询。
- 不要保留返回空的公开 API。

## 消息管线问题

### C7：`MessageDispatcher` 的队列 worker 未处于主路径

证据：

- `MessageDispatcher.StartAsync` 会启动 dispatcher tasks：`src/PulseRPC.Server/Processing/Engine/MessageDispatcher.cs:101`。
- `MessageEngine.StartAsync` 只启动 `_responseProcessor`：`src/PulseRPC.Server/Processing/Engine/MessageEngine.cs:183-191`。
- `MessageEngine` 处理消息时直接调用 `_messageDispatcher.DispatchAsync`：`MessageEngine.cs:498`、`:507`。
- `DispatchAsync` 内部直接按 `ProtocolId` 调用 routing table：`MessageDispatcher.cs:152-170`。

影响：

- 优先级 channel、dispatcher worker、队列背压可能没有参与真实 RPC 请求路径。
- 用户或维护者看到“高性能队列/调度”代码，会误以为已经生效。

根因：

- Dispatcher 同时承担“同步路由器”和“异步队列调度器”两个职责，主路径选择不清晰。

建议：

- P1：拆分 `IMessageRouter` 与 `IMessageDispatchQueue`。
- 若选择直接 dispatch，就删除/废弃未使用队列。
- 若选择队列 dispatch，`MessageEngine.StartAsync` 必须启动 dispatcher，并将消息 enqueue 进入队列。

### C8：指标存在占位值和错误计数

证据：

- `MessageEngineMetrics.GetAverageLatencyMs() => 2.5`：`MessageEngine.cs:925`。
- `RecordEnqueueLatency(long ticks) { L1MessagesEnqueued.Add(ticks); }`：`MessageEngine.cs:927`，名称是 count 但写入 latency ticks。
- `PulseServer.GetPerformanceMetrics` 多项返回 0/TODO：`PulseServer.cs:370-374`。

影响：

- 生产监控会展示虚假指标。
- 性能优化无法基于真实数据判断。

建议：

- P0：未实现指标从公开 API 移除或改为 nullable。
- P1：明确计数器、直方图、计时器三类指标，禁止用 count 字段记录 latency。

### C9：响应序列化失败可能让客户端等待超时

证据：

- `ResponseProcessor` 依赖 `IResponseSerializerRegistry` 或静态 `ResponseSerializerRegistry.Instance`。
- `SerializeResponseAsync` 找不到 serializer 或 serializer 失败时抛异常，处理任务 catch 后只记录日志。

影响：

- 服务端已经执行完业务方法，但响应序列化失败时客户端可能只看到超时。
- 问题从“协议/生成器配置错误”被隐藏成“网络慢/超时”。

建议：

- P1：响应序列化失败应发送 Error frame，包含协议号和错误码。
- P1：启动时验证所有 routed method 都有 response serializer。

## 传输层问题

### C10：基础传输类公开虚属性但默认抛异常

证据：

- `TcpTransport.Id => throw new NotImplementedException()`：`src/PulseRPC.Shared/Tcp/TcpTransport.cs:152`。
- `KcpTransport.Id => throw new NotImplementedException()`：`src/PulseRPC.Shared/Kcp/KcpTransport.cs:34`。

影响：

- 基类是 public，直接使用或派生未 override 时会在运行时失败。
- API 表达为“有默认实现”，实际是“必须实现”。

建议：

- 改为 `abstract string Id { get; }`，或在基类构造函数强制传入 id。

### C11：传输 options 含未实现能力

证据：

- 客户端 `TransportChannelOptions` 已对 compression/encryption 标注 `[Obsolete("此功能尚未实现，设置无效")]`。
- 更底层 `TransportOptions` 仍存在 `UseCompression`、`UseEncryption` 等配置语义。

影响：

- 不同层对同一能力表达不一致。
- 用户可能在 transport 层打开开关，但实际上不会生效。

建议：

- 所有未实现开关统一 `[Obsolete]` 或从文档推荐路径移除。
- 真正实现时从协议协商、帧格式、压缩阈值、安全握手完整设计，不只加 bool。

### C12：TCP 接收事件的内存生命周期需要明确

观察：

- TCP receive loop 会把读缓冲切片作为 `ReadOnlyMemory<byte>` 通过事件传出。
- 当前订阅者大多会立即复制/同步处理，但事件契约没有明确内存有效期。

影响：

- 新订阅者若异步保存 `ReadOnlyMemory`，可能读到后续覆盖的数据。

建议：

- 事件 args 文档声明只在回调同步期间有效，或改为租赁 buffer/owned packet。
- 在高性能路径中用明确的 `IMemoryOwner<byte>` 或引用计数 buffer。

### C13：KCP 接收路径分配较重

观察：

- `KcpTransport.ProcessKcpReceive()` 每轮分配接收 buffer，并对每个包复制成新数组。

影响：

- 高频 UDP/KCP 场景下 GC 压力高。

建议：

- 用 `ArrayPool<byte>` 或现有 `NetworkBufferPool` 复用接收缓冲。
- 先补 microbenchmark，再优化，避免在非瓶颈路径过度改造。

## 服务发现与集群成员

### C14：服务发现成员刷新测试不稳定

证据：

- 全量 `dotnet test PulseRPC.sln` 中失败过 `BackendRemovingNode_TakesPrecedence_ClearsSuspicion`。
- 单独运行 `PulseRPC.Infrastructure.Tests` 时失败变为 `ProviderChanged_TriggersRefresh_AndRaisesChanged`。
- 单独失败断言：`changedFired` 期望大于等于 1，实际为 0。
- 相关实现：`DiscoveryClusterMembership.OnProviderChanged` 释放 `_refreshSignal`，后台循环拉取后在 `changed` 为 true 时 `RaiseChanged()`。

影响：

- 服务发现 watch/poll 的时序语义不稳定，会影响集群路由环重建。
- 测试不稳定会降低 CI 可信度。

建议：

- P0：先把测试改成可诊断：记录事件触发次数、refresh 次数、snapshot 变化。
- P1：在实现中避免 “信号已触发但订阅方未收到事件” 的竞态，必要时在 provider changed path 直接触发 refresh task。
- P1：明确 `Changed` 事件语义：只在 live set 变化时触发，还是后端 change 即触发。

