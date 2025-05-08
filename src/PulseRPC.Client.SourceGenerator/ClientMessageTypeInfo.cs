using Microsoft.CodeAnalysis;

namespace PulseRPC.Client.SourceGenerator;

/// <summary>
/// 客户端消息类型信息
/// </summary>
public class ClientMessageTypeInfo : IMessageTypeInfo
{
    public ClientMessageTypeInfo(INamedTypeSymbol typeSymbol, int messageId, int messageType, ITypeSymbol? handlerType = null)
    {
        TypeSymbol = typeSymbol;
        MessageId = messageId;
        MessageType = messageType;
        HandlerType = handlerType;
    }

    /// <summary>
    /// 类型符号
    /// </summary>
    public INamedTypeSymbol TypeSymbol { get; set; }

    /// <summary>
    /// 消息ID
    /// </summary>
    public int MessageId { get; set; }

    /// <summary>
    /// 消息类型，0=请求，1=响应，2=通知
    /// </summary>
    public int MessageType { get; set; }

    /// <summary>
    /// 处理器类型，如果有定义的话
    /// </summary>
    public ITypeSymbol? HandlerType { get; set; }
}
