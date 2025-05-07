using System.Text;
using Microsoft.CodeAnalysis;
using PulseRPC.Generators.Core;

namespace PulseRPC.Generators.Client;

/// <summary>
/// PulseRPC客户端专用代码生成器
/// </summary>
[Generator]
public class ClientCodeGenerator : ISourceGenerator
{
    /// <summary>
    /// 初始化生成器
    /// </summary>
    /// <param name="context">初始化上下文</param>
    public void Initialize(GeneratorInitializationContext context)
    {
        // 注册语法接收器
        context.RegisterForSyntaxNotifications(() => new MessageSyntaxReceiver());
    }

    /// <summary>
    /// 执行代码生成
    /// </summary>
    /// <param name="context">执行上下文</param>
    public void Execute(GeneratorExecutionContext context)
    {
        // 获取语法接收器
        if (context.SyntaxContextReceiver is not MessageSyntaxReceiver syntaxReceiver)
        {
            return;
        }

        // 生成客户端所需的消息注册表代码
        var messageRegistryCode = GeneratorHelper.GenerateMessageRegistryCode(syntaxReceiver.MessageTypes);
        GeneratorHelper.AddSourceCode(context, "ClientMessageRegistry.g.cs", messageRegistryCode);

        // 生成客户端序列化助手代码
        var serializerCode = GenerateMessageSerializerCode(syntaxReceiver.MessageTypes);
        GeneratorHelper.AddSourceCode(context, "ClientMessageSerializer.g.cs", serializerCode);

        // 生成RPC客户端代码
        var rpcClientCode = GenerateRpcClientCode(syntaxReceiver.MessageTypes);
        GeneratorHelper.AddSourceCode(context, "RpcClient.g.cs", rpcClientCode);
    }

    /// <summary>
    /// 生成客户端序列化助手代码
    /// </summary>
    /// <param name="messageTypes">消息类型信息列表</param>
    /// <returns>生成的源代码</returns>
    private string GenerateMessageSerializerCode(List<MessageTypeInfo> messageTypes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using MemoryPack;");
        sb.AppendLine();
        sb.AppendLine("namespace PulseRPC.Protocol.Serialization");
        sb.AppendLine("{");
        sb.AppendLine("    // 自动生成的客户端序列化助手");
        sb.AppendLine("    public static partial class MessageSerializer");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 根据消息ID反序列化消息（客户端专用）");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        /// <param name=\"messageId\">消息ID</param>");
        sb.AppendLine("        /// <param name=\"data\">序列化数据</param>");
        sb.AppendLine("        /// <returns>反序列化的消息对象</returns>");
        sb.AppendLine("        public static object Deserialize(int messageId, byte[] data)");
        sb.AppendLine("        {");
        sb.AppendLine("            return messageId switch");
        sb.AppendLine("            {");

        // 为每个消息类型生成case
        foreach (var messageType in messageTypes)
        {
            var typeName = GeneratorHelper.GetFullyQualifiedName(messageType.TypeSymbol);
            sb.AppendLine($"                {messageType.MessageId} => MemoryPackSerializer.Deserialize<{typeName}>(data),");
        }

        sb.AppendLine("                _ => throw new InvalidOperationException($\"找不到ID为{messageId}的消息类型\")");
        sb.AppendLine("            };");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// 生成RPC客户端代码
    /// </summary>
    /// <param name="messageTypes">消息类型信息列表</param>
    /// <returns>生成的源代码</returns>
    private string GenerateRpcClientCode(List<MessageTypeInfo> messageTypes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using PulseRPC.Protocol;");
        sb.AppendLine("using PulseRPC.Protocol.Messages;");
        sb.AppendLine("using PulseRPC.Protocol.Serialization;");
        sb.AppendLine();
        sb.AppendLine("namespace PulseRPC.Client");
        sb.AppendLine("{");
        sb.AppendLine("    // 自动生成的客户端API");
        sb.AppendLine("    public static partial class RpcClient");
        sb.AppendLine("    {");

        // 为请求-响应对生成发送方法
        var responseTypes = messageTypes.Where(t => t.MessageType == 1).ToDictionary(t => t.MessageId);

        foreach (var messageType in messageTypes.Where(t => t.MessageType == 0))
        {
            var requestType = GeneratorHelper.GetFullyQualifiedName(messageType.TypeSymbol);
            var requestName = messageType.TypeSymbol.Name;

            // 尝试找到对应的响应类型
            // 简单约定：请求ID为N，则响应ID为N+1
            var responseId = messageType.MessageId + 1;
            if (responseTypes.TryGetValue(responseId, out var responseTypeInfo))
            {
                var responseType = GeneratorHelper.GetFullyQualifiedName(responseTypeInfo.TypeSymbol);
                var methodName = requestName.Replace("Request", "");

                sb.AppendLine();
                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// 发送{requestName}并等待响应");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        /// <param name=\"request\">请求消息</param>");
                sb.AppendLine($"        /// <param name=\"cancellationToken\">取消令牌</param>");
                sb.AppendLine($"        /// <returns>响应消息</returns>");
                sb.AppendLine($"        public static async Task<{responseType}> {methodName}Async({requestType} request, CancellationToken cancellationToken = default)");
                sb.AppendLine("        {");
                sb.AppendLine($"            var responseData = await SendRequestAsync({messageType.MessageId}, request, cancellationToken);");
                sb.AppendLine($"            return MemoryPackSerializer.Deserialize<{responseType}>(responseData);");
                sb.AppendLine("        }");
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
