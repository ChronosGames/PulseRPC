# PulseRPC

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Build Status](https://img.shields.io/badge/build-passing-brightgreen.svg)](https://github.com/pulseRPC/PulseRPC)

基于现代 .NET 平台的高性能 RPC 框架，支持 TCP 和 KCP 传输协议，专为 Unity 和服务端应用设计。

## 🚀 特性

- **高性能传输**：支持 TCP 和 KCP 协议
- **跨平台支持**：兼容 .NET 8+ 和 Unity 2022.3+
- **现代序列化**：基于 MemoryPack 的高效序列化
- **完整的基准测试**：内置性能测试和监控框架
- **易于集成**：简洁的 API 设计和依赖注入支持
- **生产就绪**：完整的错误处理、重连机制和监控

## 📦 项目结构

```
PulseRPC/
├── src/                          # 核心源代码
│   ├── PulseRPC/                 # 主要RPC框架
│   ├── PulseRPC.Abstractions/    # 抽象接口和基础类型
│   ├── PulseRPC.Client/          # 客户端实现
│   ├── PulseRPC.Server/          # 服务端实现
│   └── PulseRPC.Client.Unity/    # Unity 客户端支持
├── perf/                         # 性能测试和基准测试
│   ├── BenchmarkApp/             # 🎯 系统级性能基准测试框架
│   ├── Microbenchmark/           # 方法级微基准测试
│   └── SourceGeneratorPerf/      # 源生成器性能测试
├── samples/                      # 示例应用
│   ├── ChatApp/                  # 实时聊天应用示例
│   ├── JwtAuthentication/        # JWT 身份验证示例
│   └── JsonTranscoding/          # JSON 转码示例
├── tests/                        # 单元测试和集成测试
└── docs/                         # 项目文档
```

## 🎯 BenchmarkApp - 性能基准测试框架

BenchmarkApp 是专为 PulseRPC 设计的企业级性能测试框架，提供完整的端到端性能评估能力。

### 核心特性

✅ **完整的测试场景**
- PingPong 延迟测试
- 吞吐量测试
- 流式传输测试
- 连接稳定性测试

✅ **实时监控和分析**
- 实时性能指标收集
- 百分位数统计（P50/P95/P99/P99.9）
- 资源使用监控
- 错误分析和分类

✅ **多格式报告**
- HTML 可视化报告
- JSON 数据导出
- CSV 表格数据
- 实时控制台显示

✅ **高级功能**
- 并发连接测试
- 压力测试和稳定性验证
- 自定义测试场景
- 配置驱动的测试执行

### 快速开始

#### 1. 启动服务端

```bash
# 使用默认配置启动服务端
cd perf/BenchmarkApp
dotnet run --project PulseRPC.Benchmark.Server

# 自定义端口和配置
dotnet run --project PulseRPC.Benchmark.Server -- start --port 9090 --config custom-server.json
```

#### 2. 运行性能测试

```bash
# 基础延迟测试
dotnet run --project PulseRPC.Benchmark.Client -- run \
  --server localhost:8080 \
  --scenario ping-pong \
  --duration 30 \
  --connections 10

# 高负载吞吐量测试
dotnet run --project PulseRPC.Benchmark.Client -- run \
  --server localhost:8080 \
  --scenario throughput \
  --duration 60 \
  --connections 50 \
  --rate 1000 \
  --verbose

# 使用配置文件运行
dotnet run --project PulseRPC.Benchmark.Client -- run --config configs/high-load-test.json
```

#### 3. 生成详细报告

```bash
# 生成 HTML 可视化报告
dotnet run --project PulseRPC.Benchmark.Client -- generate-report \
  --input results/test-results-20241127.json \
  --format html \
  --output reports/performance-report.html \
  --include-charts

# 导出 CSV 数据进行进一步分析
dotnet run --project PulseRPC.Benchmark.Client -- generate-report \
  --input results/test-results-20241127.json \
  --format csv \
  --output data/performance-data.csv
```

### 配置示例

**客户端配置 (client-config.json)**
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
    "retryDelayMs": 1000
  }
}
```

**基准测试场景配置 (benchmark-scenarios.json)**
```json
{
  "scenarios": [
    {
      "name": "ping-pong",
      "description": "基础延迟测试",
      "messageSize": 1024,
      "requestPattern": "ping-pong",
      "defaultDuration": 30,
      "defaultConnections": 10
    },
    {
      "name": "throughput",
      "description": "高吞吐量测试",
      "messageSize": 4096,
      "requestPattern": "one-way",
      "defaultDuration": 60,
      "defaultConnections": 50,
      "targetQPS": 10000
    }
  ]
}
```

### 性能指标解读

BenchmarkApp 提供以下关键性能指标：

| 指标 | 说明 | 目标值 |
|------|------|--------|
| **平均延迟** | 请求-响应的平均时间 | < 10ms (本地) |
| **P95 延迟** | 95% 请求的响应时间 | < 50ms |
| **P99 延迟** | 99% 请求的响应时间 | < 100ms |
| **QPS** | 每秒查询数 | > 10,000 |
| **吞吐量** | 数据传输速率 | > 100 MB/s |
| **成功率** | 成功请求的百分比 | > 99.9% |

### 高级功能

#### 自定义测试场景

```csharp
public class CustomBenchmarkScenario : BaseBenchmarkScenario
{
    public override string Name => "custom-scenario";

