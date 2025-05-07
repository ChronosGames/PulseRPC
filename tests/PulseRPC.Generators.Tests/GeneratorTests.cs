using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PulseRPC.Generators.Client;
using PulseRPC.Generators.Server;
using Xunit;

namespace PulseRPC.Generators.Tests;

/// <summary>
/// 代码生成器测试
/// </summary>
public class GeneratorTests
{
    /// <summary>
    /// 测试客户端生成器仅生成客户端代码
    /// </summary>
    [Fact]
    public void ClientGenerator_ShouldGenerateClientOnlyCode()
    {
        // 准备测试代码
        string testCode = @"
            using System;
            using PulseRPC.Protocol;
            using PulseRPC.Protocol.Attributes;

            namespace TestNamespace
            {
                [Message(1001, MessageType.Request)]
                public partial class TestRequest : IMessage
                {
                    public string Text { get; set; }
                }

                [Message(1002, MessageType.Response)]
                public partial class TestResponse : IMessage
                {
                    public string Result { get; set; }
                }
            }";

        // 使用客户端生成器生成代码
        var generatedSources = RunGenerator(testCode, new ClientCodeGenerator());

        // 验证生成的客户端代码
        Assert.Contains(generatedSources, s => s.Key.EndsWith("ClientMessageRegistry.g.cs"));
        Assert.Contains(generatedSources, s => s.Key.EndsWith("ClientMessageSerializer.g.cs"));
        Assert.Contains(generatedSources, s => s.Key.EndsWith("RpcClient.g.cs"));

        // 检查RpcClient代码中是否包含测试请求方法
        var rpcClientCode = generatedSources.First(s => s.Key.EndsWith("RpcClient.g.cs")).Value;
        Assert.Contains("TestAsync", rpcClientCode);

        // 验证不包含服务端相关代码
        Assert.DoesNotContain(generatedSources, s => s.Key.EndsWith("ServerMessageDispatcher.g.cs"));
    }

    /// <summary>
    /// 测试服务端生成器仅生成服务端代码
    /// </summary>
    [Fact]
    public void ServerGenerator_ShouldGenerateServerOnlyCode()
    {
        // 准备测试代码，包含处理器
        string testCode = @"
            using System;
            using PulseRPC.Protocol;
            using PulseRPC.Protocol.Attributes;

            namespace TestNamespace
            {
                public class TestRequestHandler {}

                [Message(1001, MessageType.Request)]
                [Handler(typeof(TestRequestHandler))]
                public partial class TestRequest : IMessage
                {
                    public string Text { get; set; }
                }
            }";

        // 使用服务端生成器生成代码
        var generatedSources = RunGenerator(testCode, new ServerCodeGenerator());

        // 验证生成的服务端代码
        Assert.Contains(generatedSources, s => s.Key.EndsWith("ServerMessageRegistry.g.cs"));
        Assert.Contains(generatedSources, s => s.Key.EndsWith("ServerMessageSerializer.g.cs"));
        Assert.Contains(generatedSources, s => s.Key.EndsWith("ServerMessageDispatcher.g.cs"));

        // 检查服务端代码中是否包含处理器相关代码
        var dispatcherCode = generatedSources.First(s => s.Key.EndsWith("ServerMessageDispatcher.g.cs")).Value;
        Assert.Contains("TestRequestHandler", dispatcherCode);

        // 验证不包含客户端相关代码
        Assert.DoesNotContain(generatedSources, s => s.Key.EndsWith("RpcClient.g.cs"));
    }

    /// <summary>
    /// 运行生成器并获取生成的代码
    /// </summary>
    /// <param name="source">源代码</param>
    /// <param name="generator">代码生成器</param>
    /// <returns>生成的源代码字典</returns>
    private Dictionary<string, string> RunGenerator(string source, ISourceGenerator generator)
    {
        // 添加基础引用
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location)
        };

        // 添加自定义特性类型，这里使用占位符
        var attributesSource = @"
            namespace PulseRPC.Protocol
            {
                public enum MessageType
                {
                    Request = 0,
                    Response = 1,
                    Notification = 2
                }
            }

            namespace PulseRPC.Protocol.Attributes
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public class MessageAttribute : System.Attribute
                {
                    public int Id { get; }
                    public MessageType Type { get; }

                    public MessageAttribute(int id, MessageType type)
                    {
                        Id = id;
                        Type = type;
                    }
                }

                [System.AttributeUsage(System.AttributeTargets.Class)]
                public class HandlerAttribute : System.Attribute
                {
                    public System.Type HandlerType { get; }

                    public HandlerAttribute(System.Type handlerType)
                    {
                        HandlerType = handlerType;
                    }
                }
            }

            namespace PulseRPC.Protocol
            {
                public interface IMessage {}
            }

            namespace MemoryPack
            {
                public static class MemoryPackSerializer
                {
                    public static byte[] Serialize<T>(T value) => new byte[0];
                    public static T Deserialize<T>(byte[] data) => default;
                }
            }

            namespace PulseRPC.Protocol.Network
            {
                public class SessionContext {}
            }

            namespace Microsoft.Extensions.Logging
            {
                public interface ILogger {}
                public interface ILogger<T> : ILogger {}
            }";

        // 创建编译
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] {
                CSharpSyntaxTree.ParseText(source),
                CSharpSyntaxTree.ParseText(attributesSource)
            },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // 创建生成器驱动器
        var driver = CSharpGeneratorDriver.Create(generator);

        // 运行生成器
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        // 收集生成的源代码
        var generatedSources = new Dictionary<string, string>();
        foreach (var tree in outputCompilation.SyntaxTrees)
        {
            if (tree.FilePath.EndsWith(".g.cs"))
            {
                generatedSources[tree.FilePath] = tree.ToString();
            }
        }

        return generatedSources;
    }
}
