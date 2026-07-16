# PulseRPC.Client.Unity Scripts

本目录当前只包含 Unity 侧辅助脚本、AOT 支持和运行时测试工具，不包含旧文档所描述的 `ChannelManager`、`TransportFactory`、`PulseRPCSerializer` 或 Unity 专用连接客户端。

## 当前结构

```
Assets/Scripts/
├── Editor/                         # Unity 编辑器工具
├── PulseRPC.Client.Unity/
│   ├── AOT/AOTSupport.cs           # IL2CPP/AOT 类型预热
│   ├── Extensions/TaskExtensions.cs
│   ├── Samples~/BasicExample/      # 可从 Package Manager 导入的 Hub/Receiver 示例
│   ├── PulseRPC.Client.Unity.asmdef
│   └── package.json
└── RuntimeUnitTestToolkit/          # Unity 运行时测试辅助
```

正式 UPM tarball 内含 `Samples~/BasicExample`，可从 Package Manager 的 Samples 面板导入。

当前核心传输类型只有 TCP 和 KCP；旧文档中的 WebSocket、BinaryFormatter 默认序列化等描述不符合当前实现。
