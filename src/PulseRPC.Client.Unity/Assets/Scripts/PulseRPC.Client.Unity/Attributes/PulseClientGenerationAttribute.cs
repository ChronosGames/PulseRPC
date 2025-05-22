using System;

namespace PulseRPC.Attributes
{
    /// <summary>
    /// 标记需要生成客户端代理的接口或类
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class PulseClientGenerationAttribute : Attribute
    {
        /// <summary>
        /// 目标类型
        /// </summary>
        public Type TargetType { get; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="targetType">目标类型</param>
        public PulseClientGenerationAttribute(Type targetType)
        {
            TargetType = targetType;
        }
    }
}
