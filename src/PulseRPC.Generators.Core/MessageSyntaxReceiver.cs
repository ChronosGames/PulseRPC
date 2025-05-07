using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PulseRPC.Generators.Core;

/// <summary>
/// 消息语法接收器，用于收集所有标记为消息的类型
/// </summary>
public class MessageSyntaxReceiver : ISyntaxContextReceiver
{
    /// <summary>
    /// 收集到的消息类型信息列表
    /// </summary>
    public List<MessageTypeInfo> MessageTypes { get; } = new List<MessageTypeInfo>();

    /// <summary>
    /// 当访问语法节点时调用
    /// </summary>
    /// <param name="context">语法上下文</param>
    public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
    {
        // 检测所有带有[Message]特性的类
        if (context.Node is ClassDeclarationSyntax classDeclaration)
        {
            var semanticModel = context.SemanticModel;
            var typeSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);

            if (typeSymbol != null)
            {
                var messageAttribute = typeSymbol.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == "MessageAttribute");

                if (messageAttribute != null)
                {
                    // 提取消息ID和类型
                    var messageId = (int)messageAttribute.ConstructorArguments[0].Value!;
                    var messageType = (int)messageAttribute.ConstructorArguments[1].Value!;

                    // 获取处理器类型（如果有）
                    var handlerAttribute = typeSymbol.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.Name == "HandlerAttribute");

                    INamedTypeSymbol? handlerType = null;
                    if (handlerAttribute != null && handlerAttribute.ConstructorArguments.Length > 0)
                    {
                        var handlerTypeObj = handlerAttribute.ConstructorArguments[0].Value;
                        if (handlerTypeObj is INamedTypeSymbol handlerTypeSymbol)
                        {
                            handlerType = handlerTypeSymbol;
                        }
                    }

                    // 收集消息类型信息
                    var messageInfo = new MessageTypeInfo((INamedTypeSymbol)typeSymbol)
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
