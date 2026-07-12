# 性能指南

性能优化应以真实调用路径为准。先用基准工具复现瓶颈，再修改传输、序列化或调度实现。

## 优先级

1. 明确场景：延迟、吞吐、广播、Actor 调度、序列化或连接数。
2. 运行基准测试并保存参数。
3. 检查分配、锁竞争、通道背压和序列化成本。
4. 修改后用同样参数对比。
5. 把结论写入 PR 或变更记录。

## 工具

```bash
cd perf/BenchmarkApp
dotnet run -c Release --project PulseRPC.Benchmark -- server --tcp-port 12345
dotnet run -c Release --project PulseRPC.Benchmark -- client latency --port 12345 --iterations 100000 --warmup 100
dotnet run -c Release --project PulseRPC.Benchmark -- client throughput --port 12345
```

## 实现建议

- 保持消息 DTO 简单，避免无意义的大对象图。
- 避免在热路径新增反射、LINQ 大量分配或同步阻塞。
- 网络缓冲优先使用现有池化组件。
- 对大包、压缩、批处理等能力先确认协议和验证成本。

## 服务端 worker shard

服务端消息执行资源由两个 `PulseServerOptions` 属性控制：

```csharp
services.AddPulseServer(options =>
{
    options.AddTcp(7000);
    options.MessageWorkerShardCount = Math.Max(1, Environment.ProcessorCount);
    options.MessageQueueCapacityPerShard = 1024;
});
```

- `MessageWorkerShardCount` 是固定 worker 数。增加它可以提高独立消息的并发度，也会增加长期 worker 和队列数量。
- `MessageQueueCapacityPerShard` 是每个 shard 的排队上限。增加容量只能吸收短时突发，也会提高最坏情况下的排队延迟和内存占用。
- 连接在其生命周期内固定绑定一个 shard；长时间运行的调用会阻塞同 shard 的后续消息。应优先缩短 handler 临界路径，再根据同一 workload 调整 shard 数。
- 队列满时立即拒绝新消息。不要依赖历史的 priority sleep/retry、adaptive batching 或 L1/L2/L3 配置规避背压。

调优时同时记录 `message-engine.shard` 的 capacity、depth、saturation、高水位和 rejected enqueue，并在同一机器上比较吞吐、延迟与分配。只提高容量而不处理持续过载，会把拒绝转换成更长的尾延迟。

## 相关文档

- [传输模型](../concepts/transport-model.md)
- [测试指南](testing.md)
- [历史网络优化计划](../archive/roadmaps/260703-network-optimization-plan.md)
