using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PulseRPC.Generators.Core;
using PulseRPC.Protocol;

namespace PulseRPC.Client.Unity.Generator
{
    /// <summary>
    /// PulseRPC Unity客户端代码生成器
    /// </summary>
    [Generator]
    public class UnityClientGenerator : ISourceGenerator
    {
        /// <summary>
        /// 初始化生成器
        /// </summary>
        /// <param name="context">初始化上下文</param>
        public void Initialize(GeneratorInitializationContext context)
        {
            // 注册Unity专用语法接收器
            context.RegisterForSyntaxNotifications(() => new UnityMessageSyntaxReceiver());
        }

        /// <summary>
        /// 执行代码生成
        /// </summary>
        /// <param name="context">执行上下文</param>
        public void Execute(GeneratorExecutionContext context)
        {
            // 获取语法接收器
            if (context.SyntaxContextReceiver is not UnityMessageSyntaxReceiver syntaxReceiver)
            {
                return;
            }

            // 生成Unity客户端所需的消息注册表代码
            var messageRegistryCode = GenerateUnityMessageRegistryCode(syntaxReceiver.MessageTypes);
            GeneratorHelper.AddSourceCode(context, "UnityMessageRegistry.g.cs", messageRegistryCode);

            // 生成Unity客户端序列化助手代码
            var serializerCode = GenerateUnitySerializerCode(syntaxReceiver.MessageTypes);
            GeneratorHelper.AddSourceCode(context, "UnityMessageSerializer.g.cs", serializerCode);

            // 生成Unity客户端API代码
            var clientCode = GenerateUnityClientCode(syntaxReceiver.MessageTypes);
            GeneratorHelper.AddSourceCode(context, "UnityClient.g.cs", clientCode);
        }

