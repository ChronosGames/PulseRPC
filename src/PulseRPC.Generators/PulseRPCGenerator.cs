using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PulseRPC.Generators;

/// <summary>
/// PulseRPC协议代码生成器
/// </summary>
[Generator]
public class PulseRPCGenerator : ISourceGenerator
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

        // 生成消息注册表
        GenerateMessageRegistry(context, syntaxReceiver.MessageTypes);

        // 生成消息分发器
        GenerateMessageDispatcher(context, syntaxReceiver.MessageTypes);

        // 生成序列化助手
        GenerateMessageSerializer(context, syntaxReceiver.MessageTypes);

        // 生成客户端代码
        GenerateClientCode(context, syntaxReceiver.MessageTypes);
    }

    /// <summary>
    /// 生成消息注册表代码
    /// </summary>
    /// <param name="context">生成上下文</param>
    /// <param name="messageTypes">消息类型信息列表</param>
    private void GenerateMessageRegistry(GeneratorExecutionContext context, List<MessageTypeInfo> messageTypes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine();
        sb.AppendLine("namespace PulseRPC.Protocol.Serialization");
        sb.AppendLine("{");
        sb.AppendLine("    // 自动生成的消息注册表");
        sb.AppendLine("    public static partial class MessageRegistry2");
        sb.AppendLine("    {");
        sb.AppendLine("        // 静态构造函数，初始化注册表");
        sb.AppendLine("        static MessageRegistry2()");
        sb.AppendLine("        {");

        // 注册所有消息类型
        foreach (var messageType in messageTypes)
        {
            var fullyQualifiedName = GeneratorHelper.GetFullyQualifiedName(messageType.TypeSymbol);
            sb.AppendLine($"            MessageRegistry.RegisterMessageType<{fullyQualifiedName}>({messageType.MessageId});");

            // 如果有处理器，注册处理器类型
            if (messageType.HandlerType != null)
            {
                var handlerName = GeneratorHelper.GetFullyQualifiedName(messageType.HandlerType);
                sb.AppendLine($"            _handlerTypes[{messageType.MessageId}] = typeof({handlerName});");
            }

            sb.AppendLine();
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        // 添加生成的源码
        GeneratorHelper.AddSourceCode(context, "MessageRegistry.g.cs", sb.ToString());
    }

    /// <summary>
    /// 生成消息分发器代码
    /// </summary>
    /// <param name="context">生成上下文</param>
    /// <param name="messageTypes">消息类型信息列表</param>
    private void GenerateMessageDispatcher(GeneratorExecutionContext context, List<MessageTypeInfo> messageTypes)
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
        sb.AppendLine("    public partial class MessageDispatcher2");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly ILogger _logger;");
        sb.AppendLine();
        sb.AppendLine("        public MessageDispatcher2(ILogger<MessageDispatcher2> logger)");
        sb.AppendLine("        {");
        sb.AppendLine("            _logger = logger ?? throw new ArgumentNullException(nameof(logger));");
        sb.AppendLine("        }");
        sb.AppendLine();
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

        // 为每个消息类型生成case
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

        // 添加生成的源码
        GeneratorHelper.AddSourceCode(context, "MessageDispatcher.g.cs", sb.ToString());
    }

    /// <summary>
    /// 生成序列化助手代码
    /// </summary>
    /// <param name="context">生成上下文</param>
    /// <param name="messageTypes">消息类型信息列表</param>
    private void GenerateMessageSerializer(GeneratorExecutionContext context, List<MessageTypeInfo> messageTypes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using MemoryPack;");
        sb.AppendLine();
        sb.AppendLine("namespace PulseRPC.Protocol.Serialization");
        sb.AppendLine("{");
        sb.AppendLine("    // 自动生成的序列化助手");
        sb.AppendLine("    public static partial class MessageSerializer");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 根据消息ID反序列化消息");
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

        // 添加生成的源码
        GeneratorHelper.AddSourceCode(context, "MessageSerializer.g.cs", sb.ToString());
    }

    /// <summary>
    /// 生成客户端代码
    /// </summary>
    /// <param name="context">生成上下文</param>
    /// <param name="messageTypes">消息类型信息列表</param>
    private void GenerateClientCode(GeneratorExecutionContext context, List<MessageTypeInfo> messageTypes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using PulseRPC.Protocol;");
        sb.AppendLine("using PulseRPC.Protocol.Messages;");
        sb.AppendLine();
        sb.AppendLine("namespace PulseRPC.Client");
        sb.AppendLine("{");
        sb.AppendLine("    // 自动生成的客户端API");
        sb.AppendLine("    public static class RpcClient");
        sb.AppendLine("    {");
        sb.AppendLine("        private static TcpClient _client;");
        sb.AppendLine();
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 初始化客户端");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        /// <param name=\"client\">TCP客户端实例</param>");
        sb.AppendLine("        public static void Initialize(TcpClient client)");
        sb.AppendLine("        {");
        sb.AppendLine("            _client = client ?? throw new ArgumentNullException(nameof(client));");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 生成请求-响应方法对
        var requestTypes = messageTypes.Where(t => t.MessageType == Protocol.MessageType.Request).ToList();
        var responseTypes = messageTypes.Where(t => t.MessageType == Protocol.MessageType.Response).ToList();

        foreach (var requestType in requestTypes)
        {
            // 查找可能的响应类型（通常是请求ID+1）
            var responseType = responseTypes.FirstOrDefault(r => r.MessageId == requestType.MessageId + 1);
            if (responseType == null) continue;

            var requestTypeName = requestType.TypeSymbol.Name;
            var requestFullName = GeneratorHelper.GetFullyQualifiedName(requestType.TypeSymbol);
            var responseTypeName = responseType.TypeSymbol.Name;
            var responseFullName = GeneratorHelper.GetFullyQualifiedName(responseType.TypeSymbol);

            // 获取请求类型的属性
            var properties = requestType.TypeSymbol.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p => !p.IsStatic && p.DeclaredAccessibility == Accessibility.Public)
                .ToList();

            // 生成方法签名
            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// 发送{requestTypeName}并等待{responseTypeName}");
            sb.AppendLine($"        /// </summary>");

            // 为每个属性生成参数注释
            foreach (var prop in properties)
            {
                if (prop.GetMethod == null || prop.GetMethod.DeclaredAccessibility != Accessibility.Public) continue;
                sb.AppendLine($"        /// <param name=\"{char.ToLowerInvariant(prop.Name[0]) + prop.Name.Substring(1)}\">{prop.Name}</param>");
            }

            sb.AppendLine($"        /// <param name=\"timeout\">超时时间(毫秒)</param>");
            sb.AppendLine($"        /// <param name=\"cancellationToken\">取消令牌</param>");
            sb.AppendLine($"        /// <returns>{responseTypeName}</returns>");

            // 生成方法签名
            sb.Append($"        public static async Task<{responseFullName}> {requestTypeName.Replace("Request", "Async")}(");

            // 生成参数列表
            for (int i = 0; i < properties.Count; i++)
            {
                var prop = properties[i];
                if (prop.GetMethod == null || prop.GetMethod.DeclaredAccessibility != Accessibility.Public) continue;

                var paramName = char.ToLowerInvariant(prop.Name[0]) + prop.Name.Substring(1);
                var typeName = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                sb.Append($"{typeName} {paramName}");

                if (i < properties.Count - 1)
                {
                    sb.Append(", ");
                }
            }

            if (properties.Count > 0)
            {
                sb.Append(", ");
            }

            sb.AppendLine($"int timeout = 30000, CancellationToken cancellationToken = default)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (_client == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                throw new InvalidOperationException(\"客户端未初始化，请先调用Initialize方法\");");
            sb.AppendLine("            }");
            sb.AppendLine();

            // 创建请求对象
            sb.AppendLine($"            var request = new {requestFullName}");
            sb.AppendLine("            {");

            // 设置属性值
            foreach (var prop in properties)
            {
                if (prop.GetMethod == null || prop.GetMethod.DeclaredAccessibility != Accessibility.Public) continue;

                var paramName = char.ToLowerInvariant(prop.Name[0]) + prop.Name.Substring(1);
                sb.AppendLine($"                {prop.Name} = {paramName},");
            }

            sb.AppendLine("            };");
            sb.AppendLine();

            // 发送请求并等待响应
            sb.AppendLine($"            return await _client.SendRequestAsync<{requestFullName}, {responseFullName}>(request, timeout, cancellationToken);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        // 添加生成的源码
        GeneratorHelper.AddSourceCode(context, "RpcClient.g.cs", sb.ToString());
    }
}

