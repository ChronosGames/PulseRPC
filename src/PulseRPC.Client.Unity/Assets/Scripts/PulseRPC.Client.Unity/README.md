# PulseRPC Unity 客户端辅助包

> 文档状态：当前实现说明。本文按当前 `PulseRPC.Client.Unity` 辅助包源码编写；旧 WebSocket 客户端 API 未在当前仓库实现。

该包目前提供 Unity/IL2CPP 兼容辅助，而不是独立的 Unity RPC 客户端 API。

## 包含内容

- `link.xml` 与 `AOTSupport`：保留并预热 PulseRPC 框架类型。
- `PulseRPC.Client.SourceGenerator`：按 `[PulseClientGeneration]` 契约生成 Hub Stub、Receiver Dispatcher 与 MemoryPack 泛型闭包的 `[Preserve]` AOT 根。
- `MemoryPack.Generator` 与完整 `netstandard2.1` CopyLocal 运行时 DLL 闭包。
- `TaskExtensions`：Unity 环境下的任务辅助扩展。
- `PulseRPC.Client.Unity.asmdef` 与 `package.json`：Unity 包元数据。
- `Samples~/BasicExample`：可从 Package Manager 导入的 Hub/Receiver 生成示例。
- `Documentation~/dependency-closure.json`：每个运行时 DLL、analyzer、版本、引用和 SHA-256。

## 使用建议

实际连接和 Hub 调用请使用 `PulseRPC.Client` 与 Source Generator 生成的代理。安装 GitHub Release
中的版本化 `.tgz` 即可得到所需闭包；仓库包源目录本身不包含构建 DLL。可参考：

- Package Manager 中的 `Basic RPC Example` sample
- `docs/getting-started/unity-client-tutorial.md`

`PulseRPC.Client.SourceGenerator.dll` 在 Unity 中必须作为 `RoslynAnalyzer` 导入，不能放进 asmdef 的运行时 `precompiledReferences`。

当前源码未实现 `PulseWebSocketConnection`；请勿按旧 WebSocket 示例编写代码。