        /// <summary>
        /// 生成Unity客户端消息注册表代码
        /// </summary>
        /// <param name="messageTypes">消息类型信息列表</param>
        /// <returns>生成的源代码</returns>
        private string GenerateUnityMessageRegistryCode(List<UnityMessageTypeInfo> messageTypes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();
            sb.AppendLine("namespace PulseRPC.Protocol.Serialization");
            sb.AppendLine("{");
            sb.AppendLine("    // 自动生成的Unity客户端消息注册表");
            sb.AppendLine("    public static class MessageRegistry");
            sb.AppendLine("    {");
            sb.AppendLine("        // 消息类型ID映射表");
            sb.AppendLine("        private static readonly Dictionary<Type, int> _typeToId = new Dictionary<Type, int>();");
            sb.AppendLine("        private static readonly Dictionary<int, Type> _idToType = new Dictionary<int, Type>();");
            sb.AppendLine();
            sb.AppendLine("        // 静态构造函数，初始化注册表");
            sb.AppendLine("        static MessageRegistry()");
            sb.AppendLine("        {");

            // 注册所有消息类型
            foreach (var messageType in messageTypes)
            {
                var typeName = GeneratorHelper.GetFullyQualifiedName(messageType.TypeSymbol);
                sb.AppendLine($"            RegisterMessageType({messageType.MessageId}, typeof({typeName}));");
            }

            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 注册消息类型");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <param name=\"messageId\">消息ID</param>");
            sb.AppendLine("        /// <param name=\"messageType\">消息类型</param>");
            sb.AppendLine("        private static void RegisterMessageType(int messageId, Type messageType)");
            sb.AppendLine("        {");
            sb.AppendLine("            _typeToId[messageType] = messageId;");
            sb.AppendLine("            _idToType[messageId] = messageType;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 获取消息类型ID");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <param name=\"messageType\">消息类型</param>");
            sb.AppendLine("        /// <returns>消息ID</returns>");
            sb.AppendLine("        public static int GetMessageId(Type messageType)");
            sb.AppendLine("        {");
            sb.AppendLine("            return _typeToId.TryGetValue(messageType, out var id) ? id : -1;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 获取消息类型");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <param name=\"messageId\">消息ID</param>");
            sb.AppendLine("        /// <returns>消息类型</returns>");
            sb.AppendLine("        public static Type GetMessageType(int messageId)");
            sb.AppendLine("        {");
            sb.AppendLine("            return _idToType.TryGetValue(messageId, out var type) ? type : null;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// 生成Unity客户端序列化助手代码
        /// </summary>
        /// <param name="messageTypes">消息类型信息列表</param>
        /// <returns>生成的源代码</returns>
        private string GenerateUnitySerializerCode(List<UnityMessageTypeInfo> messageTypes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Text.Json;");
            sb.AppendLine();
            sb.AppendLine("namespace PulseRPC.Protocol.Serialization");
            sb.AppendLine("{");
            sb.AppendLine("    // 自动生成的Unity客户端序列化助手");
            sb.AppendLine("    public static class MessageSerializer");
            sb.AppendLine("    {");
            sb.AppendLine("        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions");
            sb.AppendLine("        {");
            sb.AppendLine("            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,");
            sb.AppendLine("            WriteIndented = false");
            sb.AppendLine("        };");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 序列化消息对象");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <param name=\"message\">消息对象</param>");
            sb.AppendLine("        /// <returns>序列化后的字节数组</returns>");
            sb.AppendLine("        public static byte[] Serialize(object message)");
            sb.AppendLine("        {");
            sb.AppendLine("            return JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), _options);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 反序列化消息对象");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <param name=\"messageId\">消息ID</param>");
            sb.AppendLine("        /// <param name=\"data\">序列化数据</param>");
            sb.AppendLine("        /// <returns>反序列化的对象</returns>");
            sb.AppendLine("        public static object Deserialize(int messageId, byte[] data)");
            sb.AppendLine("        {");
            sb.AppendLine("            var type = MessageRegistry.GetMessageType(messageId);");
            sb.AppendLine("            if (type == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                throw new InvalidOperationException($\"找不到ID为{messageId}的消息类型\");");
            sb.AppendLine("            }");
            sb.AppendLine("            return JsonSerializer.Deserialize(data, type, _options);");
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
            sb.AppendLine("            return JsonSerializer.Deserialize<T>(data, _options);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// 生成Unity客户端API代码
        /// </summary>
        /// <param name="messageTypes">消息类型信息列表</param>
        /// <returns>生成的源代码</returns>
        private string GenerateUnityClientCode(List<UnityMessageTypeInfo> messageTypes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Threading;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using PulseRPC.Protocol.Serialization;");
            sb.AppendLine();
            sb.AppendLine("namespace PulseRPC.Client.Unity");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// 自动生成的Unity客户端API");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public partial class UnityClient");
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

    /// <summary>
    /// Unity客户端消息类型信息
    /// </summary>
    public class UnityMessageTypeInfo : IMessageTypeInfo
    {
        /// <summary>
        /// 类型符号
        /// </summary>
        public INamedTypeSymbol TypeSymbol { get; }

        /// <summary>
        /// 消息ID
        /// </summary>
        public int MessageId { get; set; }

        /// <summary>
        /// 消息类型
        /// </summary>
        public MessageType MessageType { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="typeSymbol">消息类型符号</param>
        /// <param name="messageId">消息ID</param>
        /// <param name="messageType">消息类型</param>
        public UnityMessageTypeInfo(INamedTypeSymbol typeSymbol, int messageId, MessageType messageType)
        {
            TypeSymbol = typeSymbol;
            MessageId = messageId;
            MessageType = messageType;
        }
    }

    /// <summary>
    /// Unity客户端消息语法接收器
    /// </summary>
    public class UnityMessageSyntaxReceiver : AbstractMessageSyntaxReceiver<UnityMessageTypeInfo>
    {
        /// <summary>
        /// 创建消息类型信息
        /// </summary>
        /// <param name="typeSymbol">类型符号</param>
        /// <param name="messageId">消息ID</param>
        /// <param name="messageType">消息类型</param>
        /// <returns>消息类型信息</returns>
        protected override UnityMessageTypeInfo CreateMessageTypeInfo(INamedTypeSymbol typeSymbol, int messageId, MessageType messageType)
        {
            return new UnityMessageTypeInfo(typeSymbol, messageId, messageType);
        }
    }
}
