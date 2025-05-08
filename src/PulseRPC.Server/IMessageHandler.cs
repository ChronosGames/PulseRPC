using PulseRPC.Protocol;
using PulseRPC.Protocol.Network;

namespace PulseRPC.Server;

/// <summary>
/// 通用消息处理器接口
/// </summary>
public interface IMessageHandler
{
    /// <summary>
    /// 获取此处理器处理的消息类型信息
    /// </summary>
    MessageTypeInfo GetMessageTypeInfo();
}

/// <summary>
/// 泛型消息处理器接口
/// </summary>
/// <typeparam name="TMessage">消息类型</typeparam>
public interface IMessageHandler<in TMessage> : IMessageHandler where TMessage : class, IMessage
{
    /// <summary>
    /// 处理消息
    /// </summary>
    /// <param name="context">会话上下文</param>
    /// <param name="message">消息实例</param>
    /// <returns>处理任务</returns>
    Task HandleAsync(SessionContext context, TMessage message);
}

/// <summary>
/// 消息类型信息
/// </summary>
public readonly struct MessageTypeInfo
{
    /// <summary>
    /// 消息ID
    /// </summary>
    public int MessageId { get; }

    /// <summary>
    /// 消息类型
    /// </summary>
    public Type MessageType { get; }

    /// <summary>
    /// 初始化消息类型信息
    /// </summary>
    /// <param name="messageId">消息ID</param>
    /// <param name="messageType">消息类型</param>
    public MessageTypeInfo(int messageId, Type messageType)
    {
        MessageId = messageId;
        MessageType = messageType;
    }
}
