# PulseRPC 快速开始

本指南只使用仓库中的三项目 [HelloRPC](../../samples/HelloRPC/) 黄金路径。它由 CI 构建并执行真实的 TCP 请求，README 中不再维护另一套容易漂移的示例代码。

## 前置条件

- .NET SDK 版本以仓库根目录 `global.json` 为准。
- 在仓库根目录执行以下命令。

## 1. 构建三个项目

```bash
dotnet build samples/HelloRPC/HelloRPC.sln
```

项目职责如下：

- [`HelloRPC.Contracts`](../../samples/HelloRPC/HelloRPC.Contracts/)：定义客户端与服务端共享的 `IHelloHub`。
- [`HelloRPC.Server`](../../samples/HelloRPC/HelloRPC.Server/)：注册 Hub 实现并监听 TCP `5055`。
- [`HelloRPC.Client`](../../samples/HelloRPC/HelloRPC.Client/)：由 Source Generator 生成代理并发起 RPC 调用。

## 2. 启动服务端

```bash
dotnet run --project samples/HelloRPC/HelloRPC.Server
```

看到以下内容表示监听已就绪：

```text
HelloRPC server ready on 127.0.0.1:5055
```

## 3. 运行客户端

另开一个终端，在仓库根目录执行：

```bash
dotnet run --project samples/HelloRPC/HelloRPC.Client
```

客户端会通过生成代理调用 `IHelloHub.SayHelloAsync`。成功输出为：

```text
Hello, PulseRPC!
```

## 4. 对照源码

黄金路径的完整接线只有以下几个入口：

- 契约：[`IHelloHub.cs`](../../samples/HelloRPC/HelloRPC.Contracts/IHelloHub.cs)
- 服务端组合：[`Program.cs`](../../samples/HelloRPC/HelloRPC.Server/Program.cs)
- Hub 实现：[`HelloHub.cs`](../../samples/HelloRPC/HelloRPC.Server/HelloHub.cs)
- 客户端调用：[`Program.cs`](../../samples/HelloRPC/HelloRPC.Client/Program.cs)
- 代理生成标记：[`GeneratedProxies.cs`](../../samples/HelloRPC/HelloRPC.Client/GeneratedProxies.cs)

样例在仓库内使用 `ProjectReference` 直接验证当前源码。创建自己的项目时，契约项目引用 `PulseRPC.Abstractions`，服务端引用 `PulseRPC.Server`，客户端引用 `PulseRPC.Client`；Client/Server 包已经携带各自的 Source Generator，无需另外安装生成器包。

## 下一步

- [客户端和服务端使用指南](../guides/client-server.md)
- [契约与序列化](../guides/contracts-and-serialization.md)
- [服务端运行时](../concepts/server-runtime.md)
