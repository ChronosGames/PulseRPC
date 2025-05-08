using System;
using System.Reflection;
using System.Threading.Tasks;
using PulseRPC.Protocol;
using PulseRPC.Protocol.Attributes;
using PulseRPC.Protocol.Network;

namespace PulseRPC.Server;

/// <summary>
/// 消息处理器基类，为特定类型的消息提供处理能力
/// </summary>
/// <typeparam name="TMessage">消息类型</typeparam>
public abstract class MessageHandlerBase<TMessage> : IMessageHandler<TMessage>
    where TMessage : class, IMessage
{
    /// <summary>
    /// 获取消息类型的特性信息
    /// </summary>
    /// <returns>消息类型信息</returns>
    public MessageTypeInfo GetMessageTypeInfo()
    {
        var messageType = typeof(TMessage);
        var messageAttribute = messageType.GetCustomAttribute<MessageAttribute>();

        if (messageAttribute == null)
        {
            throw new InvalidOperationException($"消息类型 {messageType.Name} 未标记 MessageAttribute");
        }

        return new MessageTypeInfo(messageAttribute.Id, messageType);
    }

    /// <summary>
    /// 处理消息的核心方法，需要被派生类实现
    /// </summary>
    /// <param name="context">会话上下文</param>
    /// <param name="message">消息实例</param>
    /// <returns>处理任务</returns>
    public abstract Task HandleAsync(SessionContext context, TMessage message);
}
