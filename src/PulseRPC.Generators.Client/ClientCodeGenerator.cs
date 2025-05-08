using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using PulseRPC.Generators.Core;
using PulseRPC.Protocol;

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
        var messageRegistryCode = GenerateClientMessageRegistryCode(syntaxReceiver.MessageTypes);
        GeneratorHelper.AddSourceCode(context, "ClientMessageRegistry.g.cs", messageRegistryCode);

        // 生成客户端序列化助手代码
        var serializerCode = GenerateMessageSerializerCode(syntaxReceiver.MessageTypes);
        GeneratorHelper.AddSourceCode(context, "ClientMessageSerializer.g.cs", serializerCode);

        // 生成RPC客户端代码
        var rpcClientCode = GenerateRpcClientCode(syntaxReceiver.MessageTypes);
        GeneratorHelper.AddSourceCode(context, "RpcClient.g.cs", rpcClientCode);
    }

    /// <summary>
    /// 生成客户端消息注册表代码
    /// </summary>
    /// <param name="messageTypes">消息类型信息列表</param>
    /// <returns>生成的源代码</returns>
    private string GenerateClientMessageRegistryCode(List<ClientMessageTypeInfo> messageTypes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine();
        sb.AppendLine("namespace PulseRPC.Protocol.Serialization");
        sb.AppendLine("{");
        sb.AppendLine("    // 自动生成的客户端消息注册表");
        sb.AppendLine("    public static partial class MessageRegistry");
        sb.AppendLine("    {");
        sb.AppendLine("        // 静态构造函数，初始化注册表");
        sb.AppendLine("        static MessageRegistry()");
        sb.AppendLine("        {");

        // 注册所有消息类型
        foreach (var messageType in messageTypes)
        {
            var fullyQualifiedName = GeneratorHelper.GetFullyQualifiedName(messageType.TypeSymbol);
            sb.AppendLine($"            RegisterMessageType<{fullyQualifiedName}>({messageType.MessageId});");
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// 生成客户端序列化助手代码
    /// </summary>
    /// <param name="messageTypes">消息类型信息列表</param>
    /// <returns>生成的源代码</returns>
    private string GenerateMessageSerializerCode(List<ClientMessageTypeInfo> messageTypes)
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
    private string GenerateRpcClientCode(List<ClientMessageTypeInfo> messageTypes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using PulseRPC.Protocol.Serialization;");
        sb.AppendLine();
        sb.AppendLine("namespace PulseRPC.Client");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 自动生成的RPC客户端");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public partial class RpcClient");
        sb.AppendLine("    {");

        // 为每个请求类型生成方法
        var requestMessages = messageTypes.Where(t => t.MessageType == MessageType.Request).ToList();
        foreach (var messageType in requestMessages)
        {
            var typeName = messageType.TypeSymbol.Name;
            var fullTypeName = GeneratorHelper.GetFullyQualifiedName(messageType.TypeSymbol);
            var responseTypeName = $"{typeName}Response"; // 假设响应消息名称为请求消息名+"Response"

            sb.AppendLine();
            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// 发送{typeName}请求");
            sb.AppendLine($"        /// </summary>");
            sb.AppendLine($"        /// <param name=\"request\">请求消息</param>");
            sb.AppendLine($"        /// <param name=\"cancellationToken\">取消令牌</param>");
            sb.AppendLine($"        /// <returns>响应任务</returns>");
            sb.AppendLine($"        public async Task<{responseTypeName}> Send{typeName}Async({fullTypeName} request, CancellationToken cancellationToken = default)");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            return await SendRequestAsync<{fullTypeName}, {responseTypeName}>(request, cancellationToken);");
            sb.AppendLine($"        }}");
        }

        // 为每个通知类型生成方法
        var notificationMessages = messageTypes.Where(t => t.MessageType == MessageType.Notification).ToList();
        foreach (var messageType in notificationMessages)
        {
            var typeName = messageType.TypeSymbol.Name;
            var fullTypeName = GeneratorHelper.GetFullyQualifiedName(messageType.TypeSymbol);

            sb.AppendLine();
            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// 发送{typeName}通知");
            sb.AppendLine($"        /// </summary>");
            sb.AppendLine($"        /// <param name=\"notification\">通知消息</param>");
            sb.AppendLine($"        /// <param name=\"cancellationToken\">取消令牌</param>");
            sb.AppendLine($"        /// <returns>发送任务</returns>");
            sb.AppendLine($"        public async Task Send{typeName}Async({fullTypeName} notification, CancellationToken cancellationToken = default)");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            await SendNotificationAsync(notification, cancellationToken);");
            sb.AppendLine($"        }}");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
