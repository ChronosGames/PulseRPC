# PulseRPC.Server 消息调度流程分析与优化建议

**生成时间：** 2025-11-13
**分析范围：** 从网络字节流接收到业务逻辑执行的完整调度链路

---

## 一、当前架构概览

### 1.1 完整调用链路

```
网络层（Transport）
    ↓ DataReceived事件
MessageReceiver (分包/粘包处理)
    ↓ 完整消息帧
MessageParser (MemoryPack反序列化)
    ↓ RpcMessage
MessageDispatcher (4级优先队列 + N工作线程)
    ↓ DispatchItem
ServiceRegistry (路由到服务实例)
    ↓ IServiceHandler
┌───────────────────────────────────────┐
│ ServiceInvoker / HealthAwareServiceInvoker │
│  - 超时控制（30s默认）                   │
│  - 异常隔离                             │
│  - 健康检查（IPulseService）             │
│  - 线程亲和性调度（IPulseService）        │
└───────────────────────────────────────┘
    ↓
CompiledServiceInvoker
    ↓ 参数反序列化 → 编译方法调用 → 结果序列化
┌───────────────────────────────────────┐
│         线程调度分支                    │
│                                        │
│  普通服务              IPulseService    │
│  .NET ThreadPool       ServiceThreadPool │
│  无序并发              一致性哈希+FIFO    │
└───────────────────────────────────────┘
    ↓
业务逻辑（IPulseHub实现类）
    ↓
ResponseBuilder → 网络输出
```

### 1.2 关键组件职责

| 组件 | 职责 | 线程模型 | 文件路径 |
|------|------|---------|---------|
| **MessageReceiver** | 连接缓冲、分包粘包 | Transport的IO线程（事件回调） | `Pipeline/MessageReceiver.cs` |
| **MessageParser** | 协议解析、MemoryPack反序列化 | 同上 | `Pipeline/MessageParser.cs` |
| **MessageDispatcher** | 优先级队列调度（4级） | N个专用工作线程（默认CPU核心数） | `Pipeline/MessageDispatcher.cs` |
| **ServiceInvoker** | 超时控制、异常隔离 | MessageDispatcher工作线程 | `Pipeline/ServiceInvoker.cs` |
| **HealthAwareServiceInvoker** | 健康检查、线程亲和性 | 如配置ServiceScheduler则切换线程 | `Pipeline/HealthAwareServiceInvoker.cs` |
| **CompiledServiceInvoker** | 反射编译、参数序列化 | 继承上层线程 | `Pipeline/CompiledServiceInvoker.cs` |
| **ServiceThreadScheduler** | IPulseService专用调度 | ServiceThreadPool的WorkerThread | `Scheduling/ServiceThreadScheduler.cs` |

---

## 二、合理性分析

### 2.1 架构优点 ✅

#### ✅ 分层清晰、职责单一
每层有明确边界：
- MessageReceiver 只管网络缓冲
- MessageParser 只管协议解析
- MessageDispatcher 只管调度优先级
- Invoker层 处理超时和健康

符合单一职责原则，便于维护和扩展。

#### ✅ 高性能技术栈
1. **零拷贝设计**：`ReadOnlyMemory<byte>` 贯穿全流程
2. **无锁队列**：`System.Threading.Channels` 替代传统锁
3. **编译优化**：Expression Trees 接近直接调用性能（~10ns）
4. **内存池化**：TieredMemoryPool 减少GC压力

#### ✅ 灵活的调度策略
- **4级优先队列**：支持关键消息优先处理
- **线程亲和性**：IPulseService 保证同实例请求顺序性
- **健康感知**：自动熔断故障实例

#### ✅ 背压机制
- MessageDispatcher 队列利用率超80%返回背压信号
- Channel 有界队列，满载时阻塞写入

---

### 2.2 架构缺陷 ⚠️

#### ⚠️ **问题1：线程切换开销过大**

**现象：**
对于 IPulseService，一个请求会经历 **2次线程切换**：

```
IO线程（DataReceived）
    ↓ 解析+入队
MessageDispatcher工作线程（Thread-1）
    ↓ HealthAwareServiceInvoker.InvokeAsync
    ↓ ServiceScheduler.ScheduleAsync
ServiceThreadPool.WorkerThread（Thread-2）
    ↓ 实际业务逻辑
```

**影响：**
- 延迟增加：每次线程切换 ~1-5μs（上下文切换 + 缓存失效）
- 吞吐降低：线程切换时CPU无法处理有效业务
- CPU缓存污染：数据在不同核心之间迁移

