using System;

namespace PulseRPC.Protocol.Attributes
{
    /// <summary>
    /// 处理器标识特性，用于标记消息对应的处理器类型
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class HandlerAttribute : Attribute
    {
        /// <summary>
        /// 处理器类型
        /// </summary>
        public Type HandlerType { get; }

        /// <summary>
        /// 初始化处理器特性
        /// </summary>
        /// <param name="handlerType">处理器类型</param>
        public HandlerAttribute(Type handlerType)
        {
            HandlerType = handlerType;
        }
    }
}
