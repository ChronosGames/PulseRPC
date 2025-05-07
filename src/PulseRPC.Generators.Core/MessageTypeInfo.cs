using Microsoft.CodeAnalysis;

namespace PulseRPC.Generators.Core;

/// <summary>
/// 消息类型信息，存储消息的元数据
/// </summary>
public class MessageTypeInfo
{
    /// <summary>
    /// 消息类型符号
    /// </summary>
    public INamedTypeSymbol TypeSymbol { get; set; }

    /// <summary>
    /// 消息ID
    /// </summary>
    public int MessageId { get; set; }

    /// <summary>
    /// 消息类型（请求/响应/通知）
    /// </summary>
    public int MessageType { get; set; }

    /// <summary>
    /// 处理器类型符号（如果有）
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
