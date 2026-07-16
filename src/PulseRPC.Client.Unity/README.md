# PulseRPC.Client.Unity

当前 `PulseRPC.Client.Unity` 目录是 Unity 侧包装与 AOT 支持工程，不是独立的 WebSocket 客户端运行时。当前源码中不存在旧文档提到的 `PulseWebSocketConnection`、`PulseClientFactory`、`ChannelManager`、`TransportFactory` 等 Unity 专用 API。

## 当前包含内容

- Unity 项目/包结构与 asmdef。
- `link.xml` 与 `AOTSupport`：保留并预热 PulseRPC 框架类型。
- `PulseRPC.Client.SourceGenerator`：为用户 Hub、Receiver 和 MemoryPack wire payload 生成确定的 `[Preserve]` 泛型闭包。
- `TaskExtensions`：Unity 侧任务辅助方法。
- 编辑器工具：包导出、Unity Cloud Build 配置。
- NuGetForUnity 导入的依赖包说明文件。

正式消费入口是 GitHub Release 中版本化的
`com.chronosgames.pulserpc.client.unity-<version>.tgz`。该 tarball 包含运行时 DLL、Unity 专用
PulseRPC/MemoryPack analyzers、`link.xml`、依赖闭包清单和可导入 sample。仓库中的包源不提交
生成 DLL；维护者可用 `pwsh ./scripts/build-unity-upm.ps1` 在 `artifacts/unity-upm/` 复现产物。

## 当前连接方式

Unity 客户端应复用 `PulseRPC.Client` 的 `netstandard2.1` 目标和 `PulseRPC.Client.SourceGenerator` 生成的代理。传输类型以当前核心实现为准：`TransportType.TCP` 与 `TransportType.KCP`。

导入 Unity 时，`PulseRPC.Client.SourceGenerator.dll` 必须带 `RoslynAnalyzer` label 且不得作为 asmdef 运行时引用。每个需要代理的契约仍需要 `[PulseClientGeneration(typeof(...))]` 标记。

示例集成请优先参考：

- UPM 包内 `Samples~/BasicExample`
- `docs/getting-started/unity-client-tutorial.md`
- `docs/guides/client-server.md`

## 重要边界

- 当前核心 `TransportType` 未包含 WebSocket。
- 当前仓库未提供 `PulseWebSocketConnection`。
- 生产序列化建议使用 MemoryPack；不要依赖 BinaryFormatter。
