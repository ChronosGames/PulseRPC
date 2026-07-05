# PulseRPC.Client.Unity Scripts

本目录当前只包含 Unity 侧辅助脚本、AOT 支持和运行时测试工具，不包含旧文档所描述的 `ChannelManager`、`TransportFactory`、`PulseRPCSerializer` 或 Unity 专用连接客户端。

## 当前结构

```
Assets/Scripts/
├── Editor/                         # Unity 编辑器工具
├── PulseRPC.Client.Unity/
│   ├── AOT/AOTSupport.cs           # IL2CPP/AOT 类型预热
│   ├── Extensions/TaskExtensions.cs
│   ├── PulseRPC.Client.Unity.asmdef
│   └── package.json
└── RuntimeUnitTestToolkit/          # Unity 运行时测试辅助
```

Unity 项目中实际的聊天/游戏连接示例位于 `samples/ChatApp/ChatApp.Unity/Assets/Scripts`。

当前核心传输类型只有 TCP 和 KCP；旧文档中的 WebSocket、BinaryFormatter 默认序列化等描述不符合当前实现。
