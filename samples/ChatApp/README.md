# ChatApp 示例项目

## 概述

这是一个基于 PulseRPC 框架的实时聊天和游戏示例，演示了如何使用 TCP 和 KCP 传输协议进行网络通信。

**重要更新**: 该示例现已采用 **服务隔离架构**，展示了 PulseRPC 的高级特性：
- 每个聊天室对应一个独立的服务实例
- 相同房间的消息顺序处理（无需加锁）
- 不同房间的消息并发处理
- 单个房间故障不影响其他房间

## 服务隔离架构说明

### 架构概述

ChatApp 采用基于 `ServiceSchedulingKey` 的服务隔离架构：

```
客户端请求 → ChatHub (调度器) → ChatRoomService 实例 (房间1)
                                → ChatRoomService 实例 (房间2)
                                → ChatRoomService 实例 (房间3)
```

### 核心组件

1. **ChatRoomService** (`ChatApp.Server/ChatRoomService.cs`)
   - 继承自 `BaseService`，实现 `IChatHub` 和 `IPulseService`
   - 每个房间有独立的服务实例
   - `ServiceId` 格式: `"ChatRoom:{RoomName}"`
   - 消息通过 `AuthenticatedActorMessageQueue` 顺序处理

2. **ChatRoomManager** (`ChatApp.Server/ChatRoomManager.cs`)
   - 管理 ChatRoomService 实例的生命周期
   - 提供房间创建、查找和移除功能
   - 确保每个房间只有一个服务实例

3. **ChatHub** (`ChatApp.Server/ChatHub.cs`)
   - 作为前端调度器，将请求路由到对应的房间服务实例
   - 维护用户到房间的映射关系
   - 处理跨房间的用户管理

### 服务隔离特性

#### 1. 并发安全性
- 相同房间的所有消息在同一线程顺序处理
- 无需使用锁或其他同步原语
- 状态变更顺序可预测

#### 2. 故障隔离
- 单个房间的异常不会影响其他房间
- 每个房间有独立的错误处理机制

#### 3. 可扩展性
- 不同房间的消息可并发处理（不同线程）
- 支持大量房间同时运行
- 线程资源自动分配和管理

#### 4. 权限验证
- 基于 `[RequirePermission]` 特性的细粒度权限控制
- 自动传递认证上下文
- 支持内部服务调用和外部用户调用

## 已知问题

### KCP 传输层问题

**问题描述**：当前 KCP 传输层实现仅为示例代码，无法正常工作。这会导致使用 KCP 通道的 RPC 调用（如 `MoveAsync`）出现超时错误。

**错误信息**：
```
System.TimeoutException: 请求 MoveAsync 超时
```

**临时解决方案**：
1. 已将 `IPlayerService.MoveAsync` 方法的通道从 `KcpChannel` 改为 `TcpChannel`
2. 这样可以确保移动请求通过 TCP 协议正常工作

