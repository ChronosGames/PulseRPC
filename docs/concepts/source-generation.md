# Source Generator 模型

PulseRPC 使用 Source Generator 生成客户端代理、服务端路由表、协议号映射和响应序列化支持，目标是减少运行时反射并兼容 Unity 客户端。

## 生成器分工

- `PulseRPC.Client.SourceGenerator`：生成客户端 Hub 代理、客户端扩展、事件处理/接收器支持。
- `PulseRPC.Server.SourceGenerator`：生成服务端路由表、服务清单、响应序列化器、协议号相关代码和分析器。

## 兼容性边界

客户端生成代码必须保持 C# 9.0 及以下语法，避免破坏 Unity 场景。服务端生成代码可以使用更高版本 C#，但应避免把生成结果绑定到不必要的运行时反射。

## 设计原则

- 契约接口是唯一事实来源。
- 协议号必须稳定、可诊断；Hub 与 Receiver 方法都支持重载，每个重载按完整 wire 签名取得独立协议号，生成成员使用协议号后缀消除名称碰撞。
- 手写路由可用 `PulseRPC.Protocol.ProtocolId.Generate(canonicalSignature)` 复用生成器的 FNV-1a 规则；规范签名格式为 `InterfaceFullName.MethodName(ParamType1,ParamType2)`，并排除 `CancellationToken`。
- `CancellationToken` 只控制本地调用，不进入 wire payload；服务端 Hub 注入当前 dispatch token，Receiver 反向 Ask 注入 handler token，单向 push 使用 `CancellationToken.None`。每个 RPC 方法最多声明一个 token；仅 token 有无不同的重载具有相同 wire 签名，生成器会拒绝。
- 手动协议号可使用数值或 `[Protocol("0x1234")]`；无效字符串不会回退为自动协议号，`0x0000` 是控制消息保留值，两者都会产生编译错误。
- 生成代码应可读，便于定位用户契约错误。
- 新增生成行为时必须补 SourceGenerator 测试。
- 客户端与服务端生成器均使用 `IIncrementalGenerator`；配置和语义模型输出必须具有稳定值相等语义。CI 测试会对同一 `Compilation` 连续运行两次，并拒绝第二次输出仍为 `Modified`。
- 指定连接注册 Receiver 时，生成扩展会把对应 Dispatcher 直接注册到该连接上下文；不支持的契约形态必须产生生成器诊断，不能生成空 token 或静默 no-op。

服务端每个程序集生成的路由表、响应序列化器和服务清单会在 ModuleInitializer 中分别注册。运行时注册中心提供组合视图，因此框架程序集与宿主程序集的生成结果会同时进入 DI；跨程序集重复的 `(Hub, ProtocolId)` 或响应协议号会显式失败，不会按加载顺序覆盖。

## 常见修改入口

- 客户端代理：`src/PulseRPC.Client.SourceGenerator/Generators/`
- 服务端路由：`src/PulseRPC.Server.SourceGenerator/Generators/`
- 协议号工具：两个 SourceGenerator 项目中的 `ProtocolId*`
- 分析器：`src/PulseRPC.Server.SourceGenerator/Analyzers/`

## 相关文档

- [契约与序列化](../guides/contracts-and-serialization.md)
- [迁移指南](../guides/migration.md)
- [测试指南](../guides/testing.md)
