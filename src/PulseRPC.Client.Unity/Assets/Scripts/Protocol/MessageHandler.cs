using System;

namespace PulseRPC.Protocol
{
    /// <summary>
    /// 消息处理器接口
    /// </summary>
    public interface IMessageHandler
    {
        /// <summary>
        /// 获取处理器支持的消息类型
        /// </summary>
        Type MessageType { get; }

        /// <summary>
        /// 处理消息
        /// </summary>
        /// <param name="message">消息对象</param>
        void HandleMessage(object message);
    }

    /// <summary>
    /// 泛型消息处理器基类
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    public abstract class MessageHandler<T> : IMessageHandler
    {
        /// <summary>
        /// 获取处理器支持的消息类型
        /// </summary>
        public Type MessageType => typeof(T);

        /// <summary>
        /// 处理消息
        /// </summary>
        /// <param name="message">消息对象</param>
        public void HandleMessage(object message)
        {
            if (message is T typedMessage)
            {
                Handle(typedMessage);
            }
            else
            {
                throw new InvalidOperationException($"消息类型不匹配，期望{typeof(T).Name}，实际{message?.GetType().Name ?? "null"}");
            }
        }

        /// <summary>
        /// 处理强类型消息
        /// </summary>
        /// <param name="message">强类型消息</param>
        public abstract void Handle(T message);
    }
}