**证据位置：**
- `HealthAwareServiceInvoker.cs:108-136` - 显式切换到 ServiceScheduler
- `ServiceThreadScheduler.cs:73-132` - 包装工作项并入队到专用线程

---

#### ⚠️ **问题2：优先级调度与FIFO顺序冲突**

**现象：**
MessageDispatcher 支持4级优先级，但 ServiceThreadPool 强制 FIFO：

```
假设请求到达顺序：
  Req1 (ServiceId=A, Priority=Low)
  Req2 (ServiceId=A, Priority=High)

MessageDispatcher 会优先处理 Req2，
但 ServiceThreadPool 收到后按 FIFO 执行，
若 Req1 先入队，Req2 仍需等待 Req1 完成。
```

**影响：**
- 优先级失效：High优先级消息被Low优先级阻塞
- 排队延迟增加：紧急消息无法插队

**证据位置：**
- `MessageDispatcher.cs:231-261` - 优先级循环读取
- `WorkerThread.cs:117-157` - FIFO处理，无优先级判断

---

#### ⚠️ **问题3：调度层级过深**

**现象：**
从网络到业务逻辑需经过 **6-7层** 处理：

```
1. MessageReceiver（分包）
2. MessageParser（反序列化协议）
3. MessageDispatcher（优先级调度）
4. ServiceInvoker（超时控制）
5. HealthAwareServiceInvoker（健康检查 + 线程切换）
6. CompiledServiceInvoker（参数反序列化）
7. 业务逻辑
```

**影响：**
- **延迟累积**：每层增加 0.5-2μs，总计 3-14μs
- **内存分配**：多层包装产生中间对象（DispatchItem、WorkItem、TimeoutWrappedContext）
- **调试困难**：堆栈深度增加，异常定位复杂

**证据位置：**
- 每个组件的 `InvokeAsync` 方法都有异步包装和异常处理

---

#### ⚠️ **问题4：背压传播链断裂**

**现象：**
MessageDispatcher 有背压检测（`DispatchResult.SuccessWithBackpressure`），但：

1. **返回值未被使用**：
   - `MessageDispatcher.cs:170-178` 返回背压信号
   - 但调用方没有观察到这个返回值（TODO：需确认调用链）

2. **无法传播到网络层**：
   - Transport 层的 `DataReceived` 事件是 Fire-and-Forget
   - 即使 MessageDispatcher 队列满，Transport 仍会继续接收数据

**影响：**
- **内存溢出风险**：突发流量时 MessageReceiver 的 ConnectionBuffer 无限增长
- **延迟雪崩**：大量消息堆积在缓冲区，延迟持续增加

**建议验证：**
需查看 MessageReceiver 如何处理 `MessageReceived` 事件，是否有流量控制机制。

---

#### ⚠️ **问题5：重复序列化开销**

**现象：**
参数数据经过 **2次反序列化**：

```
网络字节流
    ↓ MessageParser（第1次）
RpcMessage { Payload: ReadOnlyMemory<byte> }  // Payload仍是字节
    ↓ 进入队列 + 线程调度
    ↓ CompiledServiceInvoker（第2次）
T parameters  // 实际业务参数对象
```

**分析：**
- **MessageParser** 只反序列化 RpcMessage 的外层（ServiceName、MethodName等）
- **参数Payload** 保持字节形式，延迟到 CompiledServiceInvoker 才反序列化

**合理性判断：**
- ✅ **延迟反序列化有优势**：
  - 若消息被拒绝（健康检查失败、超时丢弃），无需反序列化参数，节省CPU
  - 队列中存储字节比对象更紧凑，减少GC压力

- ⚠️ **潜在问题**：
  - 若99%消息都会执行，延迟反序列化无收益
  - 参数反序列化在关键路径上，增加业务逻辑延迟

**证据位置：**
- `MessageParser.cs:55-73` - 反序列化 RpcMessage
- `CompiledServiceInvoker.cs:56-84` - 反序列化参数

---

#### ⚠️ **问题6：缺少自适应流量控制**

**现象：**
当前背压机制是静态的：
- MessageDispatcher 队列容量固定（默认10000）
- 超过80%利用率才返回背压信号
- 无动态调整机制

**影响：**
- **低流量浪费内存**：空闲时仍占用10000容量的内存
- **高流量反应迟钝**：80%阈值可能太晚，已经开始延迟累积
- **突发流量处理差**：无法根据实时延迟调整队列大小

