# ChatApp 示例项目

## 概述

这是一个基于 PulseRPC 框架的实时聊天和游戏示例，演示了如何使用 TCP 和 KCP 传输协议进行网络通信。

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

### 启动服务器
```bash
cd samples/ChatApp/ChatApp.Server
dotnet run
```

### 启动控制台客户端
```bash
cd samples/ChatApp/ChatApp.Console
dotnet run
```

### Unity 客户端
1. 打开 Unity 项目 `samples/ChatApp/ChatApp.Unity`
2. 运行场景

## 传输协议说明

- **TCP 通道**：用于可靠的请求-响应通信（登录、聊天等）
- **KCP 通道**：原计划用于低延迟的实时数据传输（移动、位置更新等），但当前未实现

## 端口配置

- TCP 服务器：7000
- KCP 服务器：7001（当前无法使用）

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
