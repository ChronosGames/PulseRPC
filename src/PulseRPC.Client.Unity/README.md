# PulseRPC.Client.Unity

当前 `PulseRPC.Client.Unity` 目录是 Unity 侧包装与 AOT 支持工程，不是独立的 WebSocket 客户端运行时。当前源码中不存在旧文档提到的 `PulseWebSocketConnection`、`PulseClientFactory`、`ChannelManager`、`TransportFactory` 等 Unity 专用 API。

## 当前包含内容

- Unity 项目/包结构与 asmdef。
- `AOTSupport`：为 Unity/IL2CPP 预热 PulseRPC 相关类型。
- `TaskExtensions`：Unity 侧任务辅助方法。
- 编辑器工具：包导出、Unity Cloud Build 配置。
- NuGetForUnity 导入的依赖包说明文件。

## 当前连接方式

Unity 客户端应复用 `PulseRPC.Client` 的 `netstandard2.1` 目标和 `PulseRPC.Client.SourceGenerator` 生成的代理。传输类型以当前核心实现为准：`TransportType.TCP` 与 `TransportType.KCP`。

示例集成请优先参考：

- `samples/ChatApp/ChatApp.Unity`
- `docs/getting-started/unity-client-tutorial.md`
- `docs/guides/client-server.md`

## 重要边界

- 当前核心 `TransportType` 未包含 WebSocket。
- 当前仓库未提供 `PulseWebSocketConnection`。
- 生产序列化建议使用 MemoryPack；不要依赖 BinaryFormatter。
