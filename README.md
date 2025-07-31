# PulseRPC

[![NuGet](https://img.shields.io/nuget/v/PulseRPC.Client.svg)](https://www.nuget.org/packages/PulseRPC.Client/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/9.0)
[![Build Status](https://img.shields.io/badge/build-passing-brightgreen.svg)](https://github.com/pulseRPC/PulseRPC)

基于现代 .NET 平台的高性能 RPC 框架，支持 TCP 和 KCP 传输协议，专为 Unity 和服务端应用设计。

## 🚀 特性

### 核心功能
- **高性能传输**：支持 TCP 和 KCP 协议
- **跨平台支持**：兼容 .NET 9+ 和 Unity 2022.3+  
- **现代序列化**：基于 MemoryPack 的高效序列化
- **完整的基准测试**：内置性能测试和监控框架
- **易于集成**：简洁的 API 设计和依赖注入支持
- **生产就绪**：完整的错误处理、重连机制和监控

### 🧠 智能连接管理
- **按需连接**：根据需要自动创建和管理连接
- **智能路由**：支持轮询、一致性哈希、最少连接等多种负载均衡策略
- **多实例管理**：同一服务的多个实例连接和管理
- **广播与聚合**：跨多个服务实例的批量操作
- **自动回收**：空闲连接的智能清理和资源管理

### 🔧 基础设施
- **服务发现**：支持静态配置、Consul、Etcd、Kubernetes等多种服务发现方式
- **故障转移**：自动故障检测和实例切换
- **认证集成**：内置的认证令牌管理
- **代码生成**：基于 Source Generator 的客户端代码自动生成

## 📦 项目结构

```
PulseRPC/
├── src/                          # 核心源代码
│   ├── PulseRPC.Abstractions/    # 抽象接口和基础类型
│   ├── PulseRPC.Client/          # 客户端实现
│   ├── PulseRPC.Server/          # 服务端实现
│   ├── PulseRPC.Client.Unity/    # Unity 客户端支持
│   ├── PulseRPC.Shared/          # 共享组件（压缩、网络缓冲池等）
│   ├── PulseRPC.Infrastructure/  # 核心基础设施实现
│   └── PulseRPC.Infrastructure.*/# 特定基础设施实现（Consul、Etcd、K8s等）
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

### 快速开始

#### 1. 启动服务端

```bash
# 使用默认配置启动服务端
cd perf/BenchmarkApp
dotnet run --project PulseRPC.Benchmark.Server

# 自定义端口和配置
dotnet run --project PulseRPC.Benchmark.Server -- --port 9090
```

#### 2. 运行性能测试

```bash
# 基础延迟测试 (推荐参数)
dotnet run --project PulseRPC.Benchmark.Client -- run \
  --server localhost:8080 \
  --scenario ping-pong \
  --duration 30 \
  --connections 5 \
  --rate 50

# 高负载吞吐量测试
dotnet run --project PulseRPC.Benchmark.Client -- run \
  --server localhost:8080 \
  --scenario throughput \
  --duration 60 \
  --connections 10 \
  --rate 100 \
  --verbose
```

#### 3. 生成详细报告

```bash
# 生成 HTML 可视化报告
dotnet run --project PulseRPC.Benchmark.Client -- generate-report \
  --input results/test-results.json \
  --format html \
  --output reports/performance-report.html

# 导出 CSV 数据进行进一步分析
dotnet run --project PulseRPC.Benchmark.Client -- generate-report \
  --input results/test-results.json \
  --format csv \
  --output data/performance-data.csv
```

### 性能指标解读

| 指标 | 说明 | 测试结果 | 目标值 |
|------|------|---------|--------|
| **平均延迟** | 请求-响应的平均时间 | **19.5ms** (本地) | < 25ms |
| **P95 延迟** | 95% 请求的响应时间 | **~45ms** | < 50ms |
| **P99 延迟** | 99% 请求的响应时间 | **~85ms** | < 100ms |
| **QPS** | 每秒查询数 | **46-68 QPS** | > 100 QPS |
| **吞吐量** | 数据传输速率 | **80+ MB/s** | > 100 MB/s |
| **成功率** | 成功请求的百分比 | **99.8%** | > 99.5% |

## 🚀 快速开始：基础使用

### 环境要求

- **.NET 9 SDK** 或更高版本
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
```

### 智能连接管理示例

```csharp
// 创建智能客户端
var smartClient = PulseRpcClientFactory.CreateSmartClient();

// 获取服务代理（自动连接管理）
var chatService = await smartClient.GetServiceAsync<IChatHub>();
await chatService.SendMessageAsync("Hello, PulseRPC!");

// 多实例管理和广播
var chatManager = await smartClient.GetMultiInstanceServiceAsync<IChatHub>("ChatService");

// 广播到所有实例
var broadcastResult = await chatManager.BroadcastAsync(async chat =>
    await chat.SendGlobalAnnouncementAsync("全服公告"));

// 聚合查询
var totalUsers = await chatManager.AggregateAsync(
    async chat => await chat.GetOnlineUserCountAsync(),
    results => results.Sum());
```

### 服务发现配置

```csharp
var smartClient = PulseRpcClientFactory.CreateSmartBuilder()
    .WithServiceDiscovery(config =>
    {
        config.Type = ServiceDiscoveryType.Static;
        config.StaticEndpoints["ChatService"] = new ConnectionEndpoint
        {
            Host = "localhost",
            Port = 8000,
            Transport = TransportType.Tcp
        };
    })
    .WithServiceRouting<IChatHub>(config =>
    {
        config.DefaultStrategy = ServiceRoutingStrategy.RoundRobin;
        config.Failover.EnableFailover = true;
        config.HealthCheck.Enabled = true;
    })
    .Build();
```

## 📖 详细文档

### 核心文档
- [配置参考](Configuration.md) - 完整的配置选项和示例
- [最佳实践](BestPractices.md) - 生产环境使用指南
- [开发指南](guide/getting-started.md) - 快速入门和基础概念
- [变更日志](CHANGELOG.md) - 版本更新历史

### 高级功能
- [Unity 集成指南](Unity-SourceGenerator-Integration.md) - Unity 客户端集成详解
- [传输架构](transport-architecture.md) - 传输层设计和协议支持
- [序列化优化](serialization-fix.md) - MemoryPack 序列化优化

### 参考文档
- [架构设计](transport-refactor-proposal.md) - 系统架构和设计理念
- [性能优化](minimal-design-improvements.md) - 性能调优建议
- [路线图](ROADMAP.md) - 功能规划和发展方向

## 🧪 示例项目

项目包含了丰富的示例代码：

- **[ChatApp](../samples/ChatApp/)** - 完整的实时聊天应用示例，展示智能连接管理
- **[JwtAuthentication](../samples/JwtAuthentication/)** - JWT 身份验证集成示例
- **[JsonTranscoding](../samples/JsonTranscoding/)** - JSON 协议转码示例

### Unity 示例
- **[ChatApp.Unity](../samples/ChatApp/ChatApp.Unity/)** - Unity 聊天客户端
- **[PulseRPC.Client.Unity](../src/PulseRPC.Client.Unity/)** - Unity 集成包

## 📊 性能特性

基于 BenchmarkApp 的实际测试数据（WSL2 环境，.NET 9.0）：

### 延迟性能
- **平均延迟**: 19.5ms (本地网络)
- **P95 延迟**: ~45ms
- **P99 延迟**: ~85ms

### 吞吐量性能  
- **峰值 QPS**: 46-68 请求/秒
- **网络吞吐**: 发送 86.7MB/s，接收 80.8MB/s
- **成功率**: 99.8%

### 资源使用
- **CPU 使用率**: 50-55%
- **内存占用**: 160-492MB
- **零内存泄漏**: 24小时稳定性测试验证

## 🛠️ 开发指南

### 环境要求
- **.NET 9 SDK** 或更高版本
- **Visual Studio 2022** 或 **JetBrains Rider**（推荐）
- **Unity 2022.3 LTS**（用于 Unity 集成）

### 常用命令

```bash
# 恢复依赖（使用集中化包管理）
dotnet restore

# 构建整个解决方案
dotnet build

# 运行所有测试
dotnet test

# 构建发布版本
dotnet build -c Release
```

### 项目配置

本项目使用集中化包管理：
- **[Directory.Packages.props](../Directory.Packages.props)** - 统一的包版本管理
- **[Directory.Build.props](../Directory.Build.props)** - 全局构建配置
- **[global.json](../global.json)** - 指定 .NET SDK 版本

### 开发约定
- 启用 nullable reference types
- 使用 PublicAPI.Shipped.txt 和 PublicAPI.Unshipped.txt 进行 API 兼容性管理
- 遵循异步编程模式，使用 CancellationToken
- 客户端实现避免使用反射，通过 Source Generator 进行代码生成

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

本项目采用 [MIT 许可证](../LICENSE)。

## 📞 支持和联系

- **Issues**: [GitHub Issues](https://github.com/your-org/PulseRPC/issues)
- **Discussions**: [GitHub Discussions](https://github.com/your-org/PulseRPC/discussions)
- **文档**: [项目文档](../docs/)

## 🏆 致谢

感谢所有贡献者和社区成员的支持！

特别感谢：
- **MemoryPack** 提供高性能序列化
- **Microsoft** 提供 .NET 平台
- **Unity Technologies** 提供游戏引擎支持

---

⭐ **如果这个项目对你有帮助，请给我们一个星标！** 
