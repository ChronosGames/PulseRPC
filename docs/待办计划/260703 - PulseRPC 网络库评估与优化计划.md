# PulseRPC 网络库评估与优化计划

## 结论速览
- **大包支持：默认配置下是"坏的"** —— 序列化后 >16KB 的消息会被接收端判为非法长度并直接断开连接。
- 存在**两套服务端收发管道**（一套死代码但其配置项仍暴露给用户）、**双重发送批处理/多次拷贝**、多套重复的 buffer pool / 调度器。
- RPC 调度：Unary/OneWay/背压/优先级较完整；**Cancel 未打通、Streaming 未实现、服务端 Deadline 未强制**。
- 命名有硬伤：两个同名 `MessageHeader`；`IPulseRPCSerializer.cs` 内没有该接口。
- 热路径有多处可消除的堆分配与拷贝（分配式 MemoryPack 头序列化、BitConverter.GetBytes、Payload.ToArray、header 两次 socket 写 + Flush）。

## 关键证据（file:line）
- RecvBufferSize=8192 (TransportOptions.cs:14)；接收校验 `header.Length > RecvBufferSize*2` 断开 (Tcp/TcpTransport.cs:466)
- SmallPacketThreshold=64KB (TransportOptions.cs:91)、ChunkSize=32KB (TransportOptions.cs:96) → 均产生 >16KB 帧
- 分片重组 UAF：GetCompleteData 后立即 Dispose 归还池 (LargePacketHandler.cs:63-65,274-277)
- 帧头 BitConverter.GetBytes 分配 (AsyncSpanHelper.cs:133-136,141-144)；header/body 两次 Write+Flush (AsyncSpanHelper.cs:52-58,84-90)
- 客户端头分配式序列化 (TransportChannel.cs:617)；三层缓冲拷贝 (ThreeTierSendBuffer.cs:116-117)；传输层再拷贝 (Tcp/TcpTransport.cs:236-238,246-247)
- TcpClientTransport 未实现 IScatterGatherTransport (TcpClientTransport.cs:11) → 三层缓冲 L3 退化
- 服务端收包 Payload.ToArray 堆分配 (MessagePacket.cs:20 MessagePacketHolder)
- EnvelopeRelay header 重复序列化 (EnvelopeRelay.cs:104,125,145)
- 死代码管道 MessageReceiver/ResponseTransmitter/MessageParser（src 无 new），但 Options 暴露于 UnifiedServerOptions.cs:26,31 与 ServerPreset.cs
- 两个 MessageHeader：Tcp/TcpTransport.cs:24 vs Messaging/MessageHeader.cs:122
- Cancel 未处理（grep 全库无 MessageType.Cancel 发送/处理）
- 孤立文件 src/PulseRPC/Server/Sessions/EnhancedServerChannelManager.cs（0 引用）

## 优化计划（优先级）
### P0 正确性
1. 统一并修复大包 framing：TCP 用单一「4B 总长 + body」流式收发，去掉应用层分片；或至少让 ChunkSize/SmallPacketThreshold 与接收上限联动校验。加单包最大尺寸上限。
2. 修复 LargePacketHandler UAF（消费完成后再归还池，或返回拷贝/引用计数缓冲）。
3. Cancel 端到端打通或明确文档限制。
4. 统一 endianness（全用 BinaryPrimitives LE）。

### P1 架构/性能
5. 删除死代码管道（MessageReceiver/ResponseTransmitter/MessageParser/ResponseBatcher）及其误导性 Options，或将活跃引擎与之二选一收敛为单一管道。
6. 移除/合并客户端双重批处理：让 TcpTransport 直接支持 gather 写并去掉 ThreeTierSendBuffer，或删除传输层内部队列。
7. 头序列化零分配：MemoryPackSerializer.Serialize(bufferWriter, header)；帧头改 BinaryPrimitives；合并 header+body 单次写、去掉每包 Flush。
8. 服务端收包去 ToArray：用池化缓冲 + 引用计数传递到引擎。
9. 清理死 buffer pool（OptimizedNetworkBufferPool、LockFreeRingBuffer）与未接入调度器（PriorityAware/AffinityAware）。
10. 传输实现（Tcp/Kcp/pool）从 Abstractions 下沉到实现层。

### P2 命名/可维护性
11. 传输帧头改名 FrameHeader；IPulseRPCSerializer.cs 改名或补接口。
12. 收敛营销式前缀（Enhanced/Optimized/HighPerformance/ThreeTier）。
13. Streaming 设计（IAsyncEnumerable）路线图；服务端 Deadline 强制。

