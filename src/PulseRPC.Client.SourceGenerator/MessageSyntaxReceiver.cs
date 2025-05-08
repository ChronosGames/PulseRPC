using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PulseRPC.Client.SourceGenerator;

/// <summary>
/// 消息语法接收器，用于收集标记为消息的类型
/// </summary>
public class MessageSyntaxReceiver : AbstractMessageSyntaxReceiver
{
    /// <summary>
    /// 识别的消息类型列表
    /// </summary>
    public List<ClientMessageTypeInfo> MessageTypes { get; } = new List<ClientMessageTypeInfo>();

    /// <summary>
    /// 访问语法节点，收集消息类型
    /// </summary>
    /// <param name="context">语法上下文</param>
    public override void OnVisitSyntaxNode(GeneratorSyntaxContext context)
    {
        // 检查是否为类声明
        if (!(context.Node is ClassDeclarationSyntax classDeclaration))
            return;

        // 尝试获取语义模型和符号
        var semanticModel = context.SemanticModel;
        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
        if (classSymbol == null)
            return;

        // 检查是否实现了IMessage接口
        bool implementsIMessage = false;
        foreach (var interfaceSymbol in classSymbol.AllInterfaces)
        {
            if (interfaceSymbol.Name == "IMessage" && interfaceSymbol.ContainingNamespace.ToString() == "PulseRPC.Protocol")
            {
                implementsIMessage = true;
                break;
            }
        }

        if (!implementsIMessage)
            return;

        // 查找Message特性
        foreach (var attributeData in classSymbol.GetAttributes())
        {
            if (attributeData.AttributeClass?.Name == "MessageAttribute" &&
                attributeData.AttributeClass.ContainingNamespace.ToString() == "PulseRPC.Protocol.Attributes")
            {
                // 提取消息ID和类型
                var messageId = attributeData.ConstructorArguments[0].Value as int? ?? 0;
                var messageType = attributeData.ConstructorArguments[1].Value as int? ?? 0;

                // 创建消息类型信息对象
                var messageTypeInfo = new ClientMessageTypeInfo(classSymbol, messageId, messageType, null);

                // 检查是否有Handler特性
                foreach (var attr in classSymbol.GetAttributes())
                {
                    if (attr.AttributeClass?.Name == "HandlerAttribute" &&
                        attr.AttributeClass.ContainingNamespace.ToString() == "PulseRPC.Protocol.Attributes")
                    {
                        if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is ITypeSymbol handlerType)
                        {
                            messageTypeInfo.HandlerType = handlerType;
                        }
                    }
                }

                // 添加到消息类型列表
                MessageTypes.Add(messageTypeInfo);
                break;
            }
        }
    }
}
