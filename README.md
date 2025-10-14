# PulseRPC

[![NuGet](https://img.shields.io/nuget/v/PulseRPC.Client.svg)](https://www.nuget.org/packages/PulseRPC.Client/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/9.0)
[![Build Status](https://img.shields.io/badge/build-passing-brightgreen.svg)](https://github.com/pulseRPC/PulseRPC)

基于现代 .NET 平台的企业级高性能 RPC 框架，支持 TCP 和 KCP 传输协议，专为 Unity 游戏和微服务架构设计。

## 🚀 核心特性

### 🧠 智能连接管理
- **企业级架构**：支持复杂的微服务和游戏服务器场景
- **自动连接管理**：智能的连接生命周期管理，支持持久、会话和临时连接策略
- **动态服务发现**：支持 Consul、Etcd、Kubernetes 等主流服务发现方案
- **智能负载均衡**：轮询、最少连接、一致性哈希、加权轮询等多种策略
- **故障转移**：自动故障检测、健康检查和实例切换
- **连接池管理**：动态连接池，支持预热、扩容、缩容和空闲回收

### 🔧 高级连接功能
- **多传输协议**：TCP（可靠）+ KCP（低延迟），根据场景自动选择
- **路由规则引擎**：基于标签、区域、用户类型的智能请求分发
- **实时监控**：连接状态、性能指标、健康状况的实时监控和报告
- **批量操作**：支持连接的批量管理、广播消息和聚合查询
- **事件驱动**：完整的连接生命周期事件和服务发现变化通知

### 🎮 游戏场景优化
- **战斗服连接**：KCP 低延迟连接，支持自动清理和战斗结束断开
- **地图服切换**：无缝的地图服务器切换，先连后断避免数据丢失
- **副本服管理**：临时副本连接管理，支持副本结束自动清理
- **核心服务**：登录、聊天等核心服务的持久连接管理

### 🏢 企业级可靠性
- **重试策略**：指数退避、抖动、自定义重试条件
- **熔断机制**：服务降级和快速失败保护
- **统计监控**：详细的性能统计、错误分析和趋势报告
- **优雅关闭**：支持优雅的服务停止和连接清理

### 🛠️ 开发者友好
- **声明式配置**：流畅的 Builder 模式配置 API
- **代码生成**：基于 Source Generator 的零反射客户端代理
- **类型安全**：完整的泛型和强类型支持
- **现代 C#**：async/await、nullable、records 等现代语言特性

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

## 🚀 快速开始

### 1. 企业级应用示例

```csharp
// 构建企业级客户端 - 声明式配置
var client = new PulseClientBuilder()
    // 核心服务连接（持久连接）
    .AddConnection(new ConnectionDescriptor
    {
        Id = "auth-service-primary",
        Name = "auth-service",
        ServiceName = "authentication-service",
        Transport = TransportType.Tcp,
        Strategy = ConnectionStrategy.Persistent,
        AutoReconnect = true,
        Tags = new Dictionary<string, string> { ["type"] = "core", ["service"] = "auth" }
    })
    .AddConnection(new ConnectionDescriptor
    {
        Id = "user-service-primary",
        Name = "user-service",
        ServiceName = "user-management-service",
        Transport = TransportType.Tcp,
        Strategy = ConnectionStrategy.Persistent,
        AutoReconnect = true,
        Tags = new Dictionary<string, string> { ["type"] = "core", ["service"] = "user" }
    })
    // 配置服务发现
    .WithServiceDiscovery(new ConsulServiceDiscovery("http://consul.company.com:8500"))
    .WithLoadBalancing(LoadBalancingStrategy.WeightedRoundRobin)
    .WithConnectionPooling(new ConnectionPoolOptions
    {
        Strategy = PoolingStrategy.Dynamic,
        MinSize = 2,
        MaxSize = 20,
        IdleTimeout = TimeSpan.FromMinutes(10)
    })
    .WithRetryPolicy(new RetryPolicy
    {
        MaxRetries = 3,
        BaseDelay = TimeSpan.FromMilliseconds(100),
        BackoffStrategy = BackoffStrategy.Exponential
    })
    .Configure(options =>
    {
        options.DefaultTimeout = TimeSpan.FromSeconds(30);
        options.MaxConcurrentConnections = 100;
        options.EnableStatistics = true;
    })
    .Build();

// 初始化客户端
await client.InitializeAsync();

// 获取认证服务（自动路由到最佳连接）
var authService = await client.GetServiceAsync<IAuthenticationService>();
var loginResult = await authService.LoginAsync("user@company.com", "password");

// 获取用户服务
var userService = await client.GetServiceAsync<IUserService>();
var userProfile = await userService.GetUserProfileAsync(loginResult.UserId);

// 停止客户端
await client.StopAsync();
```

### 2. 微服务架构示例

```csharp
var client = new PulseClientBuilder()
    .WithServiceDiscovery(new KubernetesServiceDiscovery())
    .WithLoadBalancing(LoadBalancingStrategy.LeastConnections)
    .Build();

await client.InitializeAsync();

// 动态连接到订单服务
var orderConnection = await client.ConnectToServiceAsync("order-service", new ServiceConnectionOptions
{
    Strategy = ConnectionStrategy.Session,
    PreferredTransport = TransportType.Tcp,
    LoadBalancingHint = LoadBalancingHint.LeastConnections,
    Tags = new Dictionary<string, string> { ["region"] = "us-west-2" }
});

var orderService = await orderConnection.GetServiceAsync<IOrderService>();
var orders = await orderService.GetOrdersAsync(customerId: "123");

// 断开特定服务连接
await client.DisconnectAsync(orderConnection.Id);
```

### 3. 游戏客户端示例

```csharp
var gameClient = new PulseClientBuilder()
    .AddGameServerSet("production")
    .AddDevelopmentServers("localhost", 8000)
    .WithBattleOptimizations()
    .WithConnectionPooling(maxConnections: 50)
    .Build();

await gameClient.InitializeAsync();

// 连接到核心游戏服务
var loginConnection = await gameClient.ConnectToCoreServerAsync("login-service");
var loginService = await loginConnection.GetServiceAsync<ILoginService>();

// 连接到战斗服（低延迟 KCP）
var battleConnection = await gameClient.ConnectToBattleServerAsync("battle-123", "battle1.game.com", 9001);
var battleService = await battleConnection.GetServiceAsync<IBattleService>();

// 地图切换
var newMapConnection = await gameClient.SwitchMapAsync("map-001", "map-002");

// 使用临时连接执行任务
await gameClient.WithTemporaryConnectionAsync(
    new ConnectionConfig { Name = "temp-task", Host = "task.server.com", Port = 8080 },
    async connection =>
    {
        var taskService = await connection.GetServiceAsync<ITaskService>();
        await taskService.CompleteTaskAsync("daily-login");
    });

// 离开战斗
await gameClient.LeaveBattleAsync("battle-123");
```

### 4. 高并发连接池示例

```csharp
var poolFactory = new ConnectionPoolFactory();

// 创建数据库连接池
var dbPool = poolFactory.CreatePool(
    "database-pool",
    new ConnectionDescriptor
    {
        Id = "db-connection-template",
        Name = "database",
        ServiceName = "database-service",
        Transport = TransportType.Tcp,
        Strategy = ConnectionStrategy.Pooled
    },
    new ConnectionPoolOptions
    {
        Strategy = PoolingStrategy.FixedSize,
        MinSize = 10,
        MaxSize = 50,
        ValidateOnAcquire = true,
        ValidateWhileIdle = true
    });

await dbPool.InitializeAsync();

// 使用连接池
using (var lease = await dbPool.AcquireAsync(timeout: TimeSpan.FromSeconds(5)))
{
    var dbService = await lease.Connection.GetServiceAsync<IDatabaseService>();
    var result = await dbService.ExecuteQueryAsync("SELECT * FROM users");
    // 连接自动归还到池中
}

// 监控连接池状态
var stats = dbPool.GetStatistics();
Console.WriteLine($"Pool: {stats.ActiveConnections}/{stats.CurrentSize} connections");
```

### 5. 智能路由和负载均衡

```csharp
var client = new PulseClientBuilder()
    .WithLoadBalancing(LoadBalancingStrategy.ConsistentHash)
    .Build();

await client.InitializeAsync();

// 注册路由规则
client.Router.RegisterRule(new RoutingRule
{
    Id = "admin-rule",
    Name = "Route admin requests to dedicated servers",
    Matcher = (routingKey, context) => context?.Tags.GetValueOrDefault("user_type") == "admin",
    Selector = (connections, context) =>
        connections.FirstOrDefault(c => c.Descriptor.Tags.GetValueOrDefault("tier") == "premium"),
    Priority = 100
});

client.Router.RegisterRule(new RoutingRule
{
    Id = "region-rule",
    Name = "Route by user region",
    Matcher = (routingKey, context) => !string.IsNullOrEmpty(context?.PreferredRegion),
    Selector = (connections, context) =>
        connections.FirstOrDefault(c => c.Descriptor.Tags.GetValueOrDefault("region") == context?.PreferredRegion),
    Priority = 50
});

// 使用路由上下文
var routingContext = new RoutingContext
{
    UserId = "user123",
    Tags = new Dictionary<string, string> { ["user_type"] = "admin" },
    PreferredRegion = "us-east-1",
    LoadBalancingHint = LoadBalancingHint.ConsistentHash
};

var connection = await client.Router.RouteAsync("api-service", routingContext);
var apiService = await connection.GetServiceAsync<IApiService>();
```

## 📖 详细文档

### 核心文档
- [开发指南](guide/getting-started.md) - 快速入门和基础概念
- [变更日志](CHANGELOG.md) - 版本更新历史

### 高级功能
- [Unity 集成指南](Unity-SourceGenerator-Integration.md) - Unity 客户端集成详解
- [传输架构](transport-architecture.md) - 传输层设计和协议支持
- [Source Generator 集成](docs/PulseRPC-Server-DI-Interface-Design.md) - 代码生成器使用指南

### 参考文档
- [性能优化总结](docs/热路径优化总结.md) - 性能调优建议
- [路线图](ROADMAP.md) - 功能规划和发展方向

## 🧪 示例项目

项目包含了丰富的示例代码：

- **[ChatApp](samples/ChatApp/)** - 完整的实时聊天应用示例
- **[JwtAuthentication](samples/JwtAuthentication/)** - JWT 身份验证集成示例
- **[JsonTranscoding](samples/JsonTranscoding/)** - JSON 协议转码示例

### Unity 示例
- **[ChatApp.Unity](samples/ChatApp/ChatApp.Unity/)** - Unity 聊天客户端
- **[PulseRPC.Client.Unity](src/PulseRPC.Client.Unity/)** - Unity 集成包


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

## 🔧 Source Generator 调试

如需调试和排查 `PulseRPC.Server.SourceGenerator`，请参考：

- **[快速开始](docs/Debug-SourceGenerator-QuickStart.md)** - 5 分钟快速上手
- **[文档汇总](docs/Debug-SourceGenerator-README.md)** - 所有调试资源的入口
- **[Rider 调试指南](docs/Debug-SourceGenerator-Rider.md)** - 完整的 Rider 调试指南
- **[内存分析示例](docs/SourceGenerator-Memory-Profiling-Example.md)** - 内存监控和优化
- **[快速参考](docs/Debug-SourceGenerator-QuickRef.md)** - 命令和技巧速查

### 快速调试命令

```bash
# 使用调试脚本（推荐）
.\scripts\debug-sourcegenerator.ps1 -CleanFirst -Verbose -ViewReport

# 或手动构建并查看诊断
dotnet build -bl:msbuild.binlog
dotnet build 2>&1 | Select-String "PULSE"

# 查看生成的文件
ls perf/BenchmarkApp/PulseRPC.Benchmark.Server/obj/Generated/
```

---

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
- 传输层支持自动重连和连接状态监控

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
