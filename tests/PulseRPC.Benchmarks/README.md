# PulseRPC 基准测试

这个项目包含 PulseRPC 的性能基准测试，主要关注内存优化效果的验证。

## 基准测试内容

### MemoryOptimizationBenchmark

验证优化后的大包分片处理器相比原始实现的性能和内存使用改进。

**测试场景**：

- 包大小：10MB
- 分片大小：64KB
- 测试迭代：100次

## 运行方式

### 简单内存对比测试

```bash
dotnet run
```

### 完整 BenchmarkDotNet 基准测试

```bash
dotnet run -c Release -- --benchmark
```

## 最新测试结果 (Release 模式)

| 方法 | 平均耗时 | 内存分配 | 相对性能 | 内存使用比 |
|------|----------|----------|----------|------------|
| 原始实现 | 1.571s | 2000.56 MB | 基准 (1.00) | 基准 (1.00) |
| 优化实现 | 1.489s | 1000.54 MB | 0.96 (提升 5.2%) | 0.50 (减少 50%) |

### 优化效果

- ✅ **性能提升**：约 5.2%
- ✅ **内存使用减少**：约 50%
- ✅ **GC 压力降低**：Gen1 和 Gen2 垃圾回收显著减少

## 输出文件

基准测试完成后会生成以下报告文件：

- `BenchmarkDotNet.Artifacts/results/PulseRPC.Benchmarks.MemoryOptimizationBenchmark-report.csv`
- `BenchmarkDotNet.Artifacts/results/PulseRPC.Benchmarks.MemoryOptimizationBenchmark-report-github.md`
- `BenchmarkDotNet.Artifacts/results/PulseRPC.Benchmarks.MemoryOptimizationBenchmark-report.html`