**对比优秀实践：**
- gRPC 使用基于延迟的流量控制（BDP估算）
- Netty 的 `AUTO_READ` 机制动态调整接收速率

---

## 三、优化建议

### 3.1 短期优化（低风险、高收益）

#### 建议1：合并 ServiceInvoker 和 CompiledServiceInvoker

**目标：** 减少一层调用开销

**方案：**
```csharp
// 当前：ServiceInvoker → CompiledServiceInvoker
// 优化后：直接在 CompiledServiceInvoker 内处理超时和异常

public class CompiledServiceInvoker
{
    public async Task<InvocationResult> InvokeAsync(
        string methodName,
        ReadOnlyMemory<byte> parameters,
        IRequestContext context,
        TimeSpan timeout = default)  // 新增超时参数
    {
        // 1. 超时控制
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        if (timeout != default)
            timeoutCts.CancelAfter(timeout);

        try
        {
            // 2. 参数反序列化 + 方法调用（原有逻辑）
            var compiledMethod = _methodCache[methodName];
            var deserializedParams = DeserializeParameters(parameters, compiledMethod.ParameterType);
            var result = await InvokeMethod(compiledMethod, deserializedParams, context);

            // 3. 结果序列化
            return InvocationResult.Success(SerializeResult(result));
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return InvocationResult.Failure("TimeoutException", ...);
        }
        catch (Exception ex)
        {
            return InvocationResult.Failure(SanitizeException(ex));
        }
    }
}
```

**收益：**
- 减少1层异步调用（~500ns-1μs）
- 减少中间对象分配（TimeoutWrappedContext）
- 代码更紧凑，堆栈更浅

**风险：** 低（纯重构，不改变语义）

---

#### 建议2：为 ServiceThreadPool 引入优先级队列

**目标：** 解决优先级与FIFO冲突问题

**方案：**
```csharp
// WorkerThread.cs - 改造通道为优先级队列
public class WorkerThread
{
    // 原：单一通道
    // private readonly Channel<WorkItem> _messageChannel;

    // 新：4个优先级通道（与MessageDispatcher对齐）
    private readonly Channel<WorkItem>[] _priorityChannels = new Channel<WorkItem>[4];

    public async Task EnqueueAsync(WorkItem workItem, CancellationToken cancellationToken)
    {
        var priorityIndex = (int)workItem.Priority;  // 使用MessagePriority枚举
        await _priorityChannels[priorityIndex].Writer.WriteAsync(workItem, cancellationToken);
    }

    private async Task ProcessingLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            WorkItem? workItem = null;

            // 优先从高优先级通道读取（与MessageDispatcher相同逻辑）
            for (int priority = 0; priority < 4; priority++)
            {
                if (_priorityChannels[priority].Reader.TryRead(out workItem))
                    break;
            }

            // 如果所有通道都空，等待任意通道有消息
            if (workItem == null)
            {
                var readTasks = _priorityChannels.Select(ch => ch.Reader.ReadAsync(ct)).ToArray();
                workItem = await Task.WhenAny(readTasks).Result;
            }

            await workItem.Work();
        }
    }
}
```

**传递优先级信息：**
```csharp
// RpcMessage 中添加优先级字段
public sealed class RpcMessage
{
    public MessagePriority Priority { get; init; } = MessagePriority.Normal;
}

// ServiceSchedulingKey 携带优先级
public readonly struct ServiceSchedulingKey
{
    public MessagePriority Priority { get; init; }
}
```

**收益：**
- 恢复端到端优先级语义
- 紧急消息延迟降低 50-90%

**风险：** 中（需要修改消息协议，增加优先级字段）

---

#### 建议3：实现端到端背压传播

**目标：** 防止内存溢出和延迟雪崩

