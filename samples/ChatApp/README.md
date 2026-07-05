# ChatApp 示例项目

> 文档状态：当前示例说明。本文按当前 ChatApp 源码编写；末尾提到的旧 API 仅用于说明不要继续使用。

ChatApp 是当前仓库中用于演示 PulseRPC 服务端、客户端和 Unity 集成的聊天/游戏示例。服务端采用 `IChatRoomHub` + `ChatRoomHub` + `ChatRoomService` 的分层结构：

- `ChatApp.Shared/IChatHub.cs`：共享契约和 MemoryPack 消息类型。
- `ChatApp.Server/Services/ChatRoomHub.cs`：无状态 Hub，负责认证上下文、参数校验和路由。
- `ChatApp.Server/Services/ChatRoomService.cs`：继承 `PulseServiceBase` 的有状态房间服务，每个 RoomId 一个实例。
- `ChatApp.Server/Registration/ChatServiceRegistration.cs`：注册 `AddPulseService<ChatRoomService>()` 与 `IChatRoomHub`。
- `ChatApp.Client.Console`：当前控制台客户端入口，包含手写/生成代理示例。
- `ChatApp.Unity`：Unity 示例项目。

## 运行

### 服务端

```bash
cd samples/ChatApp/ChatApp.Server
dotnet run
```

服务端监听：

- TCP：7000
- KCP：7001

### 控制台客户端

```bash
cd samples/ChatApp/ChatApp.Client.Console
dotnet run
```

### Unity 客户端

使用 Unity 打开 `samples/ChatApp/ChatApp.Unity`。该目录是 Unity 工程，连接逻辑以工程内 `Assets/Scripts` 当前源码为准。

## 当前架构

请求处理路径：

```
Client
  -> IChatRoomHub
  -> ChatRoomHub
  -> IServiceAccessor<ChatRoomService>.GetAsync(roomId)
  -> ChatRoomService.EnqueueAsync(...)
```

`ChatRoomService` 通过 `PulseServiceBase` 的队列化执行保证同一房间内状态顺序一致，不同房间可并发运行。

## 重要边界

- 当前核心传输类型只有 `TCP` 与 `KCP`。
- 当前仓库不再使用 MagicOnion 或 MessagePack 代码生成工具；序列化优先使用 MemoryPack。
- 旧文档中出现的 `BaseService`、`AuthenticatedActorMessageQueue`、`PulseRPCClientBuilder`、`GetServiceAsync<IChatHub>()` 等写法不是当前 ChatApp 的准确入口。
- KCP 在当前服务端配置中存在，但端到端场景应先以 TCP 验证。

更多背景记录可参考同目录下的历史实施文档；这些文档描述的是迁移过程，不应替代当前 README 和源码。
