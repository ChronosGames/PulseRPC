# PulseRPC Unity 客户端辅助包

> 文档状态：当前实现说明。本文按当前 `PulseRPC.Client.Unity` 辅助包源码编写；旧 WebSocket 客户端 API 未在当前仓库实现。

该包目前提供 Unity/IL2CPP 兼容辅助，而不是独立的 Unity RPC 客户端 API。

## 包含内容

- `AOTSupport`：注册 PulseRPC、MemoryPack、TCP/KCP 相关类型，降低 IL2CPP 裁剪风险。
- `TaskExtensions`：Unity 环境下的任务辅助扩展。
- `PulseRPC.Client.Unity.asmdef` 与 `package.json`：Unity 包元数据。

## 使用建议

实际连接和 Hub 调用请使用 `PulseRPC.Client` 与 Source Generator 生成的代理。可参考：

- `samples/ChatApp/ChatApp.Unity`
- `docs/getting-started/unity-client-tutorial.md`

当前源码未实现 `PulseWebSocketConnection`；请勿按旧 WebSocket 示例编写代码。