/// <summary>
/// 消息语法接收器，用于收集带有消息特性的类型
/// </summary>
public class MessageSyntaxReceiver : ISyntaxContextReceiver
{
    /// <summary>
    /// 收集到的消息类型列表
    /// </summary>
    public List<MessageTypeInfo> MessageTypes { get; } = new List<MessageTypeInfo>();

    /// <summary>
    /// 访问语法节点
    /// </summary>
    /// <param name="context">语法上下文</param>
    public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
    {
        // 检测所有带有[Message]特性的类
        if (context.Node is ClassDeclarationSyntax classDeclaration)
        {
            var semanticModel = context.SemanticModel;

            if (semanticModel.GetDeclaredSymbol(classDeclaration) is INamedTypeSymbol typeSymbol)
            {
                var messageAttribute = typeSymbol.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == "MessageAttribute");

                if (messageAttribute != null)
                {
                    // 提取消息ID和类型
                    var messageId = (int)messageAttribute.ConstructorArguments[0].Value!;
                    var messageType = (Protocol.MessageType)messageAttribute.ConstructorArguments[1].Value!;

                    // 获取处理器类型（如果有）
                    var handlerAttribute = typeSymbol.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.Name == "HandlerAttribute");

                    INamedTypeSymbol? handlerType = null;
                    if (handlerAttribute != null)
                    {
                        handlerType = handlerAttribute.ConstructorArguments[0].Value as INamedTypeSymbol;
                    }

                    // 收集消息类型信息
                    var messageInfo = new MessageTypeInfo(typeSymbol)
                    {
                        MessageId = messageId,
                        MessageType = messageType,
                        HandlerType = handlerType
                    };

                    MessageTypes.Add(messageInfo);
                }
            }
        }
    }
}

/// <summary>
/// 消息类型信息
/// </summary>
public class MessageTypeInfo
{
    /// <summary>
    /// 类型符号
    /// </summary>
    public INamedTypeSymbol TypeSymbol { get; set; }

    /// <summary>
    /// 消息ID
    /// </summary>
    public int MessageId { get; set; }

    /// <summary>
    /// 消息类型
    /// </summary>
    public Protocol.MessageType MessageType { get; set; }

    /// <summary>
    /// 处理器类型
    /// </summary>
    public INamedTypeSymbol? HandlerType { get; set; }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="typeSymbol">消息类型符号</param>
    public MessageTypeInfo(INamedTypeSymbol typeSymbol)
    {
        TypeSymbol = typeSymbol;
    }
}
