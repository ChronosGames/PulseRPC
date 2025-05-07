using System.Text;
using Microsoft.CodeAnalysis;
using PulseRPC.Generators.Core;

namespace PulseRPC.Generators.Server;

/// <summary>
/// PulseRPC服务端专用代码生成器
/// </summary>
[Generator]
public class ServerCodeGenerator : ISourceGenerator
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

        // 生成服务端所需的消息注册表代码
        var messageRegistryCode = GenerateServerMessageRegistryCode(syntaxReceiver.MessageTypes);
        GeneratorHelper.AddSourceCode(context, "ServerMessageRegistry.g.cs", messageRegistryCode);

        // 生成服务端序列化助手代码
        var serializerCode = GenerateMessageSerializerCode(syntaxReceiver.MessageTypes);
        GeneratorHelper.AddSourceCode(context, "ServerMessageSerializer.g.cs", serializerCode);

        // 生成消息分发器代码
        var dispatcherCode = GenerateMessageDispatcherCode(syntaxReceiver.MessageTypes);
        GeneratorHelper.AddSourceCode(context, "ServerMessageDispatcher.g.cs", dispatcherCode);
    }

    /// <summary>
    /// 生成服务端消息注册表代码
    /// </summary>
    /// <param name="messageTypes">消息类型信息列表</param>
    /// <returns>生成的源代码</returns>
    private string GenerateServerMessageRegistryCode(List<MessageTypeInfo> messageTypes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine();
        sb.AppendLine("namespace PulseRPC.Protocol.Serialization");
        sb.AppendLine("{");
        sb.AppendLine("    // 自动生成的服务端消息注册表");
        sb.AppendLine("    public static partial class MessageRegistry");
        sb.AppendLine("    {");
        sb.AppendLine("        // 服务端处理器类型映射");
        sb.AppendLine("        private static readonly Dictionary<int, Type> _handlerTypes = new Dictionary<int, Type>();");
        sb.AppendLine();
        sb.AppendLine("        // 静态构造函数，初始化注册表");
        sb.AppendLine("        static MessageRegistry()");
        sb.AppendLine("        {");

        // 注册所有消息类型
        foreach (var messageType in messageTypes)
        {
            var fullyQualifiedName = GeneratorHelper.GetFullyQualifiedName(messageType.TypeSymbol);
            sb.AppendLine($"            RegisterMessageType<{fullyQualifiedName}>({messageType.MessageId});");

            // 如果有处理器，注册处理器类型
            if (messageType.HandlerType != null)
            {
                var handlerName = GeneratorHelper.GetFullyQualifiedName(messageType.HandlerType);
                sb.AppendLine($"            _handlerTypes[{messageType.MessageId}] = typeof({handlerName});");
            }
        }

        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 根据消息ID获取处理器类型");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        /// <param name=\"messageId\">消息ID</param>");
        sb.AppendLine("        /// <returns>处理器类型，如果不存在则返回null</returns>");
        sb.AppendLine("        public static Type? GetHandlerType(int messageId)");
        sb.AppendLine("        {");
        sb.AppendLine("            return _handlerTypes.TryGetValue(messageId, out var type) ? type : null;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// 生成服务端序列化助手代码
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
        sb.AppendLine("    // 自动生成的服务端序列化助手");
        sb.AppendLine("    public static partial class MessageSerializer");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 根据消息ID反序列化消息（服务端专用）");
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
        sb.AppendLine();
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 反序列化特定类型的消息");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        /// <typeparam name=\"T\">消息类型</typeparam>");
        sb.AppendLine("        /// <param name=\"data\">序列化数据</param>");
        sb.AppendLine("        /// <returns>反序列化的消息对象</returns>");
        sb.AppendLine("        public static T Deserialize<T>(byte[] data) where T : class");
        sb.AppendLine("        {");
        sb.AppendLine("            return MemoryPackSerializer.Deserialize<T>(data);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// 生成消息分发器代码
    /// </summary>
    /// <param name="messageTypes">消息类型信息列表</param>
    /// <returns>生成的源代码</returns>
    private string GenerateMessageDispatcherCode(List<MessageTypeInfo> messageTypes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using PulseRPC.Protocol.Network;");
        sb.AppendLine("using PulseRPC.Protocol.Serialization;");
        sb.AppendLine("using Microsoft.Extensions.Logging;");
        sb.AppendLine();
        sb.AppendLine("namespace PulseRPC.Server");
        sb.AppendLine("{");
        sb.AppendLine("    // 自动生成的消息分发器");
        sb.AppendLine("    public partial class MessageDispatcher");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 高性能消息分发方法");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        /// <param name=\"messageId\">消息ID</param>");
        sb.AppendLine("        /// <param name=\"data\">消息数据</param>");
        sb.AppendLine("        /// <param name=\"context\">会话上下文</param>");
        sb.AppendLine("        /// <returns>处理任务</returns>");
        sb.AppendLine("        public async Task DispatchAsync(int messageId, byte[] data, SessionContext context)");
        sb.AppendLine("        {");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        sb.AppendLine("                switch (messageId)");
        sb.AppendLine("                {");

        // 为每个有处理器的消息类型生成case
        foreach (var messageType in messageTypes.Where(t => t.HandlerType != null))
        {
            var typeName = GeneratorHelper.GetFullyQualifiedName(messageType.TypeSymbol);
            var handlerTypeName = GeneratorHelper.GetFullyQualifiedName(messageType.HandlerType!);

            sb.AppendLine($"                    case {messageType.MessageId}: // {messageType.TypeSymbol.Name}");
            sb.AppendLine($"                        var message{messageType.MessageId} = MessageSerializer.Deserialize<{typeName}>(data);");
            sb.AppendLine($"                        var handler{messageType.MessageId} = ({handlerTypeName})GetOrCreateHandler(typeof({handlerTypeName}));");
            sb.AppendLine($"                        await handler{messageType.MessageId}.HandleAsync(context, message{messageType.MessageId});");
            sb.AppendLine("                        break;");
            sb.AppendLine();
        }

        sb.AppendLine("                    default:");
        sb.AppendLine("                        _logger.LogWarning(\"找不到消息ID {MessageId} 的处理器\", messageId);");
        sb.AppendLine("                        break;");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine("            catch (Exception ex)");
        sb.AppendLine("            {");
        sb.AppendLine("                _logger.LogError(ex, \"处理消息 {MessageId} 时发生错误\", messageId);");
        sb.AppendLine("                throw;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");

        // 初始化处理器实例的方法
        sb.AppendLine();
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 初始化所有处理器实例");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public void InitializeHandlers()");
        sb.AppendLine("        {");
        sb.AppendLine("            // 预热所有处理器实例");

        foreach (var messageType in messageTypes.Where(t => t.HandlerType != null))
        {
            var handlerTypeName = GeneratorHelper.GetFullyQualifiedName(messageType.HandlerType!);
            sb.AppendLine($"            GetOrCreateHandler(typeof({handlerTypeName}));");
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
