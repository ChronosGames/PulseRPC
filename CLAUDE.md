# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目架构

- 详见 [README](README.md)

## 常用命令

- 详见 [README](README.md)

## 开发指导

### 配置管理
- **Directory.Packages.props** - 集中化包版本管理
- **Directory.Build.props** - 全局构建配置，包含 NuGet 包装信息
- **global.json** - 指定 .NET SDK 版本 9.0.205（如本地版本不匹配，更新此文件）

### 序列化约定
- 优先使用 MemoryPack 进行序列化
- 实现 `IPulseRPCSerializer` 接口的自定义序列化器
- 消息类型需标记 `[MemoryPackable]` 特性

### 传输层架构
- **ITransport** - 传输层抽象
- **TcpTransport/KcpTransport** - 具体传输实现
- **ITransportChannel** - 通道抽象，包含消息头和类型定义

### 集群功能
- **服务发现** - `IServiceDiscovery` 接口，支持 Consul、Etcd、Kubernetes
- **负载均衡** - 支持随机、轮询、最少连接、加权轮询策略  
- **健康检查** - `IHealthChecker` 接口，支持自定义健康检查逻辑

### 测试约定
- 单元测试位于 `tests/` 目录
- 集成测试使用 Testcontainers 进行容器化测试
- 性能测试使用 `perf/BenchmarkApp` 框架
- 使用 xUnit、FluentAssertions、NSubstitute 进行测试

### 代码约定
- 启用 nullable reference types
- 使用 PublicAPI.Shipped.txt 和 PublicAPI.Unshipped.txt 进行 API 兼容性管理
- 生成 XML 文档，抑制 CS1591 警告
- 遵循异步编程模式，使用 CancellationToken

### 性能考虑
- 使用 `NetworkBufferPool` 进行内存池化
- 实现 `ReferenceCountedBuffer` 进行引用计数管理
- 考虑使用 `System.Threading.Channels` 进行高性能消息传递
- BenchmarkApp 提供完整的性能分析和报告功能

### 生成器特殊约定
- PulseRPC.Client.SourceGenerator生成的代码要符合C# 9.0及以下的语法规范

### 客户端实现约定
- 客户端部分的接口与实现避免使用反射，尽量通过 PulseRPC.Client.SourceGenerator 使用代码生成替代
