using System;
using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace PulseRPC.SourceGenerator.Tests;

/// <summary>
/// Source Generator 集成测试
/// </summary>
public class SourceGeneratorTests
{
    /// <summary>
    /// 测试Source Generator能正确生成服务代理
    /// </summary>
    [Fact]
    public void SourceGenerator_ShouldGenerateServiceProxy_WhenPulseServiceInterfaceExists()
    {
        // Arrange
        var sourceCode = @"
using PulseRPC.Abstractions;
using MemoryPack;

[PulseService]
public interface ITestService
{
    ValueTask<string> GetDataAsync(GetDataRequest request);
}

[MemoryPackable]
public partial class GetDataRequest
{
    [MemoryPackOrder(0)]
    public string Id { get; set; } = string.Empty;
}";

        var compilation = CreateCompilation(sourceCode);
        var generator = new Pul();

        // Act
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        // Assert
        diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var generatedFiles = driver.GetRunResult().Results[0].GeneratedSources;
        generatedFiles.Should().NotBeEmpty();

        // 检查是否生成了预期的文件
        var proxyFile = generatedFiles.FirstOrDefault(f => f.HintName.Contains("TestService.Proxy"));
        proxyFile.Should().NotBeNull();

        var proxyCode = proxyFile.SourceText.ToString();
        proxyCode.Should().Contain("TestServiceProxy");
        proxyCode.Should().Contain("IGeneratedServiceProxy");
        proxyCode.Should().Contain("InvokeAsync");
        proxyCode.Should().Contain("InvokeGetDataAsyncAsync");
    }

    /// <summary>
    /// 测试Source Generator能正确生成路由表
    /// </summary>
    [Fact]
    public void SourceGenerator_ShouldGenerateRoutingTable_WhenMultipleServicesExist()
    {
        // Arrange
        var sourceCode = @"
using PulseRPC.Abstractions;
using MemoryPack;

[PulseService]
public interface ITestService1
{
    ValueTask<string> Method1Async(TestRequest request);
}

[PulseService]
[Channel(""CustomChannel"")]
public interface ITestService2
{
    ValueTask Method2Async(TestRequest request);
}

[MemoryPackable]
public partial class TestRequest
{
    [MemoryPackOrder(0)]
    public string Data { get; set; } = string.Empty;
}";

        var compilation = CreateCompilation(sourceCode);
        var generator = new PulseRPCSourceGenerator();

        // Act
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        // Assert
        diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var generatedFiles = driver.GetRunResult().Results[0].GeneratedSources;
        var routingTableFile = generatedFiles.FirstOrDefault(f => f.HintName == "ServiceRoutingTable.g.cs");

        routingTableFile.Should().NotBeNull();
        var routingCode = routingTableFile.SourceText.ToString();

        routingCode.Should().Contain("ServiceRoutingTable");
        routingCode.Should().Contain("RouteAsync");
        routingCode.Should().Contain("TESTSERVICE1_SERVICE");
        routingCode.Should().Contain("TESTSERVICE2_SERVICE");
        routingCode.Should().Contain("RouteTestService1");
        routingCode.Should().Contain("RouteTestService2");
    }

    /// <summary>
    /// 测试Source Generator能正确生成序列化优化代码
    /// </summary>
    [Fact]
    public void SourceGenerator_ShouldGenerateOptimizedSerialization_WhenMemoryPackableTypesExist()
    {
        // Arrange
        var sourceCode = @"
using PulseRPC.Abstractions;
using MemoryPack;

[PulseService]
public interface ITestService
{
    ValueTask<ResponseType> ProcessAsync(RequestType request);
}

[MemoryPackable]
public partial class RequestType
{
    [MemoryPackOrder(0)]
    public string Data { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class ResponseType
{
    [MemoryPackOrder(0)]
    public string Result { get; set; } = string.Empty;
}";

        var compilation = CreateCompilation(sourceCode);
        var generator = new PulseRPCSourceGenerator();

        // Act
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        // Assert
        diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var generatedFiles = driver.GetRunResult().Results[0].GeneratedSources;
        var serializationFile = generatedFiles.FirstOrDefault(f => f.HintName == "OptimizedSerialization.g.cs");

        serializationFile.Should().NotBeNull();
        var serializationCode = serializationFile.SourceText.ToString();

        serializationCode.Should().Contain("OptimizedSerialization");
        serializationCode.Should().Contain("DeserializeMessage");
        serializationCode.Should().Contain("SerializeMessage");
        serializationCode.Should().Contain("RequestType");
        serializationCode.Should().Contain("ResponseType");
    }

    /// <summary>
    /// 测试Source Generator应该忽略没有PulseService特性的接口
    /// </summary>
    [Fact]
    public void SourceGenerator_ShouldIgnoreNonPulseServiceInterfaces()
    {
        // Arrange
        var sourceCode = @"
public interface IRegularInterface
{
    string GetData();
}

public interface IAnotherInterface
{
    void DoSomething();
}";

        var compilation = CreateCompilation(sourceCode);
        var generator = new PulseRPCSourceGenerator();

        // Act
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        // Assert
        var generatedFiles = driver.GetRunResult().Results[0].GeneratedSources;

        // 应该只生成抽象接口文件，不应该有服务代理
        generatedFiles.Should().HaveCount(2); // PulseRPC.Abstractions.g.cs 和 GenerationReport.g.cs
        generatedFiles.Should().NotContain(f => f.HintName.Contains("Proxy"));

        // 检查是否有信息性诊断
        var infoDiagnostics = diagnostics.Where(d => d.Id == "PULSE001").ToArray();
        infoDiagnostics.Should().HaveCount(1);
        infoDiagnostics[0].GetMessage().Should().Contain("No interfaces marked with [PulseService]");
    }

    /// <summary>
    /// 测试Source Generator能生成性能报告
    /// </summary>
    [Fact]
    public void SourceGenerator_ShouldGeneratePerformanceReport()
    {
        // Arrange
        var sourceCode = @"
using PulseRPC.Abstractions;

[PulseService]
public interface ITestService
{
    ValueTask<string> Method1Async(string input);
    Task<int> Method2Async(int input);
    void Method3();
}";

        var compilation = CreateCompilation(sourceCode);
        var generator = new PulseRPCSourceGenerator();

        // Act
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        // Assert
        var generatedFiles = driver.GetRunResult().Results[0].GeneratedSources;
        var reportFile = generatedFiles.FirstOrDefault(f => f.HintName == "GenerationReport.g.cs");

        reportFile.Should().NotBeNull();
        var reportCode = reportFile.SourceText.ToString();

        reportCode.Should().Contain("GenerationReport");
        reportCode.Should().Contain("TotalServices = 1");
        reportCode.Should().Contain("TotalMethods = 3");
        reportCode.Should().Contain("AsyncMethods = 2");
        reportCode.Should().Contain("SyncMethods = 1");
        reportCode.Should().Contain("EstimatedImprovements");
    }

    /// <summary>
    /// 创建测试编译上下文
    /// </summary>
    private static Compilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ValueTask).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(MemoryPack.MemoryPackableAttribute).Assembly.Location)
        };

        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
