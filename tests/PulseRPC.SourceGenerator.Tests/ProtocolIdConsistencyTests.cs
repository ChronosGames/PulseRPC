using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace PulseRPC.SourceGenerator.Tests;

/// <summary>
/// 协议号一致性测试 - 验证客户端和服务端生成相同的协议号
/// </summary>
public class ProtocolIdConsistencyTests
{
    /// <summary>
    /// 测试客户端和服务端为相同接口生成相同的协议号
    /// </summary>
    [Fact]
    public void ProtocolIdGenerator_ShouldGenerateSameIds_ForClientAndServer()
    {
        // Arrange - 创建一个测试接口
        var sourceCode = @"
using System.Threading.Tasks;

namespace TestNamespace
{
    public interface IChatHub
    {
        ValueTask SendMessageAsync(string message);
        ValueTask<int> GetHistoryAsync(int count);
    }
}";

        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        var compilation = CSharpCompilation.Create("TestAssembly")
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddSyntaxTrees(syntaxTree);

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot();
        
        // 获取接口符号
        var interfaceDecl = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InterfaceDeclarationSyntax>()
            .First();
        var interfaceSymbol = semanticModel.GetDeclaredSymbol(interfaceDecl) as INamedTypeSymbol;
        
        interfaceSymbol.Should().NotBeNull();

        // Act - 为每个方法生成协议号（客户端）
        var clientProtocolIds = new Dictionary<string, ushort>();
        var clientUsedIds = new Dictionary<ushort, (string service, string method)>();
        var clientManualIds = new HashSet<ushort>();

        foreach (var method in interfaceSymbol!.GetMembers().OfType<IMethodSymbol>())
        {
            var signature = PulseRPC.Generator.Generators.ProtocolIdGenerator.BuildMethodSignature(method);
            var protocolId = PulseRPC.Generator.Generators.ProtocolIdGenerator.GenerateProtocolId(
                method, clientUsedIds, clientManualIds);
            
            clientProtocolIds[method.Name] = protocolId;
            clientUsedIds[protocolId] = (interfaceSymbol.Name, method.Name);
        }

        // Act - 为相同方法生成协议号（服务端）
        // 注意：服务端使用相同的签名构建算法和哈希算法
        var serverProtocolIds = new Dictionary<string, ushort>();
        var serverUsedIds = new Dictionary<ushort, (string service, string method)>();
        var serverManualIds = new HashSet<ushort>();

        foreach (var method in interfaceSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            var signature = PulseRPC.Generator.Generators.ProtocolIdGenerator.BuildMethodSignature(method);
            var protocolId = PulseRPC.Generator.Generators.ProtocolIdGenerator.GenerateProtocolId(
                method, serverUsedIds, serverManualIds);
            
            serverProtocolIds[method.Name] = protocolId;
            serverUsedIds[protocolId] = (interfaceSymbol.Name, method.Name);
        }

        // Assert - 验证协议号一致
        clientProtocolIds.Should().HaveCount(2);
        serverProtocolIds.Should().HaveCount(2);
        
        foreach (var method in clientProtocolIds.Keys)
        {
            clientProtocolIds[method].Should().Be(serverProtocolIds[method],
                $"客户端和服务端应该为 {method} 生成相同的协议号");
        }

        // 验证协议号不为0
        clientProtocolIds.Values.Should().NotContain(0, "协议号不应该为0");
        
        // 验证没有冲突
        clientProtocolIds.Values.Distinct().Should().HaveCount(clientProtocolIds.Count,
            "所有方法应该有唯一的协议号");
    }

    /// <summary>
    /// 测试方法签名构建的一致性
    /// </summary>
    [Theory]
    [InlineData("TestNamespace.IChatHub.SendMessageAsync(System.String)", "TestNamespace.IChatHub", "SendMessageAsync", "System.String")]
    [InlineData("TestNamespace.IChatHub.GetHistoryAsync(System.Int32)", "TestNamespace.IChatHub", "GetHistoryAsync", "System.Int32")]
    public void MethodSignature_ShouldBeConsistent_ForSameMethod(
        string expectedSignature, 
        string interfaceName, 
        string methodName, 
        string paramType)
    {
        // Arrange
        var sourceCode = $@"
using System.Threading.Tasks;

namespace TestNamespace
{{
    public interface IChatHub
    {{
        ValueTask {methodName}({paramType} param);
    }}
}}";

        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        var compilation = CSharpCompilation.Create("TestAssembly")
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddSyntaxTrees(syntaxTree);

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot();
        
        var interfaceDecl = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InterfaceDeclarationSyntax>()
            .First();
        var interfaceSymbol = semanticModel.GetDeclaredSymbol(interfaceDecl) as INamedTypeSymbol;
        var method = interfaceSymbol!.GetMembers().OfType<IMethodSymbol>().First();

        // Act
        var signature = PulseRPC.Generator.Generators.ProtocolIdGenerator.BuildMethodSignature(method);

        // Assert
        signature.Should().Be(expectedSignature, 
            "方法签名格式必须一致以确保协议号生成的一致性");
    }

