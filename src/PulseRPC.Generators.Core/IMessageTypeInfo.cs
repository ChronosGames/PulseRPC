using Microsoft.CodeAnalysis;
using PulseRPC.Protocol;

namespace PulseRPC.Generators.Core;

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
    /// 消息类型
    /// </summary>
    MessageType MessageType { get; }
}
