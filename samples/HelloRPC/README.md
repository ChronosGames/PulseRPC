# HelloRPC

HelloRPC 是 PulseRPC 唯一的首次上手黄金路径，由三个项目组成：

- `HelloRPC.Contracts`：共享的 `IHelloHub` 契约。
- `HelloRPC.Server`：注册契约实现并监听 TCP `5055`。
- `HelloRPC.Client`：使用 Source Generator 代理发起一次真实 RPC 调用。

从仓库根目录构建全部三个项目：

```bash
dotnet build samples/HelloRPC/HelloRPC.sln
```

先启动服务端：

```bash
dotnet run --project samples/HelloRPC/HelloRPC.Server
```

再打开另一个终端运行客户端：

```bash
dotnet run --project samples/HelloRPC/HelloRPC.Client
```

成功时客户端输出：

```text
Hello, PulseRPC!
```
