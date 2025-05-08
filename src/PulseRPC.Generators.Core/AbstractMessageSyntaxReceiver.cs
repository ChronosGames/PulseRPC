using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PulseRPC.Protocol;

namespace PulseRPC.Generators.Core;

/// <summary>
/// 抽象消息语法接收器
/// </summary>
/// <typeparam name="T">消息类型信息类型</typeparam>
public abstract class AbstractMessageSyntaxReceiver<T> : ISyntaxContextReceiver
    where T : IMessageTypeInfo
{
    /// <summary>
    /// 收集到的消息类型列表
    /// </summary>
    public List<T> MessageTypes { get; } = new List<T>();

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
                    var messageType = (MessageType)messageAttribute.ConstructorArguments[1].Value!;

                    // 调用子类的创建方法
                    var messageInfo = CreateMessageTypeInfo(typeSymbol, messageId, messageType);
                    MessageTypes.Add(messageInfo);
                }
            }
        }
    }

    /// <summary>
    /// 创建消息类型信息
    /// </summary>
    /// <param name="typeSymbol">类型符号</param>
    /// <param name="messageId">消息ID</param>
    /// <param name="messageType">消息类型</param>
    /// <returns>消息类型信息</returns>
    protected abstract T CreateMessageTypeInfo(INamedTypeSymbol typeSymbol, int messageId, MessageType messageType);
}