## 亮点（保留）
ProtocolId(ushort) 路由、EnvelopeRelay 网关零反序列化转发、Actor 串行邮箱 + Reentrant 读并发、Channel 背压、MemoryPack。

## 实施进展（260703）

### 已完成（已通过 Abstractions/Server/Client 编译验证，0 错误）
- **P0-1 大包 framing**：
  - 新增 `TransportOptions.MaxPacketSize`（默认 4MB）作为单包最大尺寸上限。
  - 接收校验由 `header.Length > RecvBufferSize*2`（16KB）改为 `> MaxPacketSize`，允许大消息以单帧收发（`Tcp/TcpTransport.cs` ReceiveLoop）。
  - 发送统一走单帧路径，**移除应用层分片**（删除大包 chunk 分支与 `SendLargePacketInternalAsync`）；发送前做 `MaxPacketSize` 上限校验，超限拒发。
  - `SmallPacketThreshold`/`ChunkSize` 标记为「已废弃、不再影响 framing」（保留以兼容旧配置，后续 P1/P2 清理）。
- **P0-2 LargePacketHandler UAF**：`ProcessChunk` 在归还池（`Dispose`）前先将完整数据复制到独立缓冲，消除 use-after-free（该路径当前已随分片移除而不再触发，仍保留防御性修复）。
- **P0-4 endianness**：`AsyncSpanHelper` 帧头/块头读写全部改用 `BinaryPrimitives.*LittleEndian`，统一小端且零分配（消除 `BitConverter.GetBytes` 分配）。
- **P0-3 Cancel（文档化限制）**：明确记录现状——客户端仅本地取消（不发送 `Cancel` 帧），服务端不处理 `Cancel`、不中止在途方法。见 `MessageType.Cancel` XML 注释与 `TransportChannel.InvokeRawAsync` 注释。端到端取消归入 P2-13。
- **P1-7 发送热路径**：
  - 帧头写入零分配（`BinaryPrimitives`）。
  - 发送项缓冲改为「头部预留区 + 消息体」布局，发送循环写入帧头后**一次性写出整帧**（header+body 合并，减少 TCP 分段）。
  - 去掉每包 `FlushAsync`（`NetworkStream` 下为 no-op）。
  - 移除 `TcpSendItem.ItemType`/chunk 字段与未使用的 `_nextChunkId`。

- **P1-5 删除死代码管道 + 误导性 Options**（整解决方案编译 0 错误验证）：
  - 删除 `MessageReceiver`/`MessageParser`/`ResponseTransmitter`/`ResponseBatcher` 四个死管道类及其内含类型（`MessageReceiverOptions`/`ResponseTransmitterOptions`/`MessageReceivedEventArgs`/`MessageParseErrorEventArgs`/`ParseResult`）。
  - 从 `UnifiedServerOptions` 移除 `MessageReceiver`/`ResponseTransmitter` 属性；从 `ServerPreset` 移除对应预设配置。
  - 删除孤立文件 `src/PulseRPC/Server/Sessions/EnhancedServerChannelManager.cs`（不属于任何 csproj、0 引用）。
- **P1-9 清理死 buffer pool 与未接入调度器**（整解决方案编译 0 错误验证）：
  - 删除 `LockFreeRingBuffer`、`OptimizedNetworkBufferPool`+`PoolStatistics`（保留在用的 `NetworkBufferPool`）。
  - 删除未接入的 `PriorityAwareScheduler`/`AffinityAwareScheduler` 及其专属类型（`SessionContext`、服务端版 `ConsistentHash<T>`、`AffinitySchedulerOptions`、`MessageTask`、`SchedulerStatus`、`PrioritySchedulerOptions`、`PrioritySchedulerMetrics`）。

- **P1-6 合并客户端双重批处理**（Abstractions/Client 编译 0 错误验证）：
  - 客户端 `TransportChannel` 的发送不再经 `ThreeTierSendBuffer`，`InvokeRawAsync`/`SendCommandAsync` **直接调用 `_transport.SendAsync`**（传输层内部已有单写者发送队列 + 单帧写出）。
  - **删除 `ThreeTierSendBuffer`（含仅其使用的 `IScatterGatherTransport`）**，消除 L1 线程本地批 + L2 队列批 + 传输层队列的三重排队与多次拷贝。
  - 顺带修掉一个隐性 framing bug：`ThreeTierSendBuffer` 非 scatter-gather 降级路径会把多个 `MessagePacket` 合并进一个 buffer 后交给 `SendAsync`，而传输层会把整块合并 buffer 包成**单个帧**，服务端只能解析出第一个包、后续包错乱（负载/并发下才暴露）。直连发送后每消息=一帧，问题消失。
