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
