# PulseRPC 高性能服务端示例

本示例完整演示了 PulseRPC.Server 的高性能架构流程，从字节流接收到响应发送的完整数据处理链路。

## 架构流程概览

```
传输层数据接收 -> 网络处理器 -> 反序列化器 -> 消息调度器 -> 服务处理器 -> 响应处理器 -> 传输层发送
     ↓              ↓           ↓            ↓            ↓            ↓
   字节流         消息解析     消息反序列化   优先级调度    方法调用     响应序列化
```

## 核心组件详解

### 1. 网络处理器 (HighPerformanceNetworkProcessor)
- **职责**: 接收原始字节流，处理分包/粘包，解析完整消息包
- **特性**: 
  - 使用 `System.IO.Pipelines` 进行零拷贝处理
  - 多线程并行处理
  - 连接状态管理

### 2. 反序列化器 (HighPerformanceDeserializer)
- **职责**: 将消息包反序列化为服务调用上下文
- **特性**:
  - 序列化器缓存
  - 多线程处理管道
  - 类型安全的反序列化

### 3. 消息调度器 (HighPerformanceMessageDispatcher)
- **职责**: 路由消息到对应服务，支持优先级调度
- **特性**:
  - 4级优先级调度 (Critical, High, Normal, Low)
  - 负载均衡
  - 性能统计

### 4. 服务处理器 (HighPerformanceServiceProcessor)
- **职责**: 管理服务注册，执行方法调用
- **特性**:
  - 支持依赖注入和单例模式
  - 方法调用缓存
  - 异常处理

### 5. 响应处理器 (HighPerformanceResponseProcessor)
- **职责**: 序列化响应，发送回客户端
- **特性**:
  - 多线程响应处理
  - 错误响应标准化
  - 序列化器缓存

## 运行示例

### 启动服务端
```bash
cd src/PulseRPC.Server/Examples
dotnet run
```

### 输出示例
```
=== PulseRPC 高性能服务端演示 ===
演示完整的服务端处理流程：
字节流接收 -> 消息解析 -> 反序列化 -> 消息调度 -> 服务处理 -> 响应序列化 -> 发送响应

[21:30:15] 启动高性能 PulseRPC 服务端...
[21:30:15] 传输管理器初始化完成
[21:30:15] 所有处理组件初始化完成
[21:30:15] 已注册 3 个服务
[21:30:15] 处理管道连接完成
[21:30:15] 所有处理组件已启动
[21:30:15] PulseRPC 服务端启动完成
[21:30:20] 模拟发送计算请求: 10 + 20
[21:30:20] [网络处理器] 解析消息完成: 连接=conn_1, 处理器=0
[21:30:20] [反序列化器] 反序列化完成: 服务=ICalculatorService, 方法=AddAsync
[21:30:20] [消息调度器] 开始处理服务调用: 服务=ICalculatorService, 方法=AddAsync
[21:30:20] [服务处理器] 执行加法运算: 10 + 20 = 30
[21:30:20] [响应处理器] 响应发送成功: 连接=conn_1, 耗时=2ms
```

## 关键设计特性

### 高性能特性
1. **零拷贝设计**: 使用 `Span<T>`, `Memory<T>`, `ReadOnlySequence<T>`
2. **内存池化**: 使用 `MemoryPool<byte>` 减少 GC 压力
3. **无锁编程**: 使用 `System.Threading.Channels` 进行线程安全通信
4. **缓存优化**: 序列化器和方法调用器缓存

### 可扩展性
1. **组件式架构**: 每个处理阶段都是独立组件
2. **异步处理**: 全程异步，支持高并发
3. **优先级调度**: 支持关键业务优先处理
4. **依赖注入**: 完整的 DI 支持

### 监控和诊断
1. **详细日志**: 每个处理阶段都有日志记录
2. **性能指标**: 处理时间、成功率统计
3. **错误处理**: 结构化错误响应

## 注册的示例服务

### 1. 计算服务 (ICalculatorService)
- `AddAsync(AddRequest)` - 加法运算
- `DivideAsync(DivideRequest)` - 除法运算  
- `GetVersionAsync()` - 获取版本信息

### 2. 用户服务 (IUserService)
- `GetUserAsync(GetUserRequest)` - 获取用户信息
- `CreateUserAsync(CreateUserRequest)` - 创建用户

### 3. 健康检查服务 (IHealthService)
- `CheckHealthAsync()` - 健康状态检查
- `GetServerInfoAsync()` - 获取服务器信息

## 性能测试建议

1. **负载测试**: 使用多个客户端并发请求
2. **内存分析**: 监控 GC 压力和内存使用
3. **延迟测试**: 测量端到端处理延迟
4. **吞吐量测试**: 测量每秒处理请求数

## 自定义扩展

### 添加新服务
1. 定义服务接口继承 `IPulseHub`
2. 实现服务类
3. 在 `RegisterExampleServices()` 中注册
4. 配置依赖注入

### 自定义序列化器
1. 实现 `ISerializer` 接口
2. 创建 `ISerializerProvider` 实现
3. 在依赖注入中注册

### 自定义传输层
1. 实现 `ITransportManager` 接口
2. 处理 `DataReceived` 事件
3. 实现 `SendAsync` 方法

这个示例展示了如何构建一个高性能、可扩展的 RPC 服务端，可以作为生产环境部署的参考模板。