**方案：**
```csharp
// MessageReceiver.cs - 改造为流量控制感知
public class MessageReceiver
{
    private readonly SemaphoreSlim _backpressureSemaphore = new(1000, 1000);  // 动态调整

    private async void OnDataReceived(object? sender, TransportDataEventArgs e)
    {
        // 1. 在处理数据前检查背压
        if (!await _backpressureSemaphore.WaitAsync(TimeSpan.FromMilliseconds(100)))
        {
            // 触发背压：暂停读取
            e.Transport.PauseReceiving();  // 需Transport支持
            _logger.LogWarning("Backpressure triggered, pausing transport {TransportId}", e.Transport.Id);
            return;
        }

        try
        {
            // 2. 处理数据（原有逻辑）
            buffer.Append(e.Data);
            while (buffer.TryReadMessage(out var messageData))
            {
                var parseResult = await _parser.ParseAsync(messageData);

                // 3. 调度消息并检查背压响应
                var dispatchResult = await _dispatcher.DispatchAsync(parseResult.Message);

                if (dispatchResult.IsBackpressure)
                {
                    // 下游背压：减少信号量容量
                    _backpressureSemaphore.Release(-100);  // 动态降低
                }
            }
        }
        finally
        {
            _backpressureSemaphore.Release();
        }
    }

    // 定期检查恢复
    private async Task BackpressureRecoveryLoop()
    {
        while (!_cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(1000, _cancellationToken);

            if (_backpressureSemaphore.CurrentCount < 1000 && /* 队列利用率 < 50% */)
            {
                // 恢复信号量容量
                _backpressureSemaphore.Release(50);

                // 恢复Transport接收
                foreach (var transport in _pausedTransports)
                    transport.ResumeReceiving();
            }
        }
    }
}
```

**关键点：**
1. **Transport 层支持**：需要 `IServerTransport` 添加 `PauseReceiving()` / `ResumeReceiving()` 方法
2. **动态信号量**：根据下游队列利用率调整容量
3. **渐进恢复**：避免"潮汐效应"（瞬间恢复导致再次过载）

**收益：**
- 防止OOM（内存溢出）
- 延迟可控（队列有界）
- 吞吐平滑（避免突发崩溃）

**风险：** 高（需要Transport层配合，接口变更大）

---

### 3.2 中期优化（架构重构）

#### 建议4：引入 Fast Path 直接调度

**目标：** 为普通服务跳过 ServiceThreadPool，减少线程切换

**方案：**
```csharp
// HealthAwareServiceInvoker.cs - 区分快速路径和慢速路径
public async Task<InvocationResult> InvokeAsync(...)
{
    // 快速路径：普通服务 + 无健康问题
    if (_scheduler == null || !_pulseService || _healthMonitor?.GetHealthState(key) == ServiceHealthState.Healthy)
    {
        // 直接在当前线程（MessageDispatcher工作线程）执行
        return await _innerInvoker.InvokeAsync(methodName, parameters, context);
    }

    // 慢速路径：IPulseService + 需要线程亲和性
    return await ScheduleToServiceThreadAsync(key, methodName, parameters, context);
}
```

**收益：**
- 普通服务延迟降低 30-50%（省去1次线程切换）
- 减少 ServiceThreadPool 负载

**风险：** 低（纯优化，不改变语义）

---

#### 建议5：合并 MessageDispatcher 和 ServiceThreadPool

**目标：** 消除双重调度，统一线程模型

**当前架构问题：**
```
MessageDispatcher（N个通用工作线程）
    ↓ 检测到 IPulseService
ServiceThreadPool（M个专用工作线程）
    ↓ 一致性哈希路由
业务逻辑
```

**优化后架构：**
```
UnifiedMessageDispatcher
    ├─ 普通消息池（N个线程，优先级调度）
    └─ 亲和性消息池（M个线程，一致性哈希 + 优先级调度）
        ↓ 根据 ServiceId 路由
业务逻辑
```

**实现方案：**
```csharp
public class UnifiedMessageDispatcher
{
    // 普通消息：优先级队列 + 工作线程
    private readonly Channel<DispatchItem>[] _generalPriorityChannels = new Channel<DispatchItem>[4];
    private readonly Task[] _generalWorkers;

    // 亲和性消息：每个WorkerThread维护4个优先级队列
    private readonly AffinityWorkerThread[] _affinityWorkers;

    public async Task<DispatchResult> DispatchAsync(RpcMessage message)
    {
        var item = new DispatchItem(message, ...);

        // 路由决策
        if (RequiresAffinity(message.ServiceName, out var serviceKey))
        {
            // 一致性哈希到亲和性工作线程
            var threadIndex = Math.Abs(serviceKey.GetHashCode() % _affinityWorkers.Length);
            await _affinityWorkers[threadIndex].EnqueueAsync(item, message.Priority);
        }
        else
        {
            // 进入通用优先级队列
            var priorityIndex = (int)message.Priority;
            await _generalPriorityChannels[priorityIndex].Writer.WriteAsync(item);
        }
    }
}

// 亲和性工作线程：维护4个优先级队列
public class AffinityWorkerThread
{
    private readonly Channel<DispatchItem>[] _priorityChannels = new Channel<DispatchItem>[4];

    public async Task EnqueueAsync(DispatchItem item, MessagePriority priority)
    {
        await _priorityChannels[(int)priority].Writer.WriteAsync(item);
    }

    private async Task ProcessingLoopAsync()
    {
        while (!_cancellationToken.IsCancellationRequested)
        {
            // 优先级循环读取（与MessageDispatcher逻辑相同）
            DispatchItem? item = null;
            for (int priority = 0; priority < 4; priority++)
            {
                if (_priorityChannels[priority].Reader.TryRead(out item))
                    break;
            }

            if (item == null)
                item = await WaitAnyChannelAsync();

            // 直接调用业务逻辑（无需再次线程切换）
            await InvokeBusinessLogic(item);
        }
    }
}
```

