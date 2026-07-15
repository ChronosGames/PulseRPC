# Unity 客户端与 IL2CPP/AOT

本文说明 Unity 2022.3 LTS 中的当前集成方式。PulseRPC 使用 Unity 自带的 Roslyn Source Generator 管线；生成代码参与当前 Unity compilation，不会写入 `Assets/Scripts/Generated`。

## 前置条件

- Unity 2022.3 LTS。仓库 CI 固定在 `2022.3.62f3`。
- `PulseRPC.Client` 的 `netstandard2.1` 程序集。
- `PulseRPC.Client.SourceGenerator.dll` 与 MemoryPack Generator。
- iOS 构建模块（仅 iOS CI/发布需要）。

## 1. 导入运行时与生成器

使用仓库导出的 `PulseRPC.Client.Unity.unitypackage`，或将 `src/PulseRPC.Client.Unity/Assets/Scripts/PulseRPC.Client.Unity` 作为本地包导入。

Unity Inspector 中的 `PulseRPC.Client.SourceGenerator.dll` 必须满足：

- Asset Label 包含 `RoslynAnalyzer`。
- 不启用任何运行平台。
- 不出现在业务 asmdef 的 `precompiledReferences` 中。

PulseRPC Unity 包已按此方式配置。MemoryPack Generator 也必须以 Roslyn Analyzer 导入。
Unity 包中的 PulseRPC 生成器为专用构建：它保留 Generator/Analyzer，但不携带只供 IDE 使用的 CodeFix 类型，因此不需要 `Microsoft.CodeAnalysis.Workspaces` 或 MEF 依赖。
如果标记类位于自定义 asmdef，该 asmdef 必须引用 `PulseRPC.Client.Unity`；Unity 只会把位于某 asmdef 下的 Analyzer 应用到该程序集及引用它的程序集。

## 2. 声明生成入口

在与客户端契约同一 Unity compilation 中添加标记类：

```csharp
using PulseRPC;

[PulseClientGeneration(typeof(IGameHub))]
[PulseClientGeneration(typeof(IGameReceiver))]
public static class PulseRpcGenerationMarker
{
}
```

生成器将为普通 Hub 生成 Stub/泛型 `GetHub<T>()`，为 `[Channel("CLIENT")]` Receiver 生成 Dispatcher/泛型 `RegisterReceiver<T>()`。用户 DTO 应按 MemoryPack 要求声明为 `[MemoryPackable] partial`。

## 3. IL2CPP 裁剪与泛型闭包

Unity 包提供两层 preservation：

1. `link.xml` 保留 `PulseRPC.Abstractions`、`PulseRPC.Shared`、`PulseRPC.Client` 和实际导入的 MemoryPack 运行时程序集。
2. 客户端生成器为当前契约生成带 `[UnityEngine.Scripting.Preserve]` 的 AOT 根，显式闭合：
   - `GetHub<THub>()` 及 Hub Stub；
   - `RegisterReceiver<TReceiver>()` 及 Receiver Dispatcher；
   - 每个 wire request、多参数元组和 response 的 `MemoryPackSerializer.Serialize/Deserialize<T>()`。

不需要运行时调用 preservation 方法，也不需要手写契约类型列表。增删 RPC 参数或响应类型后，重新编译即会更新闭包。

## 4. 发布前验证

仓库的 `Unity 2022.3 iOS IL2CPP AOT` CI job 使用：

- Unity `2022.3.62f3`。
- iOS + IL2CPP。
- `ManagedStrippingLevel.High`。
- 同时包含 Hub、Receiver、MemoryPack request/tuple/response 的 smoke 契约。

构建后 CI 还会检查 IL2CPP C++ 输出中是否存在 Hub Stub、Receiver Dispatcher 和 MemoryPack preservation 方法。生产项目应保留一个等价的 iOS IL2CPP 高裁剪 job，而不只运行 Editor/Mono 测试。

## 故障排查

### 找不到 `IYourHubStub` 或 `YourReceiverDispatcher`

- 确认存在 `[PulseClientGeneration(typeof(...))]`。
- 确认 PulseRPC 生成器 DLL 带 `RoslynAnalyzer` label，且平台加载全部关闭。
- 确认生成器不在 asmdef 的运行时引用列表中。

### MemoryPack 在 IL2CPP 下报泛型或 formatter 错误

- 确认 DTO 是 `[MemoryPackable] partial` 并且 MemoryPack Generator 正在运行。
- 确认该 DTO 确实出现在被标记 Hub/Receiver 的 wire 参数或响应中。
- 检查 Unity 包的 `link.xml` 是否被导入。
- 用 iOS IL2CPP + High stripping 构建复现；Mono Editor 通过不能证明 AOT 闭包完整。

## 相关文档

- [Source Generator 模型](../concepts/source-generation.md)
- [客户端与服务端完整指南](../guides/client-server.md)
- [契约与序列化](../guides/contracts-and-serialization.md)
