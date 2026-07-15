# Source Generator 模型

PulseRPC 使用 Source Generator 生成客户端代理、服务端路由表、协议号映射和响应序列化支持，目标是减少运行时反射并兼容 Unity 客户端。

## 生成器分工

- `PulseRPC.Client.SourceGenerator`：生成客户端 Hub 代理、Receiver Dispatcher、泛型扩展和 Unity AOT preservation roots。
- `PulseRPC.Server.SourceGenerator`：生成服务端路由表、服务清单、响应序列化器、Receiver 推送代理和强类型 `IPulseRouter` 出站代理。

## 兼容性边界

客户端生成代码必须保持 C# 9.0 及以下语法，避免破坏 Unity 场景。服务端生成代码可以使用更高版本 C#，但应避免把生成结果绑定到不必要的运行时反射。

Unity 编译中，客户端生成器会额外生成带 `[UnityEngine.Scripting.Preserve]` 的根，显式闭合：

- `GetHub<THub>()` 与对应 Stub。
- `RegisterReceiver<TReceiver>()` 与对应 Dispatcher。
- 契约实际使用的 MemoryPack 请求、多参数元组和响应类型泛型闭包。

Unity 包中的 `link.xml` 保留 PulseRPC 运行时程序集；用户契约的闭包由上述生成根精确提供，不依赖运行时反射或手写类型列表。
Unity 包使用不含 IDE CodeFix 的专用生成器构建，避免对 Unity Roslyn 宿主未保证提供的 `Microsoft.CodeAnalysis.Workspaces`/MEF 程序集产生加载依赖。

## 设计原则

- 契约接口是唯一事实来源。
- 协议号必须稳定、可诊断；Hub 与 Receiver 方法都支持重载，每个重载按完整 wire 签名取得独立协议号，生成成员使用协议号后缀消除名称碰撞。
- 手写路由可用 `PulseRPC.Protocol.ProtocolId.Generate(canonicalSignature)` 复用生成器的 FNV-1a 规则；规范签名格式为 `InterfaceFullName.MethodName(ParamType1,ParamType2)`，并排除 `CancellationToken`。
- `CancellationToken` 只控制调用，不进入 wire payload；服务端 Hub 注入当前 dispatch token，Receiver push/Ask 传递契约声明的 token，未声明时使用 `CancellationToken.None`。每个 RPC 方法最多声明一个 token；仅 token 有无不同的重载具有相同 wire 签名，生成器会拒绝。
- 手动协议号可使用数值或 `[Protocol("0x1234")]`；无效字符串不会回退为自动协议号，`0x0000` 是控制消息保留值，两者都会产生编译错误。
- 生成代码应可读，便于定位用户契约错误。
- 新增生成行为时必须补 SourceGenerator 测试。
- 客户端与服务端生成器均使用 `IIncrementalGenerator`；配置和语义模型输出必须具有稳定值相等语义。CI 测试会对同一 `Compilation` 连续运行两次，并拒绝第二次输出仍为 `Modified`。
- 指定连接注册 Receiver 时，生成扩展会把对应 Dispatcher 直接注册到该连接上下文；不支持的契约形态必须产生生成器诊断，不能生成空 token 或静默 no-op。

只有 `Provide = true` 的服务端 Hub 会生成路由表、响应序列化器、服务清单及其 ModuleInitializer。运行时注册中心提供组合视图，因此多个 provider 程序集的结果会同时进入 DI；跨程序集重复的 `(Hub, ProtocolId)` 或响应协议号会显式失败。

## 服务端出站 Router 代理

服务端对显式 `Consume = true` 的非 `CLIENT` Hub 生成 `{Hub}RouterProxy`。例如：

```csharp
[PulseHub(Provide = false, Consume = true)]
public interface IGameHub : IPulseHub
{
    Task<PlayerState> GetPlayerAsync(string playerId, CancellationToken cancellationToken = default);
}
```

生成的 `GameHubRouterProxy` 直接将强类型调用序列化后发给 `IPulseRouter`，并提供 `ForActor(router, key, nodeId)` 入口。`Provide = false` 的纯 consumer 程序集不生成服务路由、registry 注册或 ModuleInitializer。普通未标注 Hub 仍只按服务端 provider 处理，不会意外增加出站代理。

## 常见修改入口

- 客户端代理：`src/PulseRPC.Client.SourceGenerator/Generators/`
- 服务端路由：`src/PulseRPC.Server.SourceGenerator/Generators/`
- 协议号工具：两个 SourceGenerator 项目中的 `ProtocolId*`
- 分析器：`src/PulseRPC.Server.SourceGenerator/Analyzers/`

## 相关文档

- [契约与序列化](../guides/contracts-and-serialization.md)
- [迁移指南](../guides/migration.md)
- [测试指南](../guides/testing.md)
