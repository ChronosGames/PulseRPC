# PulseRPC 客户端源代码生成器

## 简介

`PulseRPC.Client.SourceGenerator` 是PulseRPC框架的客户端代码生成器，用于自动生成消息序列化、反序列化和RPC客户端方法。该项目由原有的`PulseRPC.Generators.Core`和`PulseRPC.Generators.Client`合并而来。

## 功能

- 自动扫描标记了 `MessageAttribute` 的消息类型
- 生成消息注册表代码
- 生成消息序列化和反序列化代码
- 生成类型安全的RPC客户端API

## 使用方法

在客户端项目中添加对本项目的分析器引用：

```xml
<ItemGroup>
  <ProjectReference Include="..\PulseRPC.Client.SourceGenerator\PulseRPC.Client.SourceGenerator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

确保所有消息类型都正确标记了`MessageAttribute`特性：

```csharp
[Message(100, MessageType.Request)]
public class LoginRequest : IMessage
{
    public string Username { get; set; }
    public string Password { get; set; }
}

[Message(101, MessageType.Response)]
public class LoginResponse : IMessage
{
    public bool Success { get; set; }
    public string Token { get; set; }
    public string ErrorMessage { get; set; }
}
```

## 生成的代码

代码生成器会自动生成以下源文件：

1. `ClientMessageRegistry.g.cs` - 消息类型注册表
2. `ClientMessageSerializer.g.cs` - 消息序列化和反序列化代码
3. `RpcClient.g.cs` - 类型安全的RPC客户端方法

## 工作原理

1. 消息语法接收器扫描并收集所有标记了 `MessageAttribute` 的类型
2. 从属性中提取消息ID和消息类型
3. 根据收集到的信息生成相应的代码
4. 生成的代码会自动添加到编译过程中

## 依赖项

- PulseRPC.Core - 提供基础接口和特性定义
- Microsoft.CodeAnalysis - 用于源代码生成
