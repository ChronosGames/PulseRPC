using Microsoft.CodeAnalysis;

namespace PulseRPC.Client.SourceGenerator;

/// <summary>
/// 消息类型信息接口
/// </summary>
public interface IMessageTypeInfo
{
    /// <summary>
    /// 类型符号
    /// </summary>
    INamedTypeSymbol TypeSymbol { get; }

    /// <summary>
    /// 消息ID
    /// </summary>
    int MessageId { get; }

    /// <summary>
    /// 消息类型，0=请求，1=响应，2=通知
    /// </summary>
    int MessageType { get; }
}
