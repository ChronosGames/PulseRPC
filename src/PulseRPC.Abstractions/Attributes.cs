namespace PulseRPC;

/// <summary>
/// 指定使用的通道
/// </summary>
[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
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
