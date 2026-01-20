# Unity Source Generator 集成指南

本文档介绍如何在Unity项目中正确使用PulseRPC的Source Generator功能。

## 问题背景

Unity使用.NET Framework 4.7.1，而现代的Source Generator通常针对.NET 6/7/8设计，这会导致兼容性问题，主要表现为：

1. **SYSLIB1102错误**：Microsoft.Extensions相关的Source Generator与.NET Framework 4.7.1不兼容
2. **Source Generator生成的代码在Unity中无法识别**：生成的代码存储在临时目录中，Unity无法访问
3. **现代C#特性不兼容**：如`scoped`关键字、nullable reference types等

## 解决方案

我们参考了MemoryPack的做法，实现了以下解决方案：

### 1. Unity兼容性检测

PulseRPC的Source Generator会自动检测Unity环境：

- 检查DefineConstants中的UNITY_相关符号
- 检查TargetFramework是否为.NET Framework 4.x
- 检查项目名称是否包含Unity

### 2. Unity兼容代码生成

当检测到Unity环境时，Source Generator会：

- 生成Unity兼容的代码（移除现代C#特性）
- 自动将生成的文件写入Unity项目的`Assets/Scripts/Generated`目录
- 创建Unity .meta文件以确保正确识别

### 3. 不兼容Source Generator过滤

通过MSBuild配置移除不兼容的Source Generator：

```xml
<ItemGroup>
  <Analyzer Remove="**\Microsoft.Extensions.Logging.Generators.dll" />
  <Analyzer Remove="**\Microsoft.Extensions.Configuration.Binder.SourceGeneration.dll" />
  <Analyzer Remove="**\System.Text.Json.SourceGeneration.dll" />
</ItemGroup>
```

## Unity项目配置

在Unity项目中创建`Directory.Build.props`文件：

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">

  <PropertyGroup>
    <!-- Unity特定配置 -->
    <UnityProject>true</UnityProject>

    <!-- 启用 PulseRPC Source Generator 文件输出到磁盘 -->
    <PulseRPC_WriteFilesToDisk>true</PulseRPC_WriteFilesToDisk>

    <!-- 设置输出目录为 Unity 项目的 Generated 文件夹 -->
    <PulseRPC_OutputFolder>$(MSBuildProjectDirectory)\Assets\Scripts\Generated</PulseRPC_OutputFolder>

    <!-- 禁用所有不兼容Unity/.NET Framework 4.7.1的Source Generator -->
    <EnableMicrosoftExtensionsLoggingSourceGenerator>false</EnableMicrosoftExtensionsLoggingSourceGenerator>
    <EnableConfigurationBindingGenerator>false</EnableConfigurationBindingGenerator>
    <EnableRequestDelegateGenerator>false</EnableRequestDelegateGenerator>
    <JsonSerializerIsReflectionEnabledByDefault>true</JsonSerializerIsReflectionEnabledByDefault>
    <UseSystemTextJsonSourceGenerator>false</UseSystemTextJsonSourceGenerator>

    <!-- 跳过分析器处理，避免Source Generator冲突 -->
    <RunAnalyzersDuringBuild>false</RunAnalyzersDuringBuild>
    <RunAnalyzersDuringLiveAnalysis>false</RunAnalyzersDuringLiveAnalysis>

    <!-- Unity 4.7.1兼容性设置 -->
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
  </PropertyGroup>

  <!-- 移除不兼容的分析器 -->
  <ItemGroup>
    <Analyzer Remove="**\Microsoft.Extensions.Logging.Generators.dll" />
    <Analyzer Remove="**\Microsoft.Extensions.Configuration.Binder.SourceGeneration.dll" />
    <Analyzer Remove="**\System.Text.Json.SourceGeneration.dll" />
  </ItemGroup>

</Project>
```

## 使用方法

1. **标记需要生成代理的类**：
   ```csharp
   [PulseClientGeneration(typeof(IPlayerService))]
   [PulseClientGeneration(typeof(IChatHub))]
   public partial class UnityGameClient
   {
       // Unity客户端代码
   }
   ```

2. **编译项目**：
   ```bash
   dotnet build YourUnityProject.csproj
   ```

3. **检查生成的文件**：
   生成的代理类会出现在`Assets/Scripts/Generated/`目录中：
   - `IPlayerServiceProxy.g.cs`
   - `IChatHubProxy.g.cs`
   - `ServiceChannelManagerExtensions.g.cs`

## 与MemoryPack的对比

| 特性 | MemoryPack | PulseRPC |
|------|------------|----------|
| Unity自动检测 | ✅ | ✅ |
| 兼容代码生成 | ✅ | ✅ |
| 文件自动写入 | ✅ | ✅ |
| .NET Framework 4.7.1支持 | ✅ | ✅ |
| Meta文件生成 | ✅ | ✅ |

## 故障排除

### SYSLIB1102错误
如果仍然遇到SYSLIB1102错误，检查：
1. 是否正确配置了`Directory.Build.props`
2. 是否移除了所有不兼容的Source Generator
3. 项目是否正确设置了`RunAnalyzersDuringBuild=false`

### 生成的文件不存在
如果`Assets/Scripts/Generated/`目录中没有文件：
1. 确认已设置`PulseRPC_WriteFilesToDisk=true`
2. 检查`PulseRPC_OutputFolder`路径是否正确
3. 验证Unity环境检测是否正常工作

### 编译错误
如果生成的代码有编译错误：
1. 检查是否使用了Unity不支持的C#特性
2. 确认目标框架设置正确
3. 验证依赖包版本兼容性

## 最佳实践

1. **在Unity项目中始终使用生成的代理类**而不是手动实现
2. **定期清理Generated目录**以避免过时的生成文件
3. **在版本控制中包含Generated目录**以便团队共享
4. **使用Unity 2022.3.12f1或更高版本**以获得最佳Source Generator支持

## 结论

通过这套解决方案，PulseRPC实现了与MemoryPack相当的Unity兼容性，能够：

- 自动检测Unity环境
- 生成Unity兼容的代码
- 避免.NET Framework兼容性问题
- 提供无缝的开发体验

这使得开发者可以在Unity项目中充分利用Source Generator的优势，同时保持与Unity生态系统的完全兼容。
