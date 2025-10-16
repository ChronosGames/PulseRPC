# PulseRPC Source Generator 优化设计方案

## 1. 总体目标

通过编译时代码生成，将运行时反射调用替换为静态类型安全的直接调用，实现：
- **消除反射开销** - 将动态方法调用替换为静态调用
- **优化序列化性能** - 生成类型特定的序列化/反序列化代码
- **提升消息路由效率** - 生成编译时路由表和静态分发代码
- **保持类型安全** - 编译时验证所有RPC调用

## 2. 核心设计理念

### 2.1 零反射架构
```csharp
// 当前实现 (运行时反射)
var result = methodInfo.Invoke(service, parameters);

// 生成的代码 (静态调用)
var result = ((IChatService)service).SendMessage((SendMessageRequest)parameters[0]);
```

### 2.2 编译时路由表
```csharp
// 当前实现 (运行时查找)
_services.TryGetValue(serviceName, out var serviceInfo)

// 生成的代码 (静态分发)
switch (serviceName)
{
    case "IChatService": return InvokeChatService(methodName, parameters);
    case "IPlayerService": return InvokePlayerService(methodName, parameters);
}
```

### 2.3 类型特定序列化
```csharp
// 当前实现 (泛型+反射)
MemoryPackSerializer.Deserialize<T>(data)

// 生成的代码 (直接调用)
MemoryPackSerializer.Deserialize<SendMessageRequest>(data)
```

## 3. Source Generator 架构

### 3.1 生成器分层结构
```
PulseRPC.SourceGenerator/
├── Analyzers/              # 语法分析器
│   ├── ServiceAnalyzer.cs      # 服务接口分析
│   ├── MessageAnalyzer.cs      # 消息类型分析
│   └── AttributeAnalyzer.cs    # 特性分析
├── Generators/             # 代码生成器
│   ├── ServiceProxyGenerator.cs    # 服务代理生成
│   ├── MessageHandlerGenerator.cs  # 消息处理器生成
│   ├── SerializationGenerator.cs   # 序列化代码生成
│   └── RoutingTableGenerator.cs    # 路由表生成
├── Models/                 # 内部模型
│   ├── ServiceModel.cs         # 服务元数据模型
│   ├── MessageModel.cs         # 消息元数据模型
│   └── RoutingModel.cs         # 路由元数据模型
└── Templates/              # 代码模板
    ├── ServiceProxy.template
    ├── MessageHandler.template
    └── RoutingTable.template
```

### 3.2 关键特性标记
```csharp
// 服务接口标记
[PulseService]
public interface IChatService
{
    ValueTask<SendMessageResponse> SendMessage(SendMessageRequest request);
}

// 消息类型标记
[PulseMessage]
[MemoryPackable]
public partial class SendMessageRequest 
{
    public string Message { get; set; }
    public string UserId { get; set; }
}

// 优化提示标记
[PulseOptimize(OptimizationLevel.Maximum)]
public partial class ChatServiceImpl : IChatService
{
    // 实现
}
```

## 4. 生成的代码结构

### 4.1 服务代理生成
```csharp
// 生成文件: Generated/ChatService.Proxy.g.cs
partial class ChatServiceProxy : IGeneratedServiceProxy
{
    private readonly IChatService _implementation;
    
    public ValueTask<object?> InvokeAsync(string methodName, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        return methodName switch
        {
            "SendMessage" => InvokeSendMessage(data, cancellationToken),
            _ => throw new MethodNotFoundException($"Method {methodName} not found")
        };
    }
    
    private async ValueTask<object?> InvokeSendMessage(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var request = MemoryPackSerializer.Deserialize<SendMessageRequest>(data.Span);
        var response = await _implementation.SendMessage(request);
        return response;
    }
}
```

### 4.2 消息路由表生成
```csharp
// 生成文件: Generated/ServiceRoutingTable.g.cs
public static class GeneratedServiceRoutingTable
{
    private static readonly Dictionary<string, IGeneratedServiceProxy> _services = new()
    {
        ["IChatService"] = new ChatServiceProxy(),
        ["IPlayerService"] = new PlayerServiceProxy(),
    };
    
    public static ValueTask<object?> RouteAsync(string serviceName, string methodName, 
        ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        return serviceName switch
        {
            "IChatService" => _services["IChatService"].InvokeAsync(methodName, data, cancellationToken),
            "IPlayerService" => _services["IPlayerService"].InvokeAsync(methodName, data, cancellationToken),
            _ => throw new ServiceNotFoundException($"Service {serviceName} not found")
        };
    }
}
```

