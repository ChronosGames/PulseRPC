# PulseRPC BenchmarkApp 完整使用指南

## 📋 目录

- [概述](#概述)
- [快速开始](#快速开始)
- [架构设计](#架构设计)
- [配置指南](#配置指南)
- [测试场景](#测试场景)
- [监控和指标](#监控和指标)
- [报告生成](#报告生成)
- [高级功能](#高级功能)
- [故障排除](#故障排除)
- [最佳实践](#最佳实践)

## 概述

PulseRPC BenchmarkApp 是一个企业级性能基准测试框架，专为评估和优化 PulseRPC 框架的性能而设计。它提供了完整的端到端性能测试能力，包括延迟测试、吞吐量测试、稳定性测试和资源监控。

### 核心特性

- ✅ **完整的测试生态系统**：从单机测试到分布式压力测试
- ✅ **实时监控**：实时性能指标收集和可视化显示
- ✅ **多种测试场景**：内置多种标准测试场景，支持自定义扩展
- ✅ **详细的性能报告**：HTML、JSON、CSV 多格式报告生成
- ✅ **高度可配置**：支持命令行、配置文件等多种配置方式
- ✅ **生产级质量**：经过充分测试，适用于生产环境性能评估

### 系统要求

- **.NET 8.0** 或更高版本
- **内存**: 最少 512MB，推荐 2GB+
- **CPU**: 支持多核处理器，推荐 4 核以上
- **网络**: 低延迟网络环境（用于准确的延迟测试）
- **磁盘空间**: 至少 100MB 可用空间

## 快速开始

### 1. 环境准备

```bash
# 确认 .NET 版本
dotnet --version

# 克隆项目并进入目录
cd perf/BenchmarkApp

# 恢复依赖
dotnet restore
```

### 2. 基础使用流程

#### 步骤 1: 启动服务端

```bash
# 使用默认配置启动
dotnet run --project PulseRPC.Benchmark.Server

# 自定义端口启动
dotnet run --project PulseRPC.Benchmark.Server -- start --port 9090

# 使用配置文件启动
dotnet run --project PulseRPC.Benchmark.Server -- start --config configs/server-config.json
```

#### 步骤 2: 运行基准测试

```bash
# 基础延迟测试
dotnet run --project PulseRPC.Benchmark.Client -- run \
  --server localhost:8080 \
  --scenario ping-pong \
  --duration 30 \
  --connections 5

# 高负载测试
dotnet run --project PulseRPC.Benchmark.Client -- run \
  --server localhost:8080 \
  --scenario throughput \
  --duration 60 \
  --connections 50 \
  --rate 1000 \
  --warmup 10 \
  --verbose
```

#### 步骤 3: 生成报告

```bash
# 生成 HTML 报告
dotnet run --project PulseRPC.Benchmark.Client -- generate-report \
  --input results/test-results-*.json \
  --format html \
  --output reports/benchmark-report.html \
  --include-charts

# 导出 CSV 数据
dotnet run --project PulseRPC.Benchmark.Client -- generate-report \
  --input results/test-results-*.json \
  --format csv \
  --output data/benchmark-data.csv
```

## 架构设计

### 系统组件

```
┌─────────────────────────────────────────────────────────────┐
│                    BenchmarkApp 架构                        │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌─────────────────┐                ┌─────────────────┐     │
│  │ Benchmark.Client │◄──── RPC ────►│ Benchmark.Server │     │
│  └─────────────────┘                └─────────────────┘     │
│           │                                   │              │
│           ▼                                   ▼              │
│  ┌─────────────────┐                ┌─────────────────┐     │
│  │   测试执行引擎    │                │   RPC 服务实现   │     │
│  │   进度显示       │                │   健康检查      │     │
│  │   结果收集       │                │   指标监控      │     │
│  └─────────────────┘                └─────────────────┘     │
│           │                                                 │
│           ▼                                                 │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │                共享组件层                                │ │
│  │  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐       │ │
│  │  │ 核心接口     │ │ 传输层      │ │ 测试场景     │       │ │
│  │  │ 数据模型     │ │ 连接管理    │ │ 指标收集     │       │ │
│  │  └─────────────┘ └─────────────┘ └─────────────┘       │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### 核心模块

1. **PulseRPC.Benchmark.Client**: 测试客户端
   - 测试执行引擎
   - 进度显示和监控
   - 结果收集和报告生成

2. **PulseRPC.Benchmark.Server**: 测试服务端
   - RPC 服务实现
   - 健康检查和监控
   - 性能指标收集

3. **PulseRPC.Benchmark.Core**: 核心框架
   - 抽象接口定义
   - 传输层实现
   - 连接管理

4. **PulseRPC.Benchmark.Scenarios**: 测试场景
   - 内置测试场景
   - 场景执行框架
   - 自定义场景支持

5. **PulseRPC.Benchmark.Metrics**: 指标系统
   - 实时指标收集
   - 数据聚合和分析
   - 多格式导出

## 配置指南

### 服务端配置

**server-config.json**
```json
{
  "port": 8080,
  "metricsPort": 9090,
  "maxConnections": 1000,
  "enableCompression": true,
  "bufferSize": 65536,
  "logging": {
    "level": "Information",
    "enableFileLogging": true,
    "logDirectory": "./logs"
  },
  "healthCheck": {
    "enabled": true,
    "intervalSeconds": 30
  },
  "performance": {
    "enableCpuMonitoring": true,
    "enableMemoryMonitoring": true,
    "enableNetworkMonitoring": true
  }
}
```

### 客户端配置

**client-config.json**
```json
{
  "serverAddress": "localhost:8080",
  "connectionTimeoutMs": 5000,
  "requestTimeoutMs": 30000,
  "defaultScenario": "ping-pong",
  "enableCompression": true,
  "bufferSize": 65536,
  "retryPolicy": {
    "maxRetries": 3,
    "retryDelayMs": 1000,
    "exponentialBackoff": true
  },
  "reporting": {
    "outputDirectory": "./results",
    "autoGenerateReports": true,
    "includeRawData": false
  }
}
```

### 测试场景配置

**benchmark-scenarios.json**
```json
{
  "scenarios": [
    {
      "name": "ping-pong",
      "description": "基础请求-响应延迟测试",
      "messageSize": 1024,
      "requestPattern": "ping-pong",
      "defaultDuration": 30,
      "defaultConnections": 10,
      "warmupSeconds": 5,
      "cooldownSeconds": 2
    },
    {
      "name": "throughput",
      "description": "高吞吐量单向传输测试",
      "messageSize": 4096,
      "requestPattern": "one-way",
      "defaultDuration": 60,
      "defaultConnections": 50,
      "targetQPS": 10000,
      "rampUpSeconds": 10
    },
    {
      "name": "latency-analysis",
      "description": "详细延迟分析测试",
      "messageSize": 512,
      "requestPattern": "ping-pong",
      "defaultDuration": 120,
      "defaultConnections": 5,
      "collectDetailedLatency": true,
      "latencyThresholds": [1, 5, 10, 50, 100]
    }
  ]
}
```

## 测试场景

### 内置测试场景

#### 1. PingPong 延迟测试
- **用途**: 测量基础的请求-响应延迟
- **特点**: 低并发，高精度延迟测量
- **适用场景**: 延迟敏感的应用评估

```bash
dotnet run --project PulseRPC.Benchmark.Client -- run \
  --scenario ping-pong \
  --connections 5 \
  --duration 60 \
  --message-size 1024
```

#### 2. Throughput 吞吐量测试
- **用途**: 测量系统的最大吞吐能力
- **特点**: 高并发，大量数据传输
- **适用场景**: 高负载系统性能评估

```bash
dotnet run --project PulseRPC.Benchmark.Client -- run \
  --scenario throughput \
  --connections 100 \
  --duration 120 \
  --rate 10000 \
  --message-size 4096
```

#### 3. LatencyAnalysis 延迟分析测试
- **用途**: 详细的延迟分布分析
- **特点**: 收集详细的延迟统计数据
- **适用场景**: 性能优化和问题诊断

```bash
dotnet run --project PulseRPC.Benchmark.Client -- run \
  --scenario latency-analysis \
  --connections 20 \
  --duration 300 \
  --detailed-latency
```

### 自定义测试场景

#### 创建自定义场景

```csharp
public class CustomStressTestScenario : BaseBenchmarkScenario
{
    public override string Name => "custom-stress-test";
    public override string Description => "自定义压力测试场景";

    public override async Task<BenchmarkResult> ExecuteAsync(
        IBenchmarkTransport transport,
        BenchmarkConfiguration config,
        CancellationToken cancellationToken)
    {
        var results = new List<RequestResult>();
        var stopwatch = Stopwatch.StartNew();

        // 渐进式负载增加
        for (int phase = 1; phase <= 5; phase++)
        {
            var phaseQps = config.RequestRate * phase / 5;
            var phaseResults = await ExecutePhase(transport, phaseQps, TimeSpan.FromMinutes(2));
            results.AddRange(phaseResults);
        }

        stopwatch.Stop();

        return new BenchmarkResult
        {
            ScenarioName = Name,
            TotalDuration = stopwatch.Elapsed,
            Results = results,
            Metrics = CalculateMetrics(results)
        };
    }

    private async Task<List<RequestResult>> ExecutePhase(
        IBenchmarkTransport transport,
        int targetQps,
        TimeSpan duration)
    {
        // 阶段性测试实现
        // ...
        return new List<RequestResult>();
    }
}
```

#### 注册和使用自定义场景

```csharp
// 在启动时注册
services.AddBenchmarkScenario<CustomStressTestScenario>();

// 运行自定义场景
dotnet run --project PulseRPC.Benchmark.Client -- run \
  --scenario custom-stress-test \
  --config custom-test-config.json
```

## 监控和指标

### 实时监控指标

BenchmarkApp 收集以下关键性能指标：

#### 基础性能指标
- **延迟指标**
  - 平均延迟 (Average Latency)
  - 中位数延迟 (P50)
  - 95百分位延迟 (P95)
  - 99百分位延迟 (P99)
  - 99.9百分位延迟 (P99.9)

- **吞吐量指标**
  - 每秒请求数 (QPS)
  - 每秒数据传输量 (Throughput)
  - 每秒成功请求数
  - 每秒失败请求数

- **可靠性指标**
  - 成功率 (Success Rate)
  - 错误率 (Error Rate)
  - 超时率 (Timeout Rate)
  - 连接失败率

#### 系统资源指标
- **CPU 使用率**
  - 总体 CPU 使用率
  - 用户态 CPU 时间
  - 内核态 CPU 时间

- **内存使用**
  - 工作集内存
  - 私有内存
  - 托管堆内存
  - 垃圾回收统计

- **网络指标**
  - 网络带宽使用
  - 网络包数量
  - 网络错误计数

### 指标可视化

#### 控制台实时显示

```
🚀 PulseRPC Benchmark Test: ping-pong
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

▶️ 执行测试中                                          [00:01:23 / 00:02:00]

📊 实时统计:
   总请求数: 12,347
   成功请求: 12,298 (99.60%)
   失败请求: 49 (0.40%)
   当前QPS: 1,247.3

⚡ 延迟统计:
   平均延迟: 8.2 ms
   P95延迟: 15.7 ms
   P99延迟: 23.4 ms

🔗 连接状态:
   活跃连接: 10
   连接池状态: 健康

▓ ████████████████████████████████████████░░░░░░░░░  83.5%

💾 系统资源:
   CPU: 23.5%    内存: 156 MB    网络: 45.2 MB/s
```

#### Web 监控界面

启动服务端后，可通过 `http://localhost:9090/metrics` 访问实时监控界面，查看：
- 实时性能图表
- 历史趋势分析
- 系统资源监控
- 错误分析和告警

## 报告生成

### HTML 可视化报告

HTML 报告提供完整的可视化分析，包括：

#### 执行摘要
- 测试配置概览
- 总体性能指标
- 测试结果汇总

#### 性能图表
- 延迟分布直方图
- QPS 时间序列图
- 错误率趋势图
- 资源使用图表

#### 详细分析
- 百分位数详细分析
- 错误类型分类统计
- 时间窗口性能分析
- 性能对比和建议

```bash
# 生成详细的 HTML 报告
dotnet run --project PulseRPC.Benchmark.Client -- generate-report \
  --input results/test-results-20241127-143022.json \
  --format html \
  --template detailed \
  --output reports/detailed-performance-report.html \
  --include-charts \
  --title "PulseRPC 生产环境性能测试报告"
```

### JSON 数据报告

JSON 格式适用于程序化分析和集成：

```json
{
  "reportMetadata": {
    "generatedAt": "2024-11-27T14:30:22.123Z",
    "version": "1.0.0",
    "testDuration": "00:02:00.000",
    "scenarioName": "ping-pong"
  },
  "summary": {
    "totalRequests": 12000,
    "successfulRequests": 11970,
    "failedRequests": 30,
    "successRate": 0.9975,
    "averageQPS": 100.5,
    "averageLatencyMs": 8.23
  },
  "latencyMetrics": {
    "p50": 7.2,
    "p95": 15.8,
    "p99": 24.6,
    "p999": 45.3,
    "max": 67.8,
    "min": 2.1
  },
  "timelineAnalysis": [
    {
      "windowStart": "2024-11-27T14:28:22.123Z",
      "windowEnd": "2024-11-27T14:28:32.123Z",
      "requestCount": 1000,
      "averageLatency": 8.1,
      "qps": 100.0
    }
  ],
  "errorAnalysis": {
    "timeoutErrors": 15,
    "connectionErrors": 10,
    "protocolErrors": 5
  }
}
```

### CSV 数据导出

CSV 格式便于在 Excel 或其他分析工具中处理：

```csv
Timestamp,RequestId,Success,ResponseTimeMs,ErrorType,MessageSize
2024-11-27 14:28:22.123,req_001,true,8.23,,1024
2024-11-27 14:28:22.125,req_002,true,7.45,,1024
2024-11-27 14:28:22.127,req_003,false,,timeout,1024
```

## 高级功能

### 分布式测试

支持多客户端协调测试：

```bash
# 客户端 1
dotnet run --project PulseRPC.Benchmark.Client -- run \
  --server remote-server:8080 \
  --scenario distributed-load \
  --client-id client-1 \
  --total-clients 5 \
  --coordination-endpoint http://coordinator:8090

# 客户端 2-5
# 类似配置，修改 client-id
```

### 负载模式配置

#### 恒定负载模式
```json
{
  "loadPattern": {
    "type": "constant",
    "targetQPS": 1000,
    "duration": "00:05:00"
  }
}
```

#### 阶梯负载模式
```json
{
  "loadPattern": {
    "type": "step",
    "steps": [
      {"qps": 100, "duration": "00:01:00"},
      {"qps": 500, "duration": "00:02:00"},
      {"qps": 1000, "duration": "00:03:00"}
    ]
  }
}
```

#### 突发负载模式
```json
{
  "loadPattern": {
    "type": "burst",
    "baseQPS": 100,
    "burstQPS": 1000,
    "burstDuration": "00:00:30",
    "burstInterval": "00:02:00"
  }
}
```

### 自定义指标收集

```csharp
public class CustomMetricsCollector : IMetricsCollector
{
    public void RecordCustomMetric(string name, double value, Dictionary<string, string> tags)
    {
        // 记录业务相关的自定义指标
        // 例如：业务逻辑执行时间、缓存命中率等
    }

    public void RecordBusinessEvent(string eventType, object eventData)
    {
        // 记录业务事件
        // 例如：用户登录、订单创建等
    }
}
```

### 性能基线管理

#### 设置性能基线
```bash
dotnet run --project PulseRPC.Benchmark.Client -- baseline \
  --set \
  --name "v1.0-baseline" \
  --config baseline-config.json
```

#### 性能回归检测
```bash
dotnet run --project PulseRPC.Benchmark.Client -- baseline \
  --compare \
  --baseline "v1.0-baseline" \
  --current-results results/current-test.json \
  --threshold 5 # 5% 性能下降阈值
```

## 故障排除

### 常见问题

#### 1. 连接超时错误
**症状**: 大量连接超时，测试无法正常进行
```
Error: Connection timeout after 5000ms
```

**解决方案**:
```bash
# 检查服务端是否正常运行
netstat -an | grep 8080

# 增加连接超时时间
dotnet run --project PulseRPC.Benchmark.Client -- run \
  --connection-timeout 15000

# 检查防火墙设置
# 验证网络连通性
ping target-server
```

#### 2. 内存不足错误
**症状**: 测试过程中出现 OutOfMemoryException
```
System.OutOfMemoryException: Exception of type 'System.OutOfMemoryException' was thrown.
```

**解决方案**:
```bash
# 减少并发连接数
--connections 20  # 从 100 降到 20

# 减少消息大小
--message-size 1024  # 从 4096 降到 1024

# 启用 GC 监控
--enable-gc-monitoring

# 设置内存限制
--max-memory-mb 1024
```

#### 3. 高延迟问题
**症状**: 延迟远高于预期
```
Average latency: 250ms (expected < 10ms)
```

**诊断步骤**:
```bash
# 启用详细日志
--verbose --log-level Debug

# 检查网络延迟
ping -c 10 target-server

# 分析延迟分布
--detailed-latency --latency-histogram

# 检查系统资源使用
top
iotop
```

#### 4. 测试结果不一致
**症状**: 多次运行测试结果差异很大

**解决方案**:
```bash
# 增加预热时间
--warmup 30

# 延长测试时间
--duration 300

# 检查系统负载
# 关闭其他应用程序
# 固定 CPU 频率（生产测试）

# 多次运行取平均值
for i in {1..5}; do
  dotnet run --project PulseRPC.Benchmark.Client -- run \
    --scenario ping-pong --output results/run-$i.json
done
```

### 调试和日志

#### 启用详细日志
```bash
dotnet run --project PulseRPC.Benchmark.Client -- run \
  --log-level Debug \
  --log-file logs/benchmark-debug.log \
  --verbose
```

#### 日志级别说明
- **Trace**: 最详细的调试信息
- **Debug**: 调试信息，包括请求/响应详情
- **Information**: 一般信息，包括测试进度
- **Warning**: 警告信息，可能影响测试结果
- **Error**: 错误信息，需要关注的问题
- **Critical**: 严重错误，导致测试失败

#### 性能分析工具集成

##### .NET 性能分析
```bash
# 使用 dotnet-trace 收集性能数据
dotnet-trace collect --process-id <pid> --providers Microsoft-DotNETCore-SampleProfiler

# 使用 dotnet-dump 收集内存转储
dotnet-dump collect --process-id <pid>

# 使用 dotnet-counters 监控实时指标
dotnet-counters monitor --process-id <pid>
```

##### 系统性能监控
```bash
# Linux 系统监控
htop              # CPU 和内存监控
iotop             # 磁盘 I/O 监控
nload             # 网络流量监控
ss -tuln          # 网络连接状态

# Windows 系统监控
perfmon           # 性能监视器
resmon            # 资源监视器
netstat -an       # 网络连接状态
```

## 最佳实践

### 测试环境准备

#### 1. 硬件环境
- **CPU**: 使用至少 4 核处理器，测试高并发场景时推荐 8 核或更多
- **内存**: 最少 4GB，推荐 8GB 或更多
- **网络**: 使用低延迟、高带宽网络，推荐千兆以太网
- **存储**: 使用 SSD 存储，确保 I/O 性能不成为瓶颈

#### 2. 系统配置
```bash
# Linux 系统优化
# 增加文件描述符限制
ulimit -n 65536

# 调整 TCP 参数
echo 'net.core.rmem_max = 16777216' >> /etc/sysctl.conf
echo 'net.core.wmem_max = 16777216' >> /etc/sysctl.conf
echo 'net.ipv4.tcp_rmem = 4096 65536 16777216' >> /etc/sysctl.conf
echo 'net.ipv4.tcp_wmem = 4096 65536 16777216' >> /etc/sysctl.conf
sysctl -p

# 禁用不必要的服务
systemctl stop unnecessary-services
```

#### 3. .NET 运行时优化
```bash
# 设置环境变量
export DOTNET_gcServer=1                    # 启用服务器 GC
export DOTNET_GCRetainVM=1                  # 保留虚拟内存
export DOTNET_ThreadPool_ForceMinWorkerThreads=100   # 最小工作线程数
export DOTNET_ThreadPool_ForceMaxWorkerThreads=1000  # 最大工作线程数
```

### 测试策略

#### 1. 渐进式测试
```bash
# 阶段1: 基础功能验证
dotnet run --project PulseRPC.Benchmark.Client -- run \
  --scenario ping-pong --connections 1 --duration 30

# 阶段2: 低负载测试
dotnet run --project PulseRPC.Benchmark.Client -- run \
  --scenario ping-pong --connections 10 --duration 60

# 阶段3: 中等负载测试
dotnet run --project PulseRPC.Benchmark.Client -- run \
  --scenario throughput --connections 50 --duration 120

# 阶段4: 高负载测试
dotnet run --project PulseRPC.Benchmark.Client -- run \
  --scenario throughput --connections 200 --duration 300
```

#### 2. 多维度测试矩阵

| 场景 | 连接数 | 消息大小 | 持续时间 | 目标 |
|------|--------|----------|----------|------|
| 延迟基准 | 1-5 | 512B-2KB | 60s | 测量基础延迟 |
| 低并发 | 10-20 | 1KB-4KB | 120s | 验证稳定性 |
| 中并发 | 50-100 | 2KB-8KB | 300s | 找到性能拐点 |
| 高并发 | 200-500 | 4KB-16KB | 600s | 压力测试 |
| 极限测试 | 1000+ | 16KB+ | 1800s | 找到系统极限 |

#### 3. 性能回归测试
```bash
# 建立基线
./scripts/create-baseline.sh v1.0

# 每次发布前运行回归测试
./scripts/regression-test.sh \
  --baseline v1.0 \
  --scenarios "ping-pong,throughput" \
  --threshold 5%

# 性能优化验证
./scripts/performance-improvement-test.sh \
  --before-optimization baseline-before \
  --after-optimization current-test \
  --expected-improvement 10%
```

### 结果分析

#### 1. 关键指标解读

**延迟指标分析**:
- P50 < 10ms: 优秀
- P95 < 50ms: 良好
- P99 < 100ms: 可接受
- P99.9 < 500ms: 需要优化

**吞吐量指标分析**:
- QPS > 10,000: 高性能
- QPS 5,000-10,000: 中等性能
- QPS < 5,000: 需要优化

**稳定性指标分析**:
- 成功率 > 99.9%: 优秀
- 成功率 > 99%: 良好
- 成功率 < 99%: 需要改进

#### 2. 性能趋势分析
```bash
# 生成趋势对比报告
dotnet run --project PulseRPC.Benchmark.Client -- trend-analysis \
  --results-pattern "results/daily-test-*.json" \
  --time-range "last-30-days" \
  --output reports/performance-trend.html
```

#### 3. 瓶颈识别
- **CPU 瓶颈**: CPU 使用率 > 80%，延迟随并发线性增长
- **内存瓶颈**: 内存使用率 > 90%，GC 频繁触发
- **网络瓶颈**: 网络带宽利用率 > 80%，丢包率上升
- **I/O 瓶颈**: 磁盘 I/O 等待时间 > 100ms

### 持续集成集成

#### 1. CI/CD 流水线集成
```yaml
# .github/workflows/performance-test.yml
name: Performance Testing
on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  performance-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'

      - name: Run Performance Tests
        run: |
          cd perf/BenchmarkApp
          ./scripts/ci-performance-test.sh

      - name: Upload Results
        uses: actions/upload-artifact@v3
        with:
          name: performance-results
          path: perf/BenchmarkApp/results/

      - name: Performance Regression Check
        run: |
          ./scripts/check-performance-regression.sh \
            --threshold 5% \
            --baseline-branch main
```

#### 2. 自动化部署脚本
```bash
# scripts/deploy-and-test.sh
#!/bin/bash

# 部署应用
./deploy.ps1 -Target All -Configuration Release -Environment Production

# 等待服务启动
sleep 30

# 运行冒烟测试
dotnet run --project PulseRPC.Benchmark.Client -- run \
  --scenario ping-pong \
  --connections 5 \
  --duration 60 \
  --server production-server:8080

# 检查测试结果
if [ $? -eq 0 ]; then
  echo "✅ 部署成功，性能测试通过"
else
  echo "❌ 性能测试失败，回滚部署"
  ./rollback.sh
  exit 1
fi
```

### 监控和告警

#### 1. 性能指标监控
```bash
# Prometheus 指标暴露
dotnet run --project PulseRPC.Benchmark.Server -- start \
  --enable-prometheus \
  --prometheus-port 9091

# Grafana 仪表板配置
# 导入预配置的仪表板模板
```

#### 2. 自动化告警
```yaml
# alerting-rules.yml
groups:
  - name: pulserpc-performance
    rules:
      - alert: HighLatency
        expr: pulserpc_latency_p95 > 100
        for: 5m
        annotations:
          summary: "PulseRPC 延迟过高"
          description: "P95 延迟超过 100ms，当前值: {{ $value }}ms"

      - alert: LowThroughput
        expr: pulserpc_qps < 1000
        for: 5m
        annotations:
          summary: "PulseRPC 吞吐量过低"
          description: "QPS 低于 1000，当前值: {{ $value }}"
```

---

## 📞 支持和反馈

如果在使用 BenchmarkApp 过程中遇到问题，请通过以下方式寻求帮助：

- **GitHub Issues**: [提交问题](https://github.com/your-org/PulseRPC/issues)
- **讨论区**: [GitHub Discussions](https://github.com/your-org/PulseRPC/discussions)
- **文档**: [在线文档](https://docs.pulserpc.com)

---

**最后更新**: 2024-11-27
**版本**: v1.0.0