- **P2-11 命名修正**（三项目编译 0 错误验证）：
  - 传输层帧头 struct `MessageHeader` → **`FrameHeader`**（`Tcp/TcpTransport.cs`、`AsyncSpanHelper.cs` 及 `Write/ReadFrameHeaderSync`、`TcpServerListener`/`TcpClientTransport` 握手方法签名），彻底消除与消息层 `Messaging.MessageHeader` 的同名混淆。
  - `IPulseRPCSerializer.cs`（内实为 `ISerializerProvider`/`ISerializer`/`PulseRPCSerializerProvider`，并无 `IPulseRPCSerializer`）→ 文件更名为 **`PulseRPCSerializerProvider.cs`**（内容/命名空间不变，无代码引用受影响）。
- **P2-12 收敛营销式前缀**（服务端编译 0 错误验证）：
  - `Enhanced*`/`Optimized*`/`ThreeTier*` 前缀类型已随 P1-5/P1-6/P1-9 删除。
  - 3 个活跃的 `internal sealed` 引擎类去前缀并同步重命名文件：`HighPerformanceMessageEngine`→`MessageEngine`、`HighPerformanceMessageDispatcher`→`MessageDispatcher`、`HighPerformanceResponseProcessor`→`ResponseProcessor`（均 `internal`，无公共 API 影响）。
  - 删除整段被注释掉的死类文件 `HighPerformanceNetworkProcessor.cs`（其 `INetworkProcessor`/`IncomingDataPacket`/`ConnectionParsingState`/本命名空间 `MessageParsedEventArgs`/`NetworkProcessorOptions` 均仅被该注释类引用，活跃路径用的是 `PulseRPC.Server.Transport.MessageParsedEventArgs`）。
- **P2-13（其一）服务端 Deadline 强制**（三项目编译 0 错误验证）：
  - 消息头新增相对超时字段 `MessageHeader.TimeoutMs`（毫秒，`0`=不设置；MemoryPackOrder=9 尾部追加，wire 向后兼容）。
  - 客户端 `InvokeRawAsync` 将 `DefaultTimeout` 作为相对 Deadline 写入头部。
  - 服务端 `TieredMessageProcessor.ProcessMessageSlot` 强制执行：以**服务端本地单调时钟**（`slot.EnqueueTime` = 收包入队 `Stopwatch.GetTimestamp()`）计算，规避跨机器时钟不同步；派发前若已超期则**直接卸载**（不执行 handler），否则对 handler 的 `CancellationToken` 执行 `CancelAfter(剩余时间)`。仅取消（不强制中止线程），故对不响应取消的 handler 安全无副作用。

### 待办 / 需评审后进行

- **P1-8 服务端收包去 `MessagePacketHolder.Payload.ToArray()`**（高风险，`dotnet build` 无法验证正确性）：
  - 已核实：该 `ToArray` 是**唯一且必要**的所有权拷贝。payload 是复用接收缓冲 `_receiveBuffer` 上的 span，经 `MessagePacketHolder` → `MessageSlot.Payload = ...AsMemory()` 进入 `TieredMessageProcessor`，随后在**工作线程**上经 L1→L2 批处理、派发、背压丢弃、重试、错误等**多分支**流转，**没有唯一确定的释放点**；当前链路无冗余拷贝可删。
  - 去除它必须引入「移交池化 buffer + 引用计数 + 确定性归还」的所有权协议，跨 TCP/KCP/client/server 接收热路径。错误的引用计数会重新引入我们刚在 P0-2 修复的 UAF 或造成泄漏，且此类缺陷 build 无法发现、需运行期/压力测试。与你设定的「正确性优先(P0)」冲突，故**不盲目上线**，留待专项评审 + 集成测试。
  - 建议实现路径：`TransportDataEventArgs` 携带 `IMemoryOwner<byte>`（或自研引用计数缓冲）；接收循环每帧从池租借、读满后移交所有权；`MessagePacketHolder` 持有该 owner 并实现 `IDisposable`；处理管道在**每条分支的终点**（完成/丢弃/错误/背压）统一 `Dispose`。需为「丢弃/重试/背压」分支补齐归还路径，并加并发压力测试断言无泄漏、无 UAF。
