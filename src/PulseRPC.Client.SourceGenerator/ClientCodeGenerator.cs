using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PulseRPC.Client.SourceGenerator;

/// <summary>
/// 客户端代码生成器
/// </summary>
[Generator]
public class ClientCodeGenerator : ISourceGenerator
{
    /// <summary>
    /// 初始化
    /// </summary>
    /// <param name="context">生成器初始化上下文</param>
    public void Initialize(GeneratorInitializationContext context)
    {
        // 注册语法接收器
        context.RegisterForSyntaxNotifications(() => new MessageSyntaxReceiver());
    }

    /// <summary>
    /// 执行代码生成
    /// </summary>
    /// <param name="context">生成器执行上下文</param>
    public void Execute(GeneratorExecutionContext context)
    {
        // 检查语法接收器
        if (!(context.SyntaxContextReceiver is MessageSyntaxReceiver receiver))
            return;

        // 如果没有需要处理的消息类型则返回
        if (receiver.MessageTypes.Count == 0)
            return;

        // 生成客户端消息注册表
        GenerateClientMessageRegistry(context, receiver);

        // 生成客户端消息序列化器
        GenerateClientMessageSerializer(context, receiver);

        // 生成RPC客户端
        GenerateRpcClient(context, receiver);
    }

    /// <summary>
    /// 生成客户端消息注册表
    /// </summary>
    private void GenerateClientMessageRegistry(GeneratorExecutionContext context, MessageSyntaxReceiver receiver)
    {
        // 构建源代码
        var sourceBuilder = new StringBuilder();
        sourceBuilder.AppendLine("using System;");
        sourceBuilder.AppendLine("using System.Collections.Generic;");
        sourceBuilder.AppendLine("using PulseRPC.Protocol;");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine("namespace PulseRPC.Client");
        sourceBuilder.AppendLine("{");
        sourceBuilder.AppendLine("    /// <summary>");
        sourceBuilder.AppendLine("    /// 客户端消息注册表");
        sourceBuilder.AppendLine("    /// </summary>");
        sourceBuilder.AppendLine("    public static class ClientMessageRegistry");
        sourceBuilder.AppendLine("    {");
        sourceBuilder.AppendLine("        /// <summary>");
        sourceBuilder.AppendLine("        /// 初始化消息类型注册表");
        sourceBuilder.AppendLine("        /// </summary>");
        sourceBuilder.AppendLine("        static ClientMessageRegistry()");
        sourceBuilder.AppendLine("        {");
        sourceBuilder.AppendLine("            MessageTypes = new Dictionary<int, Type>();");
        sourceBuilder.AppendLine("            ResponseTypes = new Dictionary<Type, Type>();");
        sourceBuilder.AppendLine();

        // 添加所有消息类型
        foreach (var messageType in receiver.MessageTypes)
        {
            sourceBuilder.AppendLine($"            MessageTypes.Add({messageType.MessageId}, typeof({messageType.TypeSymbol.ToDisplayString()}));");

            // 如果是请求消息，尝试查找对应的响应消息
            if (messageType.MessageType == 0) // Request
            {
                var requestName = messageType.TypeSymbol.Name;
                if (requestName.EndsWith("Request"))
                {
                    var responseName = requestName.Substring(0, requestName.Length - 7) + "Response";
                    var responseType = receiver.MessageTypes.Find(m => m.TypeSymbol.Name == responseName);

                    if (responseType != null)
                    {
                        sourceBuilder.AppendLine($"            ResponseTypes.Add(typeof({messageType.TypeSymbol.ToDisplayString()}), typeof({responseType.TypeSymbol.ToDisplayString()}));");
                    }
                }
            }
        }

        sourceBuilder.AppendLine("        }");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine("        /// <summary>");
        sourceBuilder.AppendLine("        /// 消息类型字典，键为消息ID，值为消息类型");
        sourceBuilder.AppendLine("        /// </summary>");
        sourceBuilder.AppendLine("        public static Dictionary<int, Type> MessageTypes { get; }");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine("        /// <summary>");
        sourceBuilder.AppendLine("        /// 响应类型字典，键为请求类型，值为对应的响应类型");
        sourceBuilder.AppendLine("        /// </summary>");
        sourceBuilder.AppendLine("        public static Dictionary<Type, Type> ResponseTypes { get; }");
        sourceBuilder.AppendLine("    }");
        sourceBuilder.AppendLine("}");

        // 添加到编译
        context.AddSource("ClientMessageRegistry.g.cs", SourceText.From(sourceBuilder.ToString(), Encoding.UTF8));
    }