    /// <summary>
    /// 测试协议号不会因为参数名称变化而改变
    /// </summary>
    [Fact]
    public void ProtocolId_ShouldNotChange_WhenParameterNameChanges()
    {
        // Arrange - 相同方法，不同参数名
        var sourceCode1 = @"
using System.Threading.Tasks;

namespace TestNamespace
{
    public interface IChatHub
    {
        ValueTask SendMessageAsync(string message);
    }
}";

        var sourceCode2 = @"
using System.Threading.Tasks;

namespace TestNamespace
{
    public interface IChatHub
    {
        ValueTask SendMessageAsync(string content);  // 参数名不同
    }
}";

        // Act
        var protocolId1 = GetProtocolIdForFirstMethod(sourceCode1);
        var protocolId2 = GetProtocolIdForFirstMethod(sourceCode2);

        // Assert
        protocolId1.Should().Be(protocolId2, 
            "参数名称变化不应该影响协议号（只有类型重要）");
    }

    /// <summary>
    /// 测试协议号会因为参数类型变化而改变
    /// </summary>
    [Fact]
    public void ProtocolId_ShouldChange_WhenParameterTypeChanges()
    {
        // Arrange - 相同方法名，不同参数类型
        var sourceCode1 = @"
using System.Threading.Tasks;

namespace TestNamespace
{
    public interface IChatHub
    {
        ValueTask SendMessageAsync(string message);
    }
}";

        var sourceCode2 = @"
using System.Threading.Tasks;

namespace TestNamespace
{
    public interface IChatHub
    {
        ValueTask SendMessageAsync(int message);  // 参数类型不同
    }
}";

        // Act
        var protocolId1 = GetProtocolIdForFirstMethod(sourceCode1);
        var protocolId2 = GetProtocolIdForFirstMethod(sourceCode2);

        // Assert
        protocolId1.Should().NotBe(protocolId2, 
            "参数类型变化应该导致协议号改变（这是版本隔离的基础）");
    }

    /// <summary>
    /// 测试不同命名空间中的相同接口应该有不同的协议号
    /// </summary>
    [Fact]
    public void ProtocolId_ShouldBeDifferent_ForDifferentNamespaces()
    {
        // Arrange
        var sourceCode1 = @"
using System.Threading.Tasks;

namespace Namespace1
{
    public interface IChatHub
    {
        ValueTask SendMessageAsync(string message);
    }
}";

        var sourceCode2 = @"
using System.Threading.Tasks;

namespace Namespace2
{
    public interface IChatHub
    {
        ValueTask SendMessageAsync(string message);
    }
}";

        // Act
        var protocolId1 = GetProtocolIdForFirstMethod(sourceCode1);
        var protocolId2 = GetProtocolIdForFirstMethod(sourceCode2);

        // Assert
        protocolId1.Should().NotBe(protocolId2, 
            "不同命名空间中的相同接口应该有不同的协议号");
    }

    // 辅助方法
    private ushort GetProtocolIdForFirstMethod(string sourceCode)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        var compilation = CSharpCompilation.Create("TestAssembly")
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddSyntaxTrees(syntaxTree);

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot();
        
        var interfaceDecl = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InterfaceDeclarationSyntax>()
            .First();
        var interfaceSymbol = semanticModel.GetDeclaredSymbol(interfaceDecl) as INamedTypeSymbol;
        var method = interfaceSymbol!.GetMembers().OfType<IMethodSymbol>().First();

        var usedIds = new Dictionary<ushort, (string service, string method)>();
        var manualIds = new HashSet<ushort>();

        return PulseRPC.Generator.Generators.ProtocolIdGenerator.GenerateProtocolId(
            method, usedIds, manualIds);
    }
}

