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

## 相关文档

- [传输模型](../concepts/transport-model.md)
- [测试指南](testing.md)
- [历史网络优化计划](../archive/roadmaps/260703-network-optimization-plan.md)

