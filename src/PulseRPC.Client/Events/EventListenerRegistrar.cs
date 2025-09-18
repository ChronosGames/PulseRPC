using System;
using PulseRPC.Client;

namespace PulseRPC.Client;

/// <summary>
/// 事件监听器注册辅助类
/// 这个类提供一个中间层，避免 PulseReceiverBuilder 直接依赖源代码生成的方法
/// </summary>
public static class EventListenerRegistrar
{
    /// <summary>
    /// 委托：注册事件监听器的函数
    /// 这个委托将在源代码生成器中被实现
    /// </summary>
    public static Func<IPulseClient, object, EventListenerConfiguration, Task<ISubscriptionToken>>? RegisterWithConfigurationDelegate { get; set; }

    /// <summary>
    /// 使用配置注册事件监听器
    /// </summary>
    /// <typeparam name="T">事件监听器类型</typeparam>
    /// <param name="client">PulseRPC客户端</param>
    /// <param name="listener">事件监听器实例</param>
    /// <param name="configuration">配置对象</param>
    /// <returns>订阅令牌</returns>
    public static Task<ISubscriptionToken> RegisterWithConfiguration<T>(IPulseClient client, T listener, EventListenerConfiguration configuration) where T : class
    {
        if (RegisterWithConfigurationDelegate == null)
        {
            throw new InvalidOperationException(
                "事件监听器注册器未初始化。请确保项目中存在带有 [PulseClientGeneration] 特性的类，" +
                "并且特性参数包含了实现 IPulseReceiver 的事件接口。");
        }

        return RegisterWithConfigurationDelegate(client, listener, configuration);
    }
}
