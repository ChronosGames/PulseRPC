using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PulseRPC.Generators.Client;
using Xunit;

namespace PulseRPC.Generators.Tests;

/// <summary>
/// 消息语法接收器测试
/// </summary>
public class MessageSyntaxReceiverTests
{
    /// <summary>
    /// 测试消息语法接收器能够识别标记为消息的类型
    /// </summary>
    [Fact]
    public void ShouldIdentifyMessageTypes()
    {
        // 准备测试代码
        string testCode = @"
            using System;
            using PulseRPC.Protocol;
            using PulseRPC.Protocol.Attributes;

            namespace TestNamespace
            {
                [Message(1001, MessageType.Request)]
                public partial class TestMessage : IMessage
                {
                    public string Text { get; set; }
                }
            }";

        // 分析代码并获取语法接收器
        var syntaxReceiver = AnalyzeCode(testCode);

        // 验证结果
        Assert.Single(syntaxReceiver.MessageTypes);
        Assert.Equal(1001, syntaxReceiver.MessageTypes[0].MessageId);
        Assert.Equal(0, (int)syntaxReceiver.MessageTypes[0].MessageType); // MessageType.Request = 0
        //Assert.Equal("TestMessage", syntaxReceiver.MessageTypes[0].TypeSymbol.Name);
    }

    /// <summary>
    /// 测试消息语法接收器能够识别处理器特性
    /// </summary>
    [Fact]
    public void ShouldIdentifyHandlerAttribute()
    {
        // 准备测试代码，包含处理器特性
        string testCode = @"
            using System;
            using PulseRPC.Protocol;
            using PulseRPC.Protocol.Attributes;

            namespace TestNamespace
            {
                public class TestHandler {}

                [Message(1001, MessageType.Request)]
                [Handler(typeof(TestHandler))]
                public partial class TestMessage : IMessage
                {
                    public string Text { get; set; }
                }
            }";

        // 分析代码并获取语法接收器
        var syntaxReceiver = AnalyzeCode(testCode);

        // 验证结果
        Assert.Single(syntaxReceiver.MessageTypes);
        Assert.Equal(1001, syntaxReceiver.MessageTypes[0].MessageId);
    }

    /// <summary>
    /// 分析代码并获取语法接收器
    /// </summary>
    /// <param name="source">源代码</param>
    /// <returns>语法接收器</returns>
    private MessageSyntaxReceiver AnalyzeCode(string source)
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

        // 创建语法接收器
        var syntaxReceiver = new MessageSyntaxReceiver();

        // 创建生成器上下文
        var context = new GeneratorDriver(compilation, syntaxReceiver);
        context.RunGenerator();

        return syntaxReceiver;
    }

    /// <summary>
    /// 生成器驱动，用于测试
    /// </summary>
    private class GeneratorDriver
    {
        private readonly CSharpCompilation _compilation;
        private readonly MessageSyntaxReceiver _syntaxReceiver;

        public GeneratorDriver(CSharpCompilation compilation, MessageSyntaxReceiver syntaxReceiver)
        {
            _compilation = compilation;
            _syntaxReceiver = syntaxReceiver;
        }

        public void RunGenerator()
        {
            // 对所有语法树执行分析
            foreach (var syntaxTree in _compilation.SyntaxTrees)
            {
                var semanticModel = _compilation.GetSemanticModel(syntaxTree);

                // 遍历所有语法节点
                foreach (var node in syntaxTree.GetRoot().DescendantNodes())
                {
                    var context = new GeneratorSyntaxContext(/*node, semanticModel*/);
                    _syntaxReceiver.OnVisitSyntaxNode(context);
                }
            }
        }
    }
}
