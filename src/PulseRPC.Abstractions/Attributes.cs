using System;

namespace PulseRPC
{
    /// <summary>
    /// 指定使用的通道
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Class,
        AllowMultiple = false)]
    public class ChannelAttribute : Attribute
    {
        /// <summary>
        /// 通道名称
        /// </summary>
        public string ChannelName { get; }

        public ChannelAttribute(string channelName)
        {
            ChannelName = channelName;
        }
    }

    /// <summary>
    /// 标记需要生成客户端代理的类
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class PulseClientGenerationAttribute : Attribute
    {
        /// <summary>
        /// 要扫描的程序集中的任意类型
        /// </summary>
        public Type MarkerType { get; }

        /// <summary>
        /// 初始化 <see cref="PulseClientGenerationAttribute"/>
        /// </summary>
        /// <param name="markerType">要扫描的程序集中的任意类型</param>
        public PulseClientGenerationAttribute(Type markerType)
        {
            MarkerType = markerType ?? throw new ArgumentNullException(nameof(markerType));
        }

        /// <summary>
        /// 获取或设置方法返回类型，用于指定WithDeadline等方法的返回类型
        /// </summary>
        public Type? WithResultType { get; set; }
    }

    /// <summary>
    /// 操作特性
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class OperationAttribute : Attribute
    {
        // 可以添加额外的操作特性
    }

    /// <summary>
    /// 事件特性
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class EventAttribute : Attribute
    {
        // 可以添加额外的事件特性
    }

    /// <summary>
    /// 服务契约特性
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface)]
    public class ServiceContractAttribute : Attribute
    {
        // 可以添加额外的服务契约特性
    }

    /// <summary>
    /// 事件契约特性
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface)]
    public class EventContractAttribute : Attribute
    {
        // 可以添加额外的事件契约特性
    }

    /// <summary>
    /// 标记 RPC 方法
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class RPCMethodAttribute : Attribute
    {
        // 可以添加额外的方法特性
    }

    /// <summary>
    /// 标记 RPC 服务接口
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface)]
    public class RPCServiceAttribute : Attribute
    {
        // 可以添加额外的服务特性
    }

    /// <summary>
    /// 标记事件数据
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class EventDataAttribute : Attribute
    {
        // 可以添加额外的事件数据特性
    }
}
