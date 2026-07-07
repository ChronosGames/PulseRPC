# 契约与序列化

契约是 PulseRPC 的核心边界。服务端、客户端和 Source Generator 都应以共享契约项目为唯一事实来源。

## 契约规则

1. Hub 接口继承 `IPulseHub`。
2. 请求和响应类型优先使用 MemoryPack。
3. 消息类型标记 `[MemoryPackable]`。
4. 公共契约避免运行时反射依赖，保持 Unity 可用。
5. 方法签名变更需要考虑协议号、生成代码和 PublicAPI 兼容性。

## 推荐项目划分

```text
MyGame.Contracts/
  Hubs/
  Messages/
  Receivers/

MyGame.Server/
  Hubs/
  Services/

MyGame.Client/
  Program.cs
```

共享契约项目同时被服务端、客户端和生成器引用。Unity 场景中可使用源码复制、文件链接或独立 Unity 兼容包方式引入。

## 序列化建议

- 小消息使用不可变或简单 DTO。
- 大对象避免把连接、服务、Logger、数据库上下文等运行时资源放入消息。
- 对版本敏感的数据增加兼容字段，而不是复用旧字段改变语义。
- Actor 快照只保存业务状态，不保存进程内资源。

## 验证

契约或序列化修改至少运行：

```bash
dotnet test tests/PulseRPC.SourceGenerator.Tests/PulseRPC.SourceGenerator.Tests.csproj
dotnet test tests/PulseRPC.Server.Tests/PulseRPC.Server.Tests.csproj
dotnet test tests/PulseRPC.Client.Tests/PulseRPC.Client.Tests.csproj
```

## 相关文档

- [Source Generator 模型](../concepts/source-generation.md)
- [参考手册](../reference/index.md)

