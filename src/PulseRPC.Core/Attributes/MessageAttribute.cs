using System;

namespace PulseRPC.Protocol.Attributes
{
    /// <summary>
    /// 消息标识特性，用于标记可序列化的消息类型
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class MessageAttribute : Attribute
    {
        /// <summary>
        /// 消息唯一标识ID
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// 消息类型
        /// </summary>
        public MessageType Type { get; }

        /// <summary>
        /// 初始化消息特性
        /// </summary>
        /// <param name="id">消息唯一ID</param>
        /// <param name="type">消息类型</param>
        public MessageAttribute(int id, MessageType type)
        {
            Id = id;
            Type = type;
        }
    }
}
