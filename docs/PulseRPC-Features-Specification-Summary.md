# PulseRPC 功能特性规格汇总

**文档版本**: 1.0
**创建日期**: 2025-10-10
**状态**: 当前实现状态总结

## 概述

本文档汇总 PulseRPC 项目的所有功能特性规格，包括已实现的核心功能、正在开发的功能以及计划中的功能增强。

## 📋 目录

1. [PulseRPC.Server 功能特性](#pulserpcserver-功能特性)
2. [PulseRPC.Client 功能特性](#pulserpcclient-功能特性)
3. [正在开发的功能](#正在开发的功能)
4. [功能特性详细规格](#功能特性详细规格)
5. [技术架构概览](#技术架构概览)

---

## PulseRPC.Server 功能特性

### 核心功能（已实现）

#### 1. 服务器生命周期管理
- **接口**: `IPulseServer`
- **位置**: `src/PulseRPC.Server/IPulseServer.cs`
- **功能描述**:
  - 异步启动和停止服务器 (`StartAsync`, `StopAsync`)
  - 服务器状态跟踪 (Stopped, Starting, Running, Stopping)
  - 优雅关闭机制
  - 服务器状态变更事件通知

#### 2. 传输层架构
- **接口**: `IPulseServerBuilder.AddTcp`, `IPulseServerBuilder.AddKcp`
- **位置**: `src/PulseRPC.Server/Builder/IPulseServerBuilder.cs`
- **功能描述**:
  - 多传输协议支持 (TCP, KCP)
  - 可配置的传输选项
  - 默认传输协议指定
  - 多端口监听支持
  - 传输层信息查询 (`GetTransports()`, `GetDefaultTransport()`)

**实现类**:
- `TcpServerListener` - TCP 传输实现
- `KcpServerListener` - KCP 传输实现
- `ITransportProvider` - 传输提供者接口
- `TransportIntegrationManager` - 传输集成管理器

#### 3. 连接管理
- **接口**: `IPulseServer`
- **位置**: `src/PulseRPC.Server/IPulseServer.cs`
- **功能描述**:
  - 活动连接追踪 (`ActiveConnectionCount`)
  - 连接信息查询 (`GetActiveConnections()`)
  - 广播消息到所有连接 (`BroadcastAsync`)
  - 点对点消息发送 (`SendAsync`)
  - 客户端连接/断开事件 (`ClientConnected`, `ClientDisconnected`)

**核心组件**:
- `IServerChannelManager` - 服务器通道管理器
- `ServerChannelManager` - 通道管理器实现
- `ServerTransportChannel` - 服务器传输通道

#### 4. 服务注册与管理
- **接口**: `IPulseServerBuilder`
- **位置**: `src/PulseRPC.Server/Builder/IPulseServerBuilder.cs`
- **功能描述**:
  - 泛型服务注册 (`AddService<TService, TImplementation>`)
  - 服务实例注册 (`AddService<TService>(instance)`)
  - 服务生命周期配置 (Singleton, Scoped, Transient)
  - 服务信息查询 (`GetRegisteredServices()`)
  - 服务元数据标签支持

#### 5. 高性能消息引擎
- **接口**: `IPulseServerBuilder.UseHighPerformanceEngine`
- **位置**: `src/PulseRPC.Server/Engine/HighPerformanceMessageEngine.cs`
- **功能描述**:
  - L1 循环缓冲区（高频小消息，4096 容量）
  - L2 批处理队列（中频中等消息，256 容量）
  - L3 响应队列（低频响应，128 容量）
  - 零拷贝消息传递
  - 可配置的缓冲区大小

**性能优化组件**:
- `HighPerformanceNetworkProcessor` - 高性能网络处理器
- `HighPerformanceServiceProcessor` - 高性能服务处理器
- `HighPerformanceResponseProcessor` - 高性能响应处理器
- `HighPerformanceDeserializer` - 高性能反序列化器
- `ZeroCopySerializationPipeline` - 零拷贝序列化管道

#### 6. 分层消息处理器
- **接口**: `IPulseServerBuilder.UseTieredMessageProcessor`
- **位置**: `src/PulseRPC.Server/Engine/TieredMessageProcessor.cs`
- **功能描述**:
  - 快速通道 (< 1KB 消息，2 专用线程)
  - 批量通道 (1KB-64KB 消息，批处理优化)
  - 分层内存池 (`TieredMemoryPool`)
  - 性能指标收集 (`TieredProcessorMetrics`)

**相关组件**:
- `TieredMessageEngineManager` - 分层引擎管理器
- `AdaptiveBatchScheduler` - 自适应批处理调度器
- `MessageBatch` - 消息批处理

#### 7. 优先级感知调度器
- **接口**: `IPulseServerBuilder.UsePriorityScheduler`
- **位置**: `src/PulseRPC.Server/Scheduling/PriorityAwareScheduler.cs`
- **功能描述**:
  - 多级优先级队列 (Critical: 50%, Normal: 35%, Bulk: 15%)
  - 基于优先级的消息调度
  - 可配置的权重分配
  - 优先级调度指标 (`PrioritySchedulerMetrics`)

**调度组件**:
- `AffinityAwareScheduler` - 亲和性感知调度器
- `WeightedRoundRobinScheduler` - 加权轮询调度器
- `ServiceThreadScheduler` - 服务线程调度器
- `ServiceThreadPool` - 服务线程池
- `WorkStealingMessageProcessor` - 工作窃取消息处理器

#### 8. 认证与授权
- **接口**: `IPulseServerBuilder.UseAuthentication`, `UseAuthorization`
- **位置**: `src/PulseRPC.Server/Authentication/`
- **功能描述**:
  - JWT 认证支持 (`JwtAuthenticationProvider`)
  - 基于角色的授权 (`RoleBasedAuthorizationProvider`)
  - 认证中间件 (`AuthenticationMiddleware`)
  - ServiceId 认证集成 (`ServiceIdAuthenticationExtensions`)
  - 统一认证服务 (`IUnifiedAuthenticationService`)

**认证组件**:
- `IAuthenticationProvider` - 认证提供者接口
- `IAuthorizationProvider` - 授权提供者接口
- `AuthenticationContext` - 认证上下文

#### 9. 中间件与拦截器
- **接口**: `IPulseMiddleware`, `IPulseInterceptor`
- **位置**: `src/PulseRPC.Server/Builder/IPulseServerBuilder.cs`
- **功能描述**:
  - 请求处理管道中间件
  - 请求前/后拦截器
  - 异常拦截器
  - 上下文数据传递 (`IPulseContext`)

#### 10. 性能监控与统计
- **接口**: `IPulseServer.GetPerformanceMetrics`
- **位置**: `src/PulseRPC.Server/IPulseServer.cs`
- **功能描述**:
  - 活动连接数统计
  - 总连接接受数
  - 消息处理统计（成功/丢弃）
  - 平均延迟测量
  - 吞吐量监控 (消息/秒)
  - 内存和 CPU 使用率
  - 性能指标重置

#### 11. 内存管理
- **位置**: `src/PulseRPC.Server/Memory/`
- **功能描述**:
  - 引用计数缓冲区 (`ReferenceCountedBuffer`)
  - 分层内存池 (`TieredMemoryPool`)
  - 缓冲池兼容性适配器 (`BufferPoolCompatibility`)

#### 12. 高级调度功能
- **位置**: `src/PulseRPC.Server/Scheduling/`, `Threading/`
- **功能描述**:
  - 服务执行上下文 (`ServiceExecutionContext`)
  - 工作窃取队列 (`WorkStealingQueue`)
  - 工作线程池 (`WorkerThread`)
  - 响应式背压控制 (`ReactiveBackpressureController`)
  - 令牌桶限流 (`TokenBucket`)
  - 一致性哈希环 (`ConsistentHashRing`)

#### 13. 网络优化
- **位置**: `src/PulseRPC.Server/IO/`
- **功能描述**:
  - 批量网络写入器 (`BatchedNetworkWriter`)

---

## PulseRPC.Client 功能特性

### 核心功能（已实现）

#### 1. 客户端核心接口
- **接口**: `IPulseClient`
- **位置**: `src/PulseRPC.Client/IPulseClient.cs`
- **功能描述**:
  - 客户端生命周期管理 (`InitializeAsync`, `StopAsync`)
  - 客户端状态跟踪 (`ClientState`)
  - 优雅关闭支持（超时配置）
  - 客户端统计信息 (`GetStatistics()`)
  - 健康检查 (`CheckHealthAsync`)
  - 状态变更事件 (`StateChanged`)

#### 2. 连接管理
- **接口**: `IConnectionManager`
- **位置**: `src/PulseRPC.Client/IPulseClient.cs`
- **功能描述**:
  - 连接创建和销毁
  - 连接注册表维护
  - 基于标签的连接查询 (`GetConnectionsByTag`)
  - 空闲连接清理 (`CleanupIdleConnectionsAsync`)
  - 连接池支持

**实现组件**:
- `ConnectionManager` - 连接管理器实现
- `ConnectionStateMachine` - 连接状态机
- `SimpleConnectionLifecycleManager` - 简单生命周期管理器

#### 3. 连接生命周期策略
- **接口**: `IConnectionLifecycleStrategy`
- **位置**: `src/PulseRPC.Client/LifecycleStrategies/`
- **功能描述**:
  - **持久连接** (`PersistentConnectionStrategy`) - 核心服务，自动重连
  - **会话连接** (`SessionConnectionStrategy`) - 会话期间保持
  - **临时连接** (`TransientConnectionStrategy`) - 任务完成即断开
  - **池化连接** (`PooledConnectionStrategy`) - 连接池复用

**基础类**:
- `ConnectionLifecycleStrategyBase` - 策略基类

#### 4. 连接池管理
- **接口**: `IConnectionPool`
- **位置**: `src/PulseRPC.Client/ConnectionPool/`
- **功能描述**:
  - **固定大小连接池** (`FixedSizeConnectionPool`)
  - **动态连接池** (`DynamicConnectionPool`) - 自动扩容/缩容
  - 连接预热
  - 空闲连接回收
  - 连接验证（获取时验证、空闲时验证）
  - 连接租约管理

**工厂**:
- `ConnectionPoolFactory` - 连接池工厂

#### 5. 服务发现
- **接口**: `IServiceDiscovery`
- **位置**: `src/PulseRPC.Client/ServiceDiscovery/`
- **功能描述**:
  - 服务实例发现 (`DiscoverAsync`)
  - 服务变化监听 (`WatchAsync`)
  - 服务列表查询 (`GetServicesAsync`)
  - 服务存在性检查 (`ExistsAsync`)
  - 缓存刷新 (`RefreshAsync`)

**实现**:
- `InMemoryServiceDiscovery` - 内存服务发现
- `ServiceDiscoveryBase` - 服务发现基类

#### 6. 负载均衡
- **接口**: `ILoadBalancer`
- **位置**: `src/PulseRPC.Client/LoadBalancing/`
- **功能描述**:
  - **随机负载均衡** (`RandomLoadBalancer`)
  - **轮询负载均衡** (`RoundRobinLoadBalancer`)
  - **加权轮询** (`WeightedRoundRobinLoadBalancer`)
  - **最少连接** (`LeastConnectionsLoadBalancer`)
  - **一致性哈希** (`ConsistentHashLoadBalancer`)
  - 负载均衡策略枚举 (`LoadBalancingStrategy`)
  - 负载均衡提示 (`LoadBalancingHint`)

**工厂和基类**:
- `LoadBalancerFactory` - 负载均衡器工厂
- `LoadBalancerBase` - 负载均衡器基类

#### 7. 连接路由
- **接口**: `IConnectionRouter`
- **位置**: `src/PulseRPC.Client/Routing/`
- **功能描述**:
  - 路由规则引擎 (`RegisterRule`, `RemoveRule`)
  - 智能路由决策 (`RouteAsync`)
  - 多维度匹配（标签、区域、用户）
  - 路由规则优先级
  - 路由上下文传递 (`RoutingContext`)
  - 匹配连接查询 (`GetMatchingConnections`)

**实现**:
- `ConnectionRouter` - 连接路由器实现
- `EnhancedConnectionRouter` - 增强型路由器
- `SimpleConnectionRouter` - 简单路由器
- `RoutingMonitor` - 路由监控

#### 8. 健康检查
- **接口**: `IHealthChecker`
- **位置**: `src/PulseRPC.Client/Health/`
- **功能描述**:
  - 连接健康检查 (`ConnectionHealthChecker`)
  - 连接池健康检查 (`ConnectionPoolHealthChecker`)
  - 健康检查管理器 (`HealthCheckManager`)
  - 健康状态枚举 (`ConnectionHealth`)
  - 健康检查结果 (`HealthCheckResult`)

#### 9. 可靠性机制
- **接口**: `IRetryPolicy`, `ICircuitBreaker`, `IFailoverManager`
- **位置**: `src/PulseRPC.Client/Reliability/`
- **功能描述**:
  - **重试策略** (`RetryPolicy`)
    - 指数退避
    - 抖动支持
    - 自定义重试条件
    - 最大重试次数配置
  - **熔断器** (`ICircuitBreaker`)
    - 服务降级
    - 快速失败
  - **故障转移** (`IFailoverManager`)
    - 自动切换实例

#### 10. 监控与统计
- **接口**: `IClientMonitor`, `IStatisticsCollector`
- **位置**: `src/PulseRPC.Client/Monitoring/`
- **功能描述**:
  - 统计数据收集 (`StatisticsCollector`)
  - 监控仪表板 (`MonitoringDashboard`, `MonitoringDashboardService`)
  - 性能指标
  - 错误分析
  - 趋势报告

#### 11. 事件系统
- **位置**: `src/PulseRPC.Client/Events/`
- **功能描述**:
  - 网络通道事件总线 (`NetworkChannelEventBus`)
  - 事件总线工厂 (`EventBusFactory`)
  - 事件监听器注册 (`EventListenerRegistrar`)
  - PulseReceiver 构建器 (`PulseReceiverBuilder`)
  - 事件错误处理 (`EventErrorHandler`)

#### 12. 客户端构建器
- **接口**: `IPulseClientBuilder`
- **位置**: `src/PulseRPC.Client/IPulseClient.cs`
- **功能描述**:
  - 流畅的配置 API
  - 连接配置 (`AddConnection`)
  - 服务发现配置 (`WithServiceDiscovery`)
  - 负载均衡配置 (`WithLoadBalancing`)
  - 连接池配置 (`WithConnectionPooling`)
  - 重试策略配置 (`WithRetryPolicy`)
  - 日志配置 (`WithLogging`)
  - 序列化器配置 (`WithSerializer`)
  - 认证配置 (`WithAuthentication`)
  - 传输选项配置 (`WithTransportOptions`)
  - 客户端选项配置 (`Configure`)

**实现**:
- `PulseClientBuilder` - 客户端构建器实现

#### 13. 传输层
- **位置**: `src/PulseRPC.Client/Transport/`
- **功能描述**:
  - TCP 客户端传输 (`TcpClientTransport`)
  - KCP 客户端传输 (`KcpClientTransport`)
  - 传输通道 (`TransportChannel`)
  - 传输通道选项 (`TransportChannelOptions`)

#### 14. 配置管理
- **位置**: `src/PulseRPC.Client/Configuration/`
- **功能描述**:
  - 客户端配置 (`ClientConfiguration`)
  - 客户端传输配置 (`ClientTransportConfiguration`)
  - 客户端选项 (`ClientOptions`)
  - 连接配置 (`ConnectionConfig`)

#### 15. 序列化
- **位置**: `src/PulseRPC.Client/Serialization/`
- **功能描述**:
  - 序列化器管理 (`SerializerManager`)

#### 16. 扩展功能
- **位置**: `src/PulseRPC.Client/`
- **功能描述**:
  - 游戏客户端扩展 (`GameClientExtensions`)
  - 任务扩展 (`TaskExtensions`)
  - 网络诊断 (`NetworkDiagnostics`)
  - 简单组件（`SimpleLoadBalancer`, `SimpleConnectionRegistry`, `SimpleConnectionRouter`）

---

## 正在开发的功能

### 1. ServiceName-Based Thread Scheduling for IPulseHub
- **Spec ID**: 001-channelattribute-servicename-ipulsehub
- **Spec 位置**: `specs/001-channelattribute-servicename-ipulsehub/spec.md`
- **功能分支**: `001-channelattribute-servicename-ipulsehub`
- **状态**: Draft
- **创建日期**: 2025-09-30

**功能描述**:
基于 ChannelAttribute 进行 ServiceName 的指定，所有 IPulseHub 基于 ServiceName 为服务线程调度执行，确保相同名称 Service 在同一个线程内执行，同时提供认证时把 ServiceId 放入 Service 内的接口，以便 ServiceName + ServiceId 进行准确调度。

**核心需求**:
1. 允许通过 ChannelAttribute 指定 ServiceName
2. 确保相同 ServiceName 的操作在单一线程中执行
3. 提供 ServiceId 注入接口
4. 基于 ServiceName + ServiceId 组合进行调度
5. 维护请求的执行顺序
6. 支持不同 ServiceName 的并发执行
7. 线程生命周期管理
8. 可观测性和诊断能力

**关键实体**:
- ServiceName - 通过 ChannelAttribute 指定的逻辑标识
- ServiceId - 认证时分配的唯一标识
- ChannelAttribute - 配置元数据
- Thread Scheduler - 调度组件
- Authentication Context - 认证上下文

### 2. Network Processing Flow Analysis & Optimization
- **Spec ID**: 001-pulserpc-server-ipulsehub
- **Spec 位置**: `specs/001-pulserpc-server-ipulsehub/spec.md`
- **功能分支**: `001-pulserpc-server-ipulsehub`
- **状态**: Draft
- **创建日期**: 2025-09-30

**功能描述**:
梳理 PulseRPC.Server 对于网络字节流的处理流程，从接收到消息包至 IPulseHub 实现类处理，分析并提出优化方案文档。

**核心需求**:
1. 提供从 socket 接收到 IPulseHub 方法执行的完整追踪
2. 识别所有中间处理阶段（反序列化、路由、方法分发）
3. 量化当前性能特征（延迟、吞吐量、内存分配）
4. 提出针对性优化策略
5. 优化建议包含可测量的性能改进目标
6. 检查缓冲区管理和内存分配效率
7. 评估不同传输协议的影响
8. 评估序列化/反序列化性能

**关键实体**:
- Network Byte Stream - 原始网络字节
- Message Packet - 解析后的结构化数据
- Processing Pipeline - 处理流水线
- IPulseHub Implementation - 业务逻辑处理
- Performance Metrics - 性能指标
- Optimization Recommendations - 优化建议

---

## 功能特性详细规格

### Server 功能特性矩阵

| 功能模块 | 核心接口 | 状态 | 性能特征 | 配置选项 |
|---------|---------|------|---------|---------|
| 生命周期管理 | `IPulseServer` | ✅ 已实现 | 毫秒级启动 | - |
| TCP 传输 | `TcpServerListener` | ✅ 已实现 | 高可靠性 | 端口、缓冲区大小 |
| KCP 传输 | `KcpServerListener` | ✅ 已实现 | 低延迟 | 端口、KCP 参数 |
| 连接管理 | `IServerChannelManager` | ✅ 已实现 | 支持数千并发连接 | 最大连接数 |
| 服务注册 | `IPulseServerBuilder` | ✅ 已实现 | DI 集成 | 生命周期配置 |
| 高性能引擎 | `HighPerformanceMessageEngine` | ✅ 已实现 | L1/L2/L3 队列 | 缓冲区大小 |
| 分层处理 | `TieredMessageProcessor` | ✅ 已实现 | 快速/批量通道 | 消息阈值、线程数 |
| 优先级调度 | `PriorityAwareScheduler` | ✅ 已实现 | Critical/Normal/Bulk | 权重配置 |
| JWT 认证 | `JwtAuthenticationProvider` | ✅ 已实现 | 标准 JWT | 密钥、过期时间 |
| 角色授权 | `RoleBasedAuthorizationProvider` | ✅ 已实现 | 基于角色 | 角色列表 |
| 中间件 | `IPulseMiddleware` | ✅ 已实现 | 请求管道 | - |
| 拦截器 | `IPulseInterceptor` | ✅ 已实现 | 请求前后拦截 | - |
| 性能监控 | `ServerPerformanceMetrics` | ✅ 已实现 | 实时统计 | 重置能力 |
| ServiceName 调度 | - | 🚧 开发中 | 线程亲和性 | 线程池配置 |

### Client 功能特性矩阵

| 功能模块 | 核心接口 | 状态 | 性能特征 | 配置选项 |
|---------|---------|------|---------|---------|
| 客户端核心 | `IPulseClient` | ✅ 已实现 | 异步生命周期 | 超时、并发限制 |
| 连接管理 | `IConnectionManager` | ✅ 已实现 | 智能连接管理 | - |
| 持久连接 | `PersistentConnectionStrategy` | ✅ 已实现 | 自动重连 | 重连参数 |
| 会话连接 | `SessionConnectionStrategy` | ✅ 已实现 | 会话保持 | - |
| 临时连接 | `TransientConnectionStrategy` | ✅ 已实现 | 即用即断 | - |
| 池化连接 | `PooledConnectionStrategy` | ✅ 已实现 | 连接复用 | 池大小 |
| 固定连接池 | `FixedSizeConnectionPool` | ✅ 已实现 | 固定大小 | 池大小 |
| 动态连接池 | `DynamicConnectionPool` | ✅ 已实现 | 自动扩缩容 | 最小/最大大小 |
| 内存服务发现 | `InMemoryServiceDiscovery` | ✅ 已实现 | 本地快速 | - |
| Consul 服务发现 | - | 📋 计划中 | 分布式 | Consul 地址 |
| 随机负载均衡 | `RandomLoadBalancer` | ✅ 已实现 | 均匀分布 | - |
| 轮询负载均衡 | `RoundRobinLoadBalancer` | ✅ 已实现 | 顺序分配 | - |
| 加权轮询 | `WeightedRoundRobinLoadBalancer` | ✅ 已实现 | 权重分配 | 权重配置 |
| 最少连接 | `LeastConnectionsLoadBalancer` | ✅ 已实现 | 负载感知 | - |
| 一致性哈希 | `ConsistentHashLoadBalancer` | ✅ 已实现 | 会话亲和 | 虚拟节点数 |
| 连接路由 | `ConnectionRouter` | ✅ 已实现 | 规则引擎 | 路由规则 |
| 健康检查 | `HealthCheckManager` | ✅ 已实现 | 定期检查 | 检查间隔 |
| 重试策略 | `RetryPolicy` | ✅ 已实现 | 指数退避 | 最大重试、延迟 |
| 熔断器 | `ICircuitBreaker` | ✅ 已实现 | 快速失败 | 阈值配置 |
| 统计监控 | `StatisticsCollector` | ✅ 已实现 | 实时统计 | - |
| 监控仪表板 | `MonitoringDashboard` | ✅ 已实现 | 可视化监控 | - |
| TCP 传输 | `TcpClientTransport` | ✅ 已实现 | 可靠传输 | 传输选项 |
| KCP 传输 | `KcpClientTransport` | ✅ 已实现 | 低延迟传输 | KCP 参数 |

---

## 技术架构概览

### Server 架构层次

```
┌─────────────────────────────────────────────────────────┐
│                   IPulseServer (对外接口)                  │
│  生命周期 | 连接管理 | 性能监控 | 事件通知                    │
└────────────────────┬────────────────────────────────────┘
                     │
┌────────────────────┴────────────────────────────────────┐
│              IPulseServerBuilder (构建器)                 │
│  传输配置 | 服务注册 | 性能优化 | 认证授权 | 中间件          │
└────────────────────┬────────────────────────────────────┘
                     │
        ┌────────────┼────────────┐
        │            │            │
┌───────▼────┐ ┌────▼─────┐ ┌───▼────────┐
│  传输层     │ │  消息引擎  │ │  调度层     │
│ TCP/KCP    │ │ L1/L2/L3  │ │ 优先级调度  │
│ Listener   │ │ 分层处理   │ │ 工作窃取   │
└────────────┘ └──────────┘ └───────────┘
        │            │            │
        └────────────┼────────────┘
                     │
        ┌────────────┼────────────┐
        │            │            │
┌───────▼────┐ ┌────▼─────┐ ┌───▼────────┐
│  认证授权   │ │  中间件    │ │  性能监控   │
│ JWT/Role   │ │ Intercept │ │  Metrics   │
└────────────┘ └──────────┘ └───────────┘
                     │
            ┌────────┴────────┐
            │                 │
    ┌───────▼────┐    ┌──────▼────────┐
    │  IPulseHub │    │  服务处理器     │
    │  实现类     │    │  ServiceProc  │
    └────────────┘    └───────────────┘
```

### Client 架构层次

```
┌─────────────────────────────────────────────────────────┐
│                  IPulseClient (对外接口)                   │
│  生命周期 | 连接 | 路由 | 服务发现 | 统计 | 健康检查         │
└────────────────────┬───────────────────────────────────┘
                     │
        ┌────────────┼────────────┬───────────────┐
        │            │            │               │
┌───────▼────┐ ┌────▼─────┐ ┌───▼────────┐ ┌───▼────────┐
│ 连接管理器  │ │ 连接路由  │ │ 服务发现    │ │ 负载均衡   │
│ IConnMgr   │ │ IRouter  │ │ IDiscovery │ │ ILoadBal   │
└────┬───────┘ └──────────┘ └───────────┘ └────────────┘
     │
     ├─────────────┬───────────────┬────────────────┐
     │             │               │                │
┌────▼─────┐ ┌────▼──────┐ ┌──────▼──────┐ ┌──────▼──────┐
│ 持久连接  │ │ 会话连接   │ │  临时连接    │ │  池化连接   │
│Persistent│ │  Session  │ │  Transient  │ │   Pooled    │
└──────────┘ └───────────┘ └─────────────┘ └─────────────┘
     │             │               │                │
     └─────────────┼───────────────┴────────────────┘
                   │
        ┌──────────┼──────────┬───────────────┐
        │          │          │               │
┌───────▼────┐ ┌──▼──────┐ ┌─▼──────────┐ ┌──▼────────┐
│ 连接池     │ │ 健康检查 │ │  重试策略   │ │  熔断器   │
│ IConnPool │ │ IHealth │ │  IRetry    │ │ ICircuit  │
└───────────┘ └─────────┘ └────────────┘ └───────────┘
        │          │          │               │
        └──────────┼──────────┴───────────────┘
                   │
        ┌──────────┼──────────┐
        │          │          │
┌───────▼────┐ ┌──▼──────┐ ┌─▼──────────┐
│ 传输层     │ │ 监控统计 │ │  事件系统   │
│ TCP/KCP   │ │ Monitor │ │  EventBus  │
└───────────┘ └─────────┘ └────────────┘
```

---

## 性能指标

### Server 性能指标（基于 BenchmarkApp）

| 指标 | 测试结果 | 目标值 | 备注 |
|-----|---------|--------|------|
| 平均延迟 | **19.5ms** | < 25ms | 本地网络测试 |
| P95 延迟 | **~45ms** | < 50ms | 95% 请求 |
| P99 延迟 | **~85ms** | < 100ms | 99% 请求 |
| P99.9 延迟 | **~120ms** | < 150ms | 99.9% 请求 |
| QPS | **46-68** | > 100 | 每秒查询数 |
| 吞吐量 | **80+ MB/s** | > 100 MB/s | 数据传输速率 |
| 成功率 | **99.8%** | > 99.5% | 请求成功率 |
| CPU 使用率 | **50-55%** | < 70% | 高负载下 |
| 内存使用 | **160-492MB** | < 1GB | 稳定运行 |
| 连接数 | **数千** | > 10000 | 并发连接支持 |

### Client 性能指标

| 指标 | 特征 | 备注 |
|-----|------|------|
| 连接建立 | < 100ms | TCP/KCP |
| 连接池获取 | < 1ms | 预热状态 |
| 服务发现 | < 50ms | 本地缓存 |
| 路由决策 | < 1ms | 规则引擎 |
| 健康检查 | 可配置 | 默认 30s |
| 重试延迟 | 指数退避 | 100ms 起始 |

---

## 配置示例

### Server 配置示例

```csharp
var serverBuilder = new PulseServerBuilder(services)
    // 传输层配置
    .AddTcp("tcp-main", port: 8080, isDefault: true)
    .AddKcp("kcp-battle", port: 9001)

    // 服务注册
    .AddService<IAuthService, AuthService>(ServiceLifetime.Singleton)
    .AddService<IGameService, GameService>(ServiceLifetime.Scoped)

    // 性能优化
    .UseHighPerformanceEngine(opt =>
    {
        opt.L1BufferSize = 4096;
        opt.L2QueueCapacity = 256;
        opt.L3QueueCapacity = 128;
    })
    .UseTieredMessageProcessor(opt =>
    {
        opt.FastPath.MessageSizeThreshold = 1024;
        opt.FastPath.DedicatedThreads = 2;
        opt.BatchPath.BatchSize = 16;
    })
    .UsePriorityScheduler(opt =>
    {
        opt.CriticalWeight = 50;
        opt.NormalWeight = 35;
        opt.BulkWeight = 15;
    })

    // 认证授权
    .UseAuthentication(opt =>
    {
        opt.JwtSecretKey = "your-secret-key";
        opt.JwtExpiration = TimeSpan.FromHours(1);
    })
    .UseAuthorization(opt =>
    {
        opt.SupportedRoles = new[] { "Admin", "User", "Guest" };
    })

    // 中间件
    .UseMiddleware<LoggingMiddleware>()
    .UseMiddleware<MetricsMiddleware>()

    // 服务器配置
    .ConfigureServer(opt =>
    {
        opt.MaxConnections = 10000;
        opt.EnableCompression = true;
    });

serverBuilder.Build();
```

### Client 配置示例

```csharp
var client = new PulseClientBuilder()
    // 连接配置
    .AddConnection(new ConnectionDescriptor
    {
        Id = "auth-service",
        ServiceName = "authentication-service",
        Transport = TransportType.Tcp,
        Strategy = ConnectionStrategy.Persistent,
        AutoReconnect = true,
        Tags = new Dictionary<string, string>
        {
            ["type"] = "core",
            ["region"] = "us-west-2"
        }
    })

    // 服务发现
    .WithServiceDiscovery(new InMemoryServiceDiscovery())

    // 负载均衡
    .WithLoadBalancing(LoadBalancingStrategy.WeightedRoundRobin)

    // 连接池
    .WithConnectionPooling(new ConnectionPoolOptions
    {
        Strategy = PoolingStrategy.Dynamic,
        MinSize = 2,
        MaxSize = 20,
        IdleTimeout = TimeSpan.FromMinutes(10)
    })

    // 重试策略
    .WithRetryPolicy(new RetryPolicy
    {
        MaxRetries = 3,
        BaseDelay = TimeSpan.FromMilliseconds(100),
        BackoffStrategy = BackoffStrategy.Exponential
    })

    // 客户端选项
    .Configure(opt =>
    {
        opt.DefaultTimeout = TimeSpan.FromSeconds(30);
        opt.MaxConcurrentConnections = 100;
        opt.EnableStatistics = true;
    })

    .Build();

await client.InitializeAsync();
```

---

## 功能路线图

### 近期计划（当前迭代）
- ✅ 完成 ServiceName-Based Thread Scheduling 实现
- ✅ 完成 Network Processing Flow Analysis 文档
- 🚧 性能优化实施
- 🚧 更多性能测试场景

### 中期计划（下一季度）
- 📋 Consul/Etcd 服务发现集成
- 📋 Kubernetes 服务发现支持
- 📋 分布式追踪集成 (OpenTelemetry)
- 📋 更多认证提供者（OAuth2, OpenID Connect）
- 📋 消息压缩优化
- 📋 Unity 客户端完善

### 长期计划（未来版本）
- 📋 gRPC 兼容层
- 📋 HTTP/3 传输支持
- 📋 服务网格集成
- 📋 可观测性平台集成（Prometheus, Grafana）
- 📋 多语言客户端支持（Python, Go, Java）

---

## 参考文档

### 核心文档
- [README](../README.md) - 项目概述
- [开发指南](guide/getting-started.md) - 快速入门
- [变更日志](CHANGELOG.md) - 版本历史

### 架构文档
- [PulseRPC.Server 架构分析报告](PulseRPC-Server-Architecture-Analysis-Report.md)
- [PulseRPC.Server DI 接口设计](PulseRPC-Server-DI-Interface-Design.md)
- [架构统一计划](Architecture-Unification-Plan.md)
- [热路径优化指南](HotPath-Optimization-Guide.md)
- [热路径优化总结](热路径优化总结.md)

### 使用指南
- [PulseRPC.Client 使用指南](guide/pulserpc-client-guide.md)
- [PulseRPC.Server 使用指南](guide/pulserpc-server-guide.md)
- [PulseRPC Client-Server 使用指南](PulseRPC-Client-Server-Usage-Guide.md)
- [Unity 源生成器集成](Unity-SourceGenerator-Integration.md)

### Spec 文档
- [001-pulserpc-server-ipulsehub](../specs/001-pulserpc-server-ipulsehub/spec.md) - 网络流处理优化
- [001-channelattribute-servicename-ipulsehub](../specs/001-channelattribute-servicename-ipulsehub/spec.md) - ServiceName 调度

---

## 更新日志

- **2025-10-10**: 初始版本，汇总 Server 和 Client 的所有功能特性