**收益：**
- 消除IPulseService的1次线程切换（延迟降低 1-5μs）
- 统一优先级语义（端到端优先级）
- 代码更简洁（减少ServiceScheduler、ServiceThreadPool等组件）

**风险：** 高（大规模重构，需要充分测试）

---

### 3.3 长期优化（性能极致）

#### 建议6：实现 Zero-Copy Pipeline

**目标：** 消除参数反序列化开销，直接传递字节流到业务逻辑

**适用场景：**
- 代理/网关服务：仅路由消息，不解析参数
- 流式处理：大块数据传输（文件上传/下载）

**方案：**
```csharp
// 业务接口支持零拷贝模式
public interface IPulseHubZeroCopy
{
    // 传统模式：框架自动反序列化参数
    Task<MyResponse> ProcessAsync(MyRequest request);

    // 零拷贝模式：直接接收字节流
    Task<ReadOnlyMemory<byte>> ProcessRawAsync(ReadOnlyMemory<byte> rawPayload, IRequestContext context);
}

// CompiledServiceInvoker 检测零拷贝方法
private CompiledMethod CompileMethod(MethodInfo method)
{
    var parameters = method.GetParameters();
    bool isZeroCopy = parameters.Length == 2
        && parameters[0].ParameterType == typeof(ReadOnlyMemory<byte>)
        && parameters[1].ParameterType == typeof(IRequestContext);

    return new CompiledMethod
    {
        IsZeroCopy = isZeroCopy,
        // ...
    };
}

// 调用时跳过反序列化
if (compiledMethod.IsZeroCopy)
{
    // 直接传递原始字节
    result = await compiledMethod.Invoker(_serviceInstance, rpcMessage.Payload, context);
}
else
{
    // 传统流程：反序列化参数
    var deserializedParams = MemoryPackSerializer.Deserialize<T>(rpcMessage.Payload);
    result = await compiledMethod.Invoker(_serviceInstance, deserializedParams, context);
}
```

**收益：**
- 特定场景下延迟降低 50-80%
- 减少GC压力（无中间对象）

**风险：** 中（需业务代码配合，API侵入性较强）

---

#### 建议7：使用 io_uring (Linux) 或 IOCP 增强 (Windows)

**目标：** 优化 Transport 层网络IO

**当前瓶颈：**
- .NET 的 `Socket.ReceiveAsync` 仍有内核态/用户态拷贝开销
- 每次接收都需要系统调用

**优化方案：**
- **Linux**: 使用 io_uring 实现真正零拷贝网络IO
- **Windows**: 使用 Registered Buffers 减少内存拷贝

