using Microsoft.CodeAnalysis;
using PulseRPC.Generators.Core;
using PulseRPC.Protocol;

namespace PulseRPC.Generators.Client;

/// <summary>
/// 客户端消息类型信息
/// </summary>
public class ClientMessageTypeInfo : IMessageTypeInfo
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
    public ClientMessageTypeInfo(INamedTypeSymbol typeSymbol, int messageId, MessageType messageType)
    {
        TypeSymbol = typeSymbol;
        MessageId = messageId;
        MessageType = messageType;
    }
}
