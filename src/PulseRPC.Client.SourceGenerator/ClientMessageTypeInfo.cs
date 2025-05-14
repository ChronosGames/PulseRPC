using Microsoft.CodeAnalysis;

namespace PulseRPC.Client.SourceGenerator;

/// <summary>
/// 客户端消息类型信息
/// </summary>
public class ClientMessageTypeInfo : IMessageTypeInfo
{
    public ClientMessageTypeInfo(INamedTypeSymbol typeSymbol, ushort messageId, ITypeSymbol? handlerType = null)
    {
        TypeSymbol = typeSymbol;
        MessageId = messageId;
        HandlerType = handlerType;
    }

    /// <summary>
    /// 类型符号
    /// </summary>
    public INamedTypeSymbol TypeSymbol { get; set; }

    /// <summary>
    /// 消息ID
    /// </summary>
    public ushort MessageId { get; set; }

    /// <summary>
    /// 处理器类型，如果有定义的话
    /// </summary>
    public ITypeSymbol? HandlerType { get; set; }
}
