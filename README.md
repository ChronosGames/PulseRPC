# PulseRPC 分离式代码生成器

本项目实现了PulseRPC协议的分离式代码生成器，通过创建专用的客户端和服务端生成器，解决了现有代码生成框架的以下问题：

1. 解决了跨程序集无法使用partial能力的问题
2. 避免了客户端与服务端代码混合的问题
3. 消除了不必要的项目依赖关系

## 项目结构

```
src/
├── PulseRPC.Generators.Core/     # 核心共享组件
├── PulseRPC.Generators.Client/   # 客户端专用生成器
└── PulseRPC.Generators.Server/   # 服务端专用生成器
```

## 技术特点

1. **高效代码生成**：使用Roslyn源生成器技术，在编译时静态生成高性能代码
2. **分离式架构**：客户端和服务端代码完全分离，避免不必要的依赖
3. **可测试设计**：包含完整的单元测试套件，确保生成器的可靠性
4. **高性能实现**：生成的代码使用高性能的switch语句和类型映射

## 使用方法

### 客户端项目配置

```xml
<ItemGroup>
  <ProjectReference Include="..\..\..\src\PulseRPC.Generators.Client\PulseRPC.Generators.Client.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
</ItemGroup>
```

### 服务端项目配置

```xml
<ItemGroup>
  <ProjectReference Include="..\..\..\src\PulseRPC.Generators.Server\PulseRPC.Generators.Server.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
</ItemGroup>
```

### 共享项目配置

```xml
<ItemGroup>
  <ProjectReference Include="..\..\..\src\PulseRPC.Core\PulseRPC.Core.csproj" />
</ItemGroup>
```

## 示例项目

`samples/MiniGame` 目录包含了一个完整的示例，展示了如何使用分离式代码生成器：

- **MiniGame.Shared**：定义共享消息结构
- **MiniGame.Client**：客户端实现（使用客户端生成器）
- **MiniGame.Server**：服务端实现（使用服务端生成器）

## 开发者指南

1. 克隆仓库：`git clone https://github.com/yourusername/PulseRPC.git`
2. 进入项目目录：`cd PulseRPC`
3. 构建解决方案：`dotnet build`
4. 运行测试：`dotnet test`

## 许可证

MIT License