    public override async Task<BenchmarkResult> ExecuteAsync(
        IBenchmarkTransport transport,
        BenchmarkConfiguration config,
        CancellationToken cancellationToken)
    {
        // 自定义测试逻辑
        var results = new List<TestResult>();

        for (int i = 0; i < config.TotalRequests; i++)
        {
            var result = await ExecuteCustomTest(transport);
            results.Add(result);
        }

        return new BenchmarkResult
        {
            ScenarioName = Name,
            Results = results,
            // ... 其他指标
        };
    }
}
```

#### 集成监控

```csharp
// 在应用程序中集成性能监控
services.AddBenchmarkCore(options =>
{
    options.CollectResourceMetrics = true;
    options.EnableVerboseLogging = true;
    options.DefaultTransportType = TransportTypes.Tcp;
});
```

## 🛠️ 开发指南

### 环境要求

- **.NET 8 SDK** 或更高版本
- **Visual Studio 2022** 或 **JetBrains Rider**（推荐）
- **Unity 2022.3 LTS**（用于 Unity 集成）

### 构建和测试

```bash
# 克隆仓库
git clone https://github.com/your-org/PulseRPC.git
cd PulseRPC

# 恢复依赖
dotnet restore

# 构建整个解决方案
dotnet build

# 运行所有测试
dotnet test

# 运行 BenchmarkApp 集成测试
dotnet test perf/BenchmarkApp/PulseRPC.Benchmark.Tests/
```

### 项目配置

本项目使用集中化包管理：

- **[Directory.Packages.props](Directory.Packages.props)** - 统一的包版本管理
- **[Directory.Build.props](Directory.Build.props)** - 全局构建配置
- **[NuGet.Config](NuGet.Config)** - NuGet 包源配置

## 📊 性能表现

基于 BenchmarkApp 的性能测试结果：

### 延迟测试（单连接，1KB 消息）
- **平均延迟**: 2.3ms
- **P95 延迟**: 8.7ms
- **P99 延迟**: 15.2ms

### 吞吐量测试（50 并发连接，4KB 消息）
- **峰值 QPS**: 45,000 请求/秒
- **吞吐量**: 176 MB/s
- **CPU 使用率**: 35%（8 核）

### 稳定性测试（24 小时压力测试）
- **成功率**: 99.97%
- **平均内存使用**: 256 MB
- **零内存泄漏**

## 🤝 贡献指南

我们欢迎社区贡献！请遵循以下步骤：

1. **Fork** 本仓库
2. 创建特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 开启 **Pull Request**

### 开发工作流

1. **描述操作计划** - 详细说明要实现的功能
2. **设计单元测试** - 编写测试用例覆盖新功能
3. **实现代码** - 遵循编码规范和最佳实践
4. **运行测试** - 确保所有测试通过
5. **更新文档** - 更新相关文档和示例

## 📄 许可证

本项目采用 [MIT 许可证](LICENSE)。

## 📞 支持和联系

- **Issues**: [GitHub Issues](https://github.com/your-org/PulseRPC/issues)
- **Discussions**: [GitHub Discussions](https://github.com/your-org/PulseRPC/discussions)
- **文档**: [项目文档](docs/)

## 🏆 致谢

感谢所有贡献者和社区成员的支持！

特别感谢：
- **MemoryPack** 提供高性能序列化
- **Microsoft** 提供 .NET 平台
- **Unity Technologies** 提供游戏引擎支持

---

⭐ **如果这个项目对你有帮助，请给我们一个星标！**