**完整解决方案**：
要真正解决此问题，需要：
1. 集成真正的 KCP 库，如 [KCP.NET](https://github.com/skywind3000/kcp)
2. 替换 `src/PulseRPC.Abstractions/Transport/KcpTransport.cs` 中的简化实现
3. 实现完整的 KCP 协议支持

## 使用说明

### 快速开始

#### 1. 启动服务器
```bash
cd samples/ChatApp/ChatApp.Server
dotnet run
```

服务器启动后，你会看到：
```
=================================
  PulseRPC 高性能游戏服务器 v2.0
=================================
高性能服务器已启动，按 ESC 键停止服务器...
```

#### 2. 启动聊天客户端（演示服务隔离架构）
```bash
cd samples/ChatApp/ChatApp.Client.Console
dotnet run
```

客户端会自动演示以下功能：
1. 连接到服务器
2. 加入聊天室 "lobby"
3. 发送多条消息（顺序处理）
4. 测试异常处理（故障隔离）
5. 离开聊天室

#### 3. 启动游戏客户端（可选）
```bash
cd samples/ChatApp/ChatApp.Console
dotnet run
```

### 客户端使用示例

#### 基本聊天流程

```csharp
// 1. 创建客户端
var client = new PulseRPCClientBuilder()
    .ConfigureConnection("127.0.0.1", 7000)
    .ConfigureTransport(TransportType.Tcp)
    .Build();

await client.InitializeAsync();

// 2. 获取聊天服务代理
var chatHub = await client.GetServiceAsync<IChatHub>();

// 3. 加入房间（服务端会创建或获取对应的 ChatRoomService 实例）
var joinRequest = new JoinRequest
{
    RoomName = "lobby",  // 决定 ServiceId: "ChatRoom:lobby"
    UserName = "Alice"
};
await chatHub.JoinAsync(joinRequest);

// 4. 发送消息（消息会在房间服务实例中顺序处理）
await chatHub.SendMessageAsync("Hello, World!");

// 5. 离开房间
await chatHub.LeaveAsync();
```

#### 多房间并发示例

```csharp
// 创建多个客户端，加入不同房间
var tasks = new[]
{
    JoinAndChatAsync("room-1", "Alice"),
    JoinAndChatAsync("room-2", "Bob"),
    JoinAndChatAsync("room-3", "Charlie")
};

await Task.WhenAll(tasks);

// 不同房间的消息会并发处理（不同线程）
// 相同房间的消息会顺序处理（同一线程）
```

### Unity 客户端
1. 打开 Unity 项目 `samples/ChatApp/ChatApp.Unity`
2. 运行场景
3. Unity 客户端会自动使用服务隔离架构

## 传输协议说明

- **TCP 通道**：用于可靠的请求-响应通信（登录、聊天等）
- **KCP 通道**：原计划用于低延迟的实时数据传输（移动、位置更新等），但当前未实现

## 端口配置

- TCP 服务器：7000
- KCP 服务器：7001（当前无法使用）

## 技术细节

### ServiceSchedulingKey 路由机制

每个聊天室请求都包含一个 `ServiceSchedulingKey`：

```csharp
ServiceSchedulingKey {
    ServiceName = "ChatRoom",
    ServiceId = "ChatRoom:{RoomName}"
}
```

例如：
- 房间 "lobby" → `ServiceSchedulingKey("ChatRoom", "ChatRoom:lobby")`
- 房间 "room-1" → `ServiceSchedulingKey("ChatRoom", "ChatRoom:room-1")`

### 消息处理流程

```
1. 客户端调用 chatHub.JoinAsync(new JoinRequest { RoomName = "lobby", UserName = "Alice" })
   ↓
2. ChatHub 接收请求，提取 RoomName = "lobby"
   ↓
3. ChatRoomManager.GetOrCreateRoom("lobby")
   - 如果房间不存在，创建新的 ChatRoomService 实例
   - 设置 ServiceId = "ChatRoom:lobby"
   - 初始化 AuthenticatedActorMessageQueue
   ↓
4. 调用 roomService.InvokeAsync<bool>("JoinAsync", [request])
   ↓
5. 消息入队到 AuthenticatedActorMessageQueue
   - 设置认证上下文
   - 验证权限（如果有 [RequirePermission] 特性）
   ↓
6. ServiceThreadScheduler 根据 ServiceSchedulingKey 分配线程
   - 相同 ServiceId 总是分配到同一线程
   ↓
7. 在专属线程中顺序处理消息
   - 调用 ChatRoomService.JoinAsync(request)
   - 更新房间状态（无需加锁）
   ↓
8. 返回结果给客户端
```

### 权限验证示例

```csharp
public class ChatRoomService : BaseService, IChatHub, IPulseService
{
    // 需要 "chat.send" 权限
    [RequirePermission("chat.send")]
    public Task<bool> SendMessageAsync(string message)
    {
        // 获取当前调用者
        var caller = GetCurrentCaller();
        var userName = caller.UserId ?? caller.CallerId;

        // 处理消息（无需加锁，单线程执行）
        _messageHistory.Add(new ChatMessage
        {
            UserName = userName,
            Content = message,
            Timestamp = DateTime.UtcNow
        });

        return Task.FromResult(true);
    }

    // 仅内部服务可调用
    [InternalOnly]
    public Task<RoomStats> GetStatsAsync()
    {
        return Task.FromResult(new RoomStats
        {
            RoomId = _roomId,
            MemberCount = _members.Count,
            TotalMessages = _totalMessages
        });
    }
}
```

### 性能特性

1. **无锁设计**: 相同房间的所有操作在同一线程顺序执行，无需使用锁
2. **并发处理**: 不同房间的操作可以并发执行，充分利用多核 CPU
3. **表达式树优化**: 方法调用使用表达式树编译，性能提升 ~50 倍
4. **MethodInfo 缓存**: 反射调用缓存，首次调用后性能提升 ~20 倍

### 测试

运行集成测试以验证服务隔离特性：

```bash
cd tests/PulseRPC.IntegrationTests
dotnet test --filter "FullyQualifiedName~ChatRoomServiceIsolationTests"
```

测试覆盖：
- ✅ 相同房间的消息顺序处理
- ✅ 不同房间的消息并发处理
- ✅ 单个房间故障不影响其他房间
- ✅ 多个用户加入同一房间
- ✅ 房间服务实例的生命周期管理

# ChatApp Sample

Provides a sample of a simple chat app using PulseRPC.

Please see here about PulseRPC itself.
https://github.com/ChronosGames/PulseRPC

## Getting started

To run simple ChatApp.Server,

1. Launch `ChatApp.Server` from VisualStudio.
2. Run `ChatScene` from UnityEditor.

### ChatApp.Server

This is Sample Serverside PulseRPC.
You can lanunch via Visual Studio 2022 with .NET 8, open `MagicOnion.sln` > samples > set `ChatApp.Server` project as start up and Start Debug.

### ChatApp.Unity

Sample Clientside Unity.
You can ran with Unity from 2021.3 and higher then start on unity editor. Now unity client automatically connect to MagicOnion Server, try chat app!

## Solution configuration

We will place the C# code (Service, Hub interfaces, Request/Response objects, Logic) common to both the server and client in a Shared Project(.NET Standard class library).

This project will be referenced from Unity as a local package of UPM.

First, to reference it from Unity, place a [package.json](https://github.com/Cysharp/MagicOnion/blob/main/samples/ChatApp/ChatApp.Shared/package.json) and an [asmdef](https://github.com/Cysharp/MagicOnion/blob/main/samples/ChatApp/ChatApp.Shared/ChatApp.Shared.Unity.asmdef) inside the Shared Project.

Additionally, to ignore obj and bin in Unity, please place a [Directory.Build.props](https://github.com/Cysharp/MagicOnion/blob/main/samples/ChatApp/ChatApp.Shared/Directory.Build.props) file with the following content and change the output directories for obj and bin.

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <!--
      prior to .NET 8
      <BaseIntermediateOutputPath>.artifacts\obj\</BaseIntermediateOutputPath>
		  <BaseOutputPath>.artifacts\bin\</BaseOutputPath>
    -->

    <!-- after .NET 8: https://learn.microsoft.com/en-us/dotnet/core/sdk/artifacts-output -->
    <!-- Unity ignores . prefix folder -->
    <ArtifactsPath>$(MSBuildThisFileDirectory).artifacts</ArtifactsPath>
  </PropertyGroup>
</Project>
```

Finally, add the following line to the [Shared csproj](https://github.com/Cysharp/MagicOnion/blob/main/samples/ChatApp/ChatApp.Shared/ChatApp.Shared.csproj) to ignore the files for Unity from the server project.

```csharp
<ItemGroup>
  <None Remove="**\package.json" />
  <None Remove="**\*.asmdef" />
  <None Remove="**\*.meta" />
</ItemGroup>
```

https://github.com/Cysharp/MagicOnion/blob/main/samples/ChatApp/ChatApp.Unity/Packages/manifest.json

In the Unity project, specify the Shared project as a file reference in [Packages/manifest.json](https://github.com/Cysharp/MagicOnion/blob/main/samples/ChatApp/ChatApp.Unity/Packages/manifest.json). Since setting it up through the GUI results in a full path, it is necessary to manually change it to a relative path.

```json
{
  "dependencies": {
    "com.cysharp.magiconion.samples.chatapp.shared.unity": "file:../../ChatApp.Shared",
  }
}
```

## Code generate

MagicOnion Client is Source Generator based but still MessagePack needs generate code by command line tool.

Add the following specification to `ChatApp.Shared.csproj`.

```xml
<Target Name="RestoreLocalTools" BeforeTargets="GenerateMessagePack">
  <Exec Command="dotnet tool restore" />
</Target>

<Target Name="GenerateMessagePack" AfterTargets="Build">
  <PropertyGroup>
    <_MessagePackGeneratorArguments>-i ./ChatApp.Shared.csproj -o ../ChatApp.Unity/Assets/Scripts/Generated/MessagePack.Generated.cs</_MessagePackGeneratorArguments>
  </PropertyGroup>
  <Exec Command="dotnet tool run mpc $(_MessagePackGeneratorArguments)" />
</Target>
```
