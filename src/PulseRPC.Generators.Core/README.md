# PulseRPC 代码生成器

这个项目提供了PulseRPC协议的代码生成器，用于生成消息序列化、分发和处理的代码。

## 项目结构

PulseRPC生成器已拆分为三个独立项目：

- **PulseRPC.Generators.Core**：共享的核心代码和工具类
- **PulseRPC.Generators.Client**：客户端专用代码生成器
- **PulseRPC.Generators.Server**：服务端专用代码生成器

## 使用方法

### 客户端项目

在客户端项目中添加对客户端生成器的引用：

```xml
<ItemGroup>
  <ProjectReference Include="..\..\..\src\PulseRPC.Generators.Client\PulseRPC.Generators.Client.csproj"
                   OutputItemType="Analyzer"
                   ReferenceOutputAssembly="false" />
</ItemGroup>
```

### 服务端项目

在服务端项目中添加对服务端生成器的引用：

```xml
<ItemGroup>
  <ProjectReference Include="..\..\..\src\PulseRPC.Generators.Server\PulseRPC.Generators.Server.csproj"
                   OutputItemType="Analyzer"
                   ReferenceOutputAssembly="false" />
</ItemGroup>
```

### 共享项目

在共享项目中只需引用核心库：

```xml
<ItemGroup>
  <ProjectReference Include="..\..\..\src\PulseRPC.Core\PulseRPC.Core.csproj" />
</ItemGroup>
```

## 生成的代码

### 客户端代码

客户端生成器会生成以下文件：

- **ClientMessageRegistry.g.cs**：客户端消息注册表
- **ClientMessageSerializer.g.cs**：客户端序列化助手
- **RpcClient.g.cs**：RPC客户端API

### 服务端代码

服务端生成器会生成以下文件：

- **ServerMessageRegistry.g.cs**：服务端消息注册表
- **ServerMessageSerializer.g.cs**：服务端序列化助手
- **ServerMessageDispatcher.g.cs**：服务端消息分发器

## 优势

1. 严格分离客户端和服务端代码生成
2. 避免不必要的程序集引用
3. 提高编译速度和减少内存占用
4. 简化项目依赖管理