    /// <summary>
    /// 生成客户端消息序列化器
    /// </summary>
    private void GenerateClientMessageSerializer(GeneratorExecutionContext context, MessageSyntaxReceiver receiver)
    {
        var sourceBuilder = new StringBuilder();
        sourceBuilder.AppendLine("using System;");
        sourceBuilder.AppendLine("using System.Collections.Generic;");
        sourceBuilder.AppendLine("using PulseRPC.Protocol;");
        sourceBuilder.AppendLine("using MemoryPack;");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine("namespace PulseRPC.Client");
        sourceBuilder.AppendLine("{");
        sourceBuilder.AppendLine("    /// <summary>");
        sourceBuilder.AppendLine("    /// 客户端消息序列化器");
        sourceBuilder.AppendLine("    /// </summary>");
        sourceBuilder.AppendLine("    public static class ClientMessageSerializer");
        sourceBuilder.AppendLine("    {");
        sourceBuilder.AppendLine("        /// <summary>");
        sourceBuilder.AppendLine("        /// 序列化消息");
        sourceBuilder.AppendLine("        /// </summary>");
        sourceBuilder.AppendLine("        public static byte[] Serialize(IMessage message)");
        sourceBuilder.AppendLine("        {");
        sourceBuilder.AppendLine("            return MemoryPackSerializer.Serialize(message);");
        sourceBuilder.AppendLine("        }");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine("        /// <summary>");
        sourceBuilder.AppendLine("        /// 反序列化消息");
        sourceBuilder.AppendLine("        /// </summary>");
        sourceBuilder.AppendLine("        public static IMessage Deserialize(int messageId, byte[] data)");
        sourceBuilder.AppendLine("        {");
        sourceBuilder.AppendLine("            if (!ClientMessageRegistry.MessageTypes.TryGetValue(messageId, out var type))");
        sourceBuilder.AppendLine("                throw new ArgumentException($\"未知的消息ID: {messageId}\");");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine("            return (IMessage)MemoryPackSerializer.Deserialize(type, data);");
        sourceBuilder.AppendLine("        }");
        sourceBuilder.AppendLine("    }");
        sourceBuilder.AppendLine("}");

        // 添加到编译
        context.AddSource("ClientMessageSerializer.g.cs", SourceText.From(sourceBuilder.ToString(), Encoding.UTF8));
    }

    /// <summary>
    /// 生成RPC客户端
    /// </summary>
    private void GenerateRpcClient(GeneratorExecutionContext context, MessageSyntaxReceiver receiver)
    {
        var sourceBuilder = new StringBuilder();
        sourceBuilder.AppendLine("using System;");
        sourceBuilder.AppendLine("using System.Threading;");
        sourceBuilder.AppendLine("using System.Threading.Tasks;");
        sourceBuilder.AppendLine("using PulseRPC.Protocol;");
        sourceBuilder.AppendLine("using Microsoft.Extensions.Logging;");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine("namespace PulseRPC.Client");
        sourceBuilder.AppendLine("{");
        sourceBuilder.AppendLine("    public partial class RpcClient");
        sourceBuilder.AppendLine("    {");

        // 为每个请求消息生成方法
        foreach (var messageType in receiver.MessageTypes)
        {
            if (messageType.MessageType == 0) // Request
            {
                var requestName = messageType.TypeSymbol.Name;
                if (requestName.EndsWith("Request"))
                {
                    var methodName = requestName.Substring(0, requestName.Length - 7);
                    var responseName = methodName + "Response";
                    var responseType = receiver.MessageTypes.Find(m => m.TypeSymbol.Name == responseName);

                    if (responseType != null)
                    {
                        sourceBuilder.AppendLine();
                        sourceBuilder.AppendLine("        /// <summary>");
                        sourceBuilder.AppendLine($"        /// 发送{methodName}请求");
                        sourceBuilder.AppendLine("        /// </summary>");
                        sourceBuilder.AppendLine($"        public async Task<{responseType.TypeSymbol.ToDisplayString()}> {methodName}Async({messageType.TypeSymbol.ToDisplayString()} request, CancellationToken cancellationToken = default)");
                        sourceBuilder.AppendLine("        {");
                        sourceBuilder.AppendLine("            _logger?.LogDebug($\"发送{0}请求\", nameof(request));");
                        sourceBuilder.AppendLine("            return await SendRequestAsync<" +
                                                 $"{messageType.TypeSymbol.ToDisplayString()}, {responseType.TypeSymbol.ToDisplayString()}" +
                                                 ">(request, cancellationToken);");
                        sourceBuilder.AppendLine("        }");
                    }
                }
            }
        }

        sourceBuilder.AppendLine("    }");
        sourceBuilder.AppendLine("}");

        // 添加到编译
        context.AddSource("RpcClient.g.cs", SourceText.From(sourceBuilder.ToString(), Encoding.UTF8));
    }
}
