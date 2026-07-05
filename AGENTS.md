# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## 项目概述

PulseRPC 是基于现代 .NET 平台的企业级高性能 RPC 框架，支持 TCP 和 KCP 传输协议，专为 Unity 游戏和微服务架构设计。

## 项目结构

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
│   ├── BenchmarkApp/             # 系统级性能基准测试框架
│   ├── Microbenchmark/           # 方法级微基准测试
│   └── SourceGeneratorPerf/      # 源生成器性能测试
├── samples/                      # 示例应用
│   ├── ChatApp/                  # 实时聊天应用示例
│   ├── JwtAuthentication/        # JWT 身份验证示例
│   └── JsonTranscoding/          # JSON 转码示例
├── tests/                        # 单元测试和集成测试
└── docs/                         # 项目文档
```

## 常用命令

```bash
# 恢复依赖
dotnet restore

# 构建整个解决方案
dotnet build

# 运行所有测试
dotnet test

# 构建发布版本
dotnet build -c Release

# 启动性能测试服务端
cd perf/BenchmarkApp && dotnet run --project PulseRPC.Benchmark.Server

# 运行性能测试客户端
dotnet run --project PulseRPC.Benchmark.Client -- run --server localhost:8080 --scenario ping-pong --duration 30

# 调试 Source Generator
dotnet build -bl:msbuild.binlog
```

## 开发指导

### 配置管理
- **Directory.Packages.props** - 集中化包版本管理
- **Directory.Build.props** - 全局构建配置，包含 NuGet 包装信息（当前版本 1.1.5）
- **global.json** - 指定 .NET SDK 版本 10.0.100（如本地版本不匹配，更新此文件）

### 序列化约定
- 优先使用 MemoryPack 进行序列化
- 实现 `IPulseRPCSerializer` 接口的自定义序列化器
- 消息类型需标记 `[MemoryPackable]` 特性

### 传输层架构
- **ITransport** - 传输层抽象
- **TcpTransport/KcpTransport** - 具体传输实现（TCP 可靠传输，KCP 低延迟传输）
- **ITransportChannel** - 通道抽象，包含消息头和类型定义

### 集群功能
- **集群发现** - `IDiscoveryProvider` / `IClusterMembership` 抽象，支持静态成员以及 Consul、Etcd、Kubernetes 后端
- **负载均衡** - 支持随机、轮询、最少连接、加权轮询、一致性哈希策略
- **健康检查** - 客户端连接健康检查与服务端 `IPulseServiceHealthCheck`，支持自定义健康检查逻辑

### 测试约定
- 单元测试位于 `tests/` 目录
- 集成测试使用 Testcontainers 进行容器化测试
- 性能测试使用 `perf/BenchmarkApp` 框架
- 使用 xUnit、FluentAssertions、NSubstitute 进行测试

### 代码约定
- 启用 nullable reference types（Nullable 警告视为错误）
- 使用 PublicAPI.Shipped.txt 和 PublicAPI.Unshipped.txt 进行 API 兼容性管理
- 生成 XML 文档，抑制 CS1591 警告
- 遵循异步编程模式，使用 CancellationToken

### 性能考虑
- 使用 `NetworkBufferPool` 进行内存池化
- 实现 `ReferenceCountedBuffer` 进行引用计数管理
- 考虑使用 `System.Threading.Channels` 进行高性能消息传递
- BenchmarkApp 提供完整的性能分析和报告功能

### 生成器特殊约定
- PulseRPC.Client.SourceGenerator 生成的代码要符合 C# 9.0 及以下的语法规范（兼容 Unity）
- PulseRPC.Server.SourceGenerator 生成的代码要符合 C# 14.0 及以下的语法规范

### 客户端实现约定
- 客户端部分的接口与实现避免使用反射，尽量通过 PulseRPC.Client.SourceGenerator 使用代码生成替代
- 支持自动连接管理、故障转移和连接池

## 参考文档

- [README](README.md) - 完整项目说明和使用示例
- [Unity 集成指南](docs/使用指南/Unity%20Source%20Generator%20集成指南.md) - Unity 客户端集成详解
- [Source Generator 调试](docs/Debug-SourceGenerator-README.md) - 代码生成器调试资源
- [网络库评估与优化计划](docs/待办计划/260703%20-%20PulseRPC%20网络库评估与优化计划.md) - 当前传输层优化记录