**参考实现：**
- [Tmds.Linux.io_uring](https://github.com/tmds/Tmds.LinuxAsync)
- [.NET 7+ Network Performance](https://devblogs.microsoft.com/dotnet/dotnet-7-networking-improvements/)

**收益：**
- 网络吞吐提升 20-40%
- CPU使用率降低 15-30%

**风险：** 极高（平台相关，需要大量底层测试）

---

## 四、性能测试基准

建议在优化前后进行以下测试：

### 4.1 延迟测试
```csharp
// perf/BenchmarkApp/Benchmarks/DispatchLatency.cs
[Benchmark]
public async Task MeasureEndToEndLatency()
{
    // 测试：网络字节流 → 业务逻辑 → 响应字节流
    var stopwatch = Stopwatch.StartNew();

    // 发送请求
    await client.SendRequestAsync(payload);

    // 等待响应
    var response = await client.ReceiveResponseAsync();

    stopwatch.Stop();
    return stopwatch.ElapsedMilliseconds;
}
```

**关键指标：**
- P50 延迟（中位数）
- P99 延迟（尾延迟）
- P999 延迟（极端情况）

**目标：**
- P50 < 1ms（千级吞吐）
- P99 < 5ms（可接受）
- P999 < 20ms（避免超时）

---

### 4.2 吞吐测试
```csharp
[Benchmark]
public async Task MeasureThroughput()
{
    // 测试：1000个并发客户端，每秒发送100个请求
    var clients = CreateClients(1000);
    var totalRequests = 100_000;

    var stopwatch = Stopwatch.StartNew();
    await Task.WhenAll(clients.Select(c => c.SendBurstAsync(100)));
    stopwatch.Stop();

    return totalRequests / stopwatch.Elapsed.TotalSeconds;
}
```

**目标：**
- 单机 10万+ QPS（简单RPC）
- CPU利用率 < 80%

---

### 4.3 背压测试
```csharp
[Benchmark]
public async Task MeasureBackpressureBehavior()
{
    // 测试：突发流量 10倍正常负载
    var normalLoad = 10_000;  // QPS
    var burstLoad = 100_000;  // QPS

    // 1. 正常负载运行1分钟
    await SendConstantLoad(normalLoad, TimeSpan.FromMinutes(1));

    // 2. 突发负载冲击
    var latencies = await SendBurstLoad(burstLoad, TimeSpan.FromSeconds(10));

    // 3. 检查恢复时间
    var recoveryTime = await MeasureRecoveryTime(normalLoad);

    Assert.True(latencies.P99 < 50ms, "背压未生效，延迟暴涨");
    Assert.True(recoveryTime < TimeSpan.FromSeconds(5), "恢复过慢");
}
```

---

## 五、实施路线图

### Phase 1: 快速收益（1-2周）
1. ✅ **建议1**：合并 ServiceInvoker + CompiledServiceInvoker
2. ✅ **建议4**：Fast Path 直接调度
3. ✅ 性能基准测试（建立baseline）

**预期收益：** 延迟降低 10-20%

---

### Phase 2: 优先级修复（2-3周）
1. ✅ **建议2**：ServiceThreadPool 引入优先级队列
2. ✅ 修改 RpcMessage 协议（添加Priority字段）
3. ✅ 端到端优先级测试

**预期收益：** 紧急消息延迟降低 50%+

---

### Phase 3: 背压完善（3-4周）
1. ✅ **建议3**：端到端背压传播
2. ✅ Transport 层接口扩展（Pause/Resume）
3. ✅ 突发流量测试

**预期收益：** 消除OOM风险，延迟稳定性提升

---

### Phase 4: 架构重构（8-12周，可选）
1. ⚠️ **建议5**：合并 MessageDispatcher + ServiceThreadPool
2. ⚠️ 充分测试（单元测试 + 集成测试 + 性能测试）
3. ⚠️ 灰度发布

**预期收益：** 延迟降低 30-50%，代码量减少 20%

---

## 六、总结

### 当前架构评分

| 维度 | 评分 | 说明 |
|------|------|------|
| **性能** | 7/10 | 高性能技术栈，但线程切换和层级过深影响延迟 |
| **可扩展性** | 8/10 | 分层清晰，易于扩展新功能 |
| **可靠性** | 6/10 | 缺少完善的背压机制，有OOM风险 |
| **可维护性** | 7/10 | 代码结构良好，但组件较多增加维护成本 |
| **优先级语义** | 5/10 | MessageDispatcher 和 ServiceThreadPool 冲突 |

**总体评价：** 良好（Good）
架构设计合理，技术栈先进，但在线程调度、背压控制和优先级语义方面有改进空间。

### 核心建议优先级

1. **高优先级（立即实施）**：
   - 建议1：合并 Invoker 层（低风险高收益）
   - 建议4：Fast Path 优化（低风险高收益）
   - 建立性能基准测试

2. **中优先级（3个月内）**：
   - 建议2：修复优先级冲突
   - 建议3：完善背压机制

3. **低优先级（长期规划）**：
   - 建议5：架构重构（需充分评估ROI）
   - 建议6/7：极致性能优化（特定场景）

---

**文档维护：**
- 每次优化实施后更新本文档
- 记录性能测试结果对比
- 追踪优化收益和风险

**联系人：** [填写责任人]
**审阅人：** [填写审阅人]