- **P1-10 传输实现从 Abstractions 下沉到实现层**（已完成，整解决方案编译 0 错误验证；**破坏性打包变更**，发布时需版本协调）：
  - 新建 `PulseRPC.Transport` 项目（多目标 `netstandard2.1;net10.0`，`ProjectReference` → `PulseRPC.Abstractions`，单向依赖），加入 `PulseRPC.sln`。
  - 用 `git mv` 迁移具体实现（保留历史、**命名空间不变**，故消费方 `using` 无需改动）：`Tcp/TcpTransport.cs`、`Kcp/{KcpTransport,KcpCore,KcpSegment}.cs`、`AsyncSpanHelper.cs`、`LargePacketHandler.cs`、`NetworkBufferPool.cs`、`Batching/BatchedTransport.cs`；顺带删除已死的 `LockFreeRingBuffer.cs`。
  - `Client`/`Server` 各加一条 `ProjectReference` → `PulseRPC.Transport`；`TcpClientTransport`/`TcpServerTransport`/`Kcp*` 仍是继承同一份共享基类的**薄子类**（无重复实现）。
  - `Abstractions` 现只保留传输**抽象**（`ITransport`/`TransportOptions`/`ProtocolConstants`/`TransportContext`/批处理接口与选项等）；已核实无任何非传输代码引用被迁走的实现类型，故 `Abstractions` 仍可独立编译（0 错误）。
  - 打包影响：`TcpTransport` 等已从 `PulseRPC.Abstractions` 包移入新的 `PulseRPC.Transport` 包。仅引用 Abstractions 包并直接使用这些实现的下游需增加对 `PulseRPC.Transport` 的引用——发布时需相应的版本号/迁移说明。
- **P2-13（其二）Streaming 路线图**（设计，未编码）：
  - 阶段 1：在 `MethodType`（已具备 `ClientStreaming`/`ServerStreaming`/`DuplexStreaming`）之上定义 wire 语义——复用现有帧，新增流控帧类型（`StreamData`/`StreamEnd`/`StreamError`）与 `StreamId`（可复用 `MessageId` + `SequenceNumber`）。
  - 阶段 2：服务端以 `System.Threading.Channels` 承接 `IAsyncEnumerable<T>` 入/出；沿用现有 Actor 串行邮箱与背压。
  - 阶段 3：源生成器为 `IAsyncEnumerable<T>` 参数/返回值生成 `ServiceProxy`/`ReceiverProxy`；客户端 `TransportChannel` 增加流式发送/接收 API。
  - 阶段 4：与 Deadline/Cancel 统一——流的取消即向对端发送 `StreamError(Canceled)`。
- **P2-13（其三）Cancel 端到端**（设计，未编码；依赖 P1-8 之外的在途请求登记）：
  - 客户端：本地 `CancellationToken` 触发时，除结束本地等待外，向服务端发送 `MessageType.Cancel`（body 为待取消 `MessageId`）。
  - 服务端：维护 `ConcurrentDictionary<Guid, CancellationTokenSource>` 在途请求登记表——`ProcessMessageSlot` 现已为每请求创建 `CancellationTokenSource`（Deadline 用），只需在派发前登记 `MessageId→cts`、完成时注销；`ServerTransportChannel.ProcessReceivedData` 收到 `Cancel` 帧时查表 `Cancel()`。
  - 风险点：登记表需跨 `ServerTransportChannel`（收 Cancel 帧）与每连接 `TieredMessageProcessor`（持 cts）共享，涉及对象生命周期与并发清理，建议随 P1-8 的所有权重构一并评审落地。

> 说明 1：单帧 framing 变更位于客户端/服务端共享的 `TcpTransport` 基类，两端同步生效、wire 互相兼容；但与旧版本（分片格式）不互通。分片相关接收代码（`ProcessChunkedMessageAsync`/`ChunkHeader`）暂保留但已不产生 chunk 帧，属死路径，随后续清理移除。
>
> 说明 2：Deadline 采用**相对时长 + 服务端本地单调时钟**（对齐 gRPC `grpc-timeout` 语义），刻意不使用绝对时间戳，以规避客户端/服务端时钟不同步。旧客户端不带该字段（反序列化为 `0`）→ 服务端视为「不设置 Deadline」，行为与之前一致。