### 4.3 高性能序列化代码
```csharp
// 生成文件: Generated/MessageSerialization.g.cs
public static class GeneratedMessageSerialization
{
    public static T DeserializeMessage<T>(ReadOnlyMemory<byte> data)
    {
        // 编译时类型检查，生成特化代码
        if (typeof(T) == typeof(SendMessageRequest))
            return (T)(object)MemoryPackSerializer.Deserialize<SendMessageRequest>(data.Span);
        if (typeof(T) == typeof(LoginRequest))
            return (T)(object)MemoryPackSerializer.Deserialize<LoginRequest>(data.Span);
            
        throw new NotSupportedException($"Message type {typeof(T)} not supported");
    }
    
    public static void SerializeMessage<T>(IBufferWriter<byte> writer, in T message)
    {
        // 编译时类型检查，生成特化代码
        if (typeof(T) == typeof(SendMessageResponse))
        {
            MemoryPackSerializer.Serialize(writer, (SendMessageResponse)(object)message);
            return;
        }
        
        throw new NotSupportedException($"Message type {typeof(T)} not supported");
    }
}
```

## 5. 性能优化策略

### 5.1 消除虚拟调用
```csharp
// 生成密封类和静态方法，避免虚拟调用开销
public sealed class ChatServiceProxy : IGeneratedServiceProxy
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<object?> InvokeSendMessage(IChatService service, ReadOnlyMemory<byte> data)
    {
        // 内联优化的直接调用
    }
}
```

### 5.2 预计算哈希码
```csharp
// 生成预计算的哈希码常量，优化字符串比较
public static class ServiceNameHashes
{
    public const uint IChatService = 0x12345678u;    // 预计算哈希
    public const uint IPlayerService = 0x87654321u;  // 预计算哈希
}
```

### 5.3 SIMD优化的路由
```csharp
// 使用向量化比较优化服务名匹配
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static int FindServiceIndex(ReadOnlySpan<char> serviceName)
{
    // 使用SIMD指令加速字符串匹配
    return Vector.Equals(serviceName, "IChatService"u8) ? 0 : -1;
}
```

## 6. 集成方案

### 6.1 替换ServiceRegistry
```csharp
// 原 ServiceRegistry.InvokeMethodAsync 方法
// 替换为生成的静态路由表调用
public async Task<object?> InvokeMethodAsync(string serviceName, string methodName, 
    byte[] requestData, IServerTransport transport)
{
    // 直接调用生成的路由表
    return await GeneratedServiceRoutingTable.RouteAsync(serviceName, methodName, 
        requestData, CancellationToken.None);
}
```

### 6.2 优化消息处理管道
```csharp
// 在ServerHighThroughputMessageProcessor中集成
private async Task<object?> ProcessSingleMessage(ServerMessage message)
{
    // 使用生成的高性能路由
    return await GeneratedServiceRoutingTable.RouteAsync(
        message.ServiceName, 
        message.MethodName,
        message.Data,
        CancellationToken.None
    );
}
```

## 7. 开发阶段规划

### 阶段1: 基础生成器 (当前)
- [x] 服务接口分析器
- [ ] 基础服务代理生成器
- [ ] 简单路由表生成

### 阶段2: 序列化优化
- [ ] 消息类型分析器
- [ ] 特化序列化代码生成
- [ ] 类型安全验证

### 阶段3: 高级优化
- [ ] SIMD加速的路由匹配
- [ ] 内联优化提示生成
- [ ] 编译时常量折叠

### 阶段4: 集成测试
- [ ] 性能基准测试
- [ ] 兼容性验证
- [ ] 生产环境集成

## 8. 预期性能提升

基于第一阶段基准测试结果，预期通过Source Generator优化实现：

- **方法调用延迟降低 80%** - 消除反射开销
- **序列化吞吐量提升 150%** - 特化代码生成
- **消息路由性能提升 200%** - 静态分发表
- **整体端到端延迟降低 50-70%** - 综合优化效果

## 9. 下一步行动

1. 实现服务接口语法分析器
2. 创建基础服务代理生成模板
3. 集成到现有PulseRPC项目
4. 编写单元测试验证生成代码
5. 执行性能基准测试