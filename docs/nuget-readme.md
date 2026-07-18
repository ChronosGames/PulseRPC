# PulseRPC

PulseRPC 是面向现代 .NET 与 Unity 的 TCP/KCP RPC 框架。

## 安装

按项目角色安装运行时包：

```bash
# 共享契约项目
dotnet add package PulseRPC.Abstractions

# 服务端项目
dotnet add package PulseRPC.Server

# 客户端项目
dotnet add package PulseRPC.Client
```

`PulseRPC.Server` 和 `PulseRPC.Client` 已携带各自的 Source Generator，无需另外安装生成器包。

通过 NuGetForUnity 安装 `PulseRPC.Client` 时，最低要求为 Unity 2022.3.12f1 / Roslyn 4.3；包内使用不依赖 IDE Workspaces/MEF 的 `netstandard2.0` 生成器构建。

## 唯一快速开始

完整、可运行并由 CI 端到端验证的黄金路径是三项目 HelloRPC：

- [HelloRPC 源码](https://github.com/ChronosGames/PulseRPC/tree/main/samples/HelloRPC)
- [快速开始指南](https://github.com/ChronosGames/PulseRPC/blob/main/docs/getting-started/quickstart.md)

HelloRPC 包含共享 Contracts、Server 和 Client，执行一次真实 TCP RPC 并校验返回值 `Hello, PulseRPC!`。
