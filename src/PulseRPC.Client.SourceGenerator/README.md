# PulseRPC.Client.SourceGenerator

PulseRPC 客户端源代码生成器，为 `IPulseHub` 和 `IPulseReceiver` 接口自动生成高性能代理和调度代码。

## 生成文件概览

### 按接口生成的文件

| 接口类型 | 生成文件 | 说明 |
|----------|----------|------|
| `IPulseHub` | `{Namespace}_{TypeName}.Proxy.g.cs` | 服务代理类，封装 RPC 调用 |
| `IPulseReceiver` | `{Namespace}_{TypeName}.Dispatcher.g.cs` | 接收器调度器，处理事件反序列化和分发 |
| `IPulseReceiver` | `{Namespace}_{TypeName}.SmartHandler.g.cs` | 智能事件处理器（可选，支持批处理、监控等高级功能） |

> **命名规则**：接口名称会自动去掉 `I` 前缀。例如 `IPlayerHub` 生成 `PlayerHub.Proxy.g.cs`

### 全局生成的文件

| 文件名 | 说明 |
|--------|------|
| `PulseRPC.Client.Generated.ServiceExtensions.g.cs` | Hub 服务扩展方法 |
| `PulseRPC.Client.Generated.FactoryExtensions.g.cs` | `IPulseClient` 工厂扩展方法 |
| `PulseRPC.Client.Generated.ChannelExtensions.g.cs` | `IClientChannel` 泛型扩展方法 |
| `PulseRPC.Client.Generated.SupportTypes.g.cs` | 事件处理支持类型（仅当存在 Receiver 时生成） |
| `PulseRPC.Client.Generated.UnifiedReceiverRegistration.g.cs` | 统一接收器注册扩展方法 |

## 项目结构

```
PulseRPC.Client.SourceGenerator/
├── ServiceProxyGenerator.cs              # 主入口点（IIncrementalGenerator）
├── FNV1A32.cs                            # FNV-1a 哈希算法
└── Generators/
    ├── ProtocolIdGenerator.cs            # 协议号生成
    ├── ReceiverDispatcherGenerator.cs    # Receiver 调度器生成
    ├── SmartEventHandlerGenerator.cs     # 智能事件处理器生成
    ├── PulseClientExtensionsGenerator.cs # IPulseClient 扩展方法生成
    ├── ClientChannelGenericExtensionsGenerator.cs  # IClientChannel 泛型扩展
    ├── EventHandlerSupportTypes.cs       # 支持类型生成
    └── UnifiedReceiverRegistrationGenerator.cs     # 统一注册扩展
```

## 配置选项

在项目文件 (`.csproj`) 中可配置以下选项：

```xml
<PropertyGroup>
  <!-- 是否生成 SmartHandler（默认 true） -->
  <PulseRPC_GenerateSmartHandlers>true</PulseRPC_GenerateSmartHandlers>

  <!-- 客户端通道名称列表（可选） -->
  <PulseRPC_ClientChannels>channel1;channel2</PulseRPC_ClientChannels>
</PropertyGroup>
```

### SmartHandler vs Dispatcher

| 特性 | Dispatcher | SmartHandler |
|------|------------|--------------|
| 反序列化 | ✓ | ✓ |
| 事件分发 | ✓ | ✓ |
| 批量处理 | ✗ | ✓ |
| 性能监控 | ✗ | ✓ |
| 断路器 | ✗ | ✓ |
| 代码量 | ~100 行 | ~500 行 |
| 推荐场景 | 一般用途 | 高性能/高可靠场景 |

**建议**：默认使用 `Dispatcher`（轻量），仅在需要高级功能时启用 `SmartHandler`。

## 使用示例

### 1. 定义接口

```csharp
// Hub 接口（服务端实现）
public interface IPlayerHub : IPulseHub
{
    Task<PlayerInfo> GetPlayerAsync(string playerId);
    Task UpdatePositionAsync(Vector3 position);
}

// Receiver 接口（客户端实现）
public interface IPlayerReceiver : IPulseReceiver
{
    void OnPlayerJoined(PlayerInfo player);
    void OnPlayerLeft(string playerId);
}
```

### 2. 标记生成

```csharp
[PulseClientGeneration(
    ServiceTypes = new[] { typeof(IPlayerHub) },
    EventTypes = new[] { typeof(IPlayerReceiver) }
)]
public partial class GameClient { }
```

### 3. 使用生成的代码

```csharp
// 获取 Hub 代理
var playerHub = await channel.GetHubAsync<IPlayerHub>();
var player = await playerHub.GetPlayerAsync("player123");

// 注册 Receiver
var token = channel.RegisterReceiver<IPlayerReceiver>(myReceiver);

// 或使用统一注册（注册对象实现的所有 IPulseReceiver 接口）
var tokens = channel.RegisterAllReceivers(myMultiReceiver);
```

## 协议号生成

生成器使用 **FNV-1a** 哈希算法为每个方法生成唯一的协议号，确保客户端和服务端一致。

**方法签名格式**：
```
{InterfaceFullName}.{MethodName}({ParamType1},{ParamType2},...)
```

**手动指定协议号**：
```csharp
[Protocol(0x1234)]
Task<PlayerInfo> GetPlayerAsync(string playerId);
```

## 生成代码约定

根据 `CLAUDE.md` 中的约定：

> **PulseRPC.Client.SourceGenerator 生成的代码要符合 C# 9.0 及以下的语法规范**

这是为了确保与 Unity 等旧版本 .NET 运行时的兼容性。

## 零拷贝优化

生成的代码使用缓冲池和直接内存操作，避免不必要的内存复制：

```csharp
// 生成的代码示例
var __buffer__ = _connection.RentSerializationBuffer(256);
try
{
    MemoryPackSerializer.Serialize(__buffer__, parameter);
    var __serializedRequest__ = __buffer__ is ArrayBufferWriter<byte> __abw__
        ? __abw__.WrittenMemory
        : ReadOnlyMemory<byte>.Empty;

    return await _connection.InvokeRawAsync(
        protocolId: PROTOCOL_ID,
        serializedRequest: __serializedRequest__,
        cancellationToken: token);
}
finally
{
    _connection.ReturnSerializationBuffer(__buffer__);
}
```

## 支持类型

当存在 `IPulseReceiver` 接口时，会生成以下支持类型：

- `SmartSubscriptionOptions` - 智能订阅配置（Default/Game/Critical 预设）
- `SubscriptionContext` - 订阅上下文
- `SmartSubscriptionToken` - 订阅令牌
- `EventMetrics` - 事件指标统计
- `EventHandlerMetrics` - 处理器指标
- `BatchProcessor` - 批处理器
- `MonitoredEventReceiver<T>` - 带监控的接收器包装

## 开发说明

### 添加新的生成器

1. 在 `Generators/` 目录创建新的静态类
2. 实现 `Generate` 方法返回生成的代码字符串
3. 在 `ServiceProxyGenerator.cs` 的 `RegisterSourceOutput` 中调用
4. 使用 `spc.AddSource()` 添加生成的源文件

### 调试生成器

1. 设置 `EmitCompilerGeneratedFiles` 将生成的文件输出到磁盘：
   ```xml
   <PropertyGroup>
     <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
   </PropertyGroup>
   ```

2. 生成的文件位于：`obj/{Configuration}/{TargetFramework}/generated/`
