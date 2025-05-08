using Microsoft.CodeAnalysis;
using PulseRPC.Generators.Core;
using PulseRPC.Protocol;

namespace PulseRPC.Generators.Client;

/// <summary>
/// 客户端消息语法接收器，用于收集带有消息特性的类型
/// </summary>
public class MessageSyntaxReceiver : AbstractMessageSyntaxReceiver<ClientMessageTypeInfo>
{
    /// <summary>
    /// 创建消息类型信息
    /// </summary>
    /// <param name="typeSymbol">类型符号</param>
    /// <param name="messageId">消息ID</param>
    /// <param name="messageType">消息类型</param>
    /// <returns>消息类型信息</returns>
    protected override ClientMessageTypeInfo CreateMessageTypeInfo(INamedTypeSymbol typeSymbol, int messageId, MessageType messageType)
    {
        return new ClientMessageTypeInfo(typeSymbol, messageId, messageType);
    }
}
