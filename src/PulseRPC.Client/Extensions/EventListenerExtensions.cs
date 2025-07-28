using System;
using PulseRPC.Client.Events;
using PulseRPC.Events;

namespace PulseRPC.Client;

/// <summary>
/// 事件监听器扩展方法 - 统一命名，零反射实现
/// </summary>
public static class EventListenerExtensions
{
    /// <summary>
    /// 注册事件监听器 - 使用默认配置（简单场景）
    /// </summary>
    /// <typeparam name="T">事件监听器类型，必须实现IPulseReceiver</typeparam>
    /// <param name="client">PulseRPC客户端</param>
    /// <param name="listener">事件监听器实例</param>
    /// <returns>订阅令牌</returns>
    /// <remarks>
    /// 这个方法提供最简单的事件监听器注册方式，使用默认配置：
    /// - 自动发现所有事件方法（On开头）
    /// - 使用接口定义的默认通道
    /// - 日志记录错误并继续处理
    /// - 无重试机制
    /// </remarks>
    public static ISubscriptionToken RegisterEventListener<T>(this IPulseRpcClient client, T listener)
        where T : class, IPulseReceiver
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));
        if (listener == null)
            throw new ArgumentNullException(nameof(listener));

        // 这里将调用源代码生成的特定实现
        // 生成器会为每个实现IPulseReceiver的接口生成对应的方法
        return RegisterEventListenerInternal(client, listener);
    }

    /// <summary>
    /// 配置事件监听器 - 提供高级配置选项（高级场景）
    /// </summary>
    /// <typeparam name="T">事件监听器类型，必须实现IPulseReceiver</typeparam>
    /// <param name="client">PulseRPC客户端</param>
    /// <param name="listener">事件监听器实例</param>
    /// <returns>事件监听器构建器</returns>
    /// <remarks>
    /// 这个方法提供流式配置API，支持高级功能：
    /// - 自定义通道选择
    /// - 事件过滤器
    /// - 错误处理策略
    /// - 重试机制
    /// - 性能监控
    /// </remarks>
    public static EventListenerBuilder<T> ConfigureEventListener<T>(this IPulseRpcClient client, T listener)
        where T : class, IPulseReceiver
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));
        if (listener == null)
            throw new ArgumentNullException(nameof(listener));

        return new EventListenerBuilder<T>(client, listener);
    }

    /// <summary>
    /// 使用指定配置注册事件监听器
    /// </summary>
    /// <typeparam name="T">事件监听器类型</typeparam>
    /// <param name="client">PulseRPC客户端</param>
    /// <param name="listener">事件监听器实例</param>
    /// <param name="configuration">配置对象</param>
    /// <returns>订阅令牌</returns>
    public static ISubscriptionToken RegisterEventListener<T>(this IPulseRpcClient client, T listener, EventListenerConfiguration configuration)
        where T : class, IPulseReceiver
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));
        if (listener == null)
            throw new ArgumentNullException(nameof(listener));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        // 这里将调用源代码生成的配置版本实现
        return RegisterEventListenerWithConfiguration(client, listener, configuration);
    }

    /// <summary>
    /// 快捷方法：注册事件监听器并指定通道
    /// </summary>
    /// <typeparam name="T">事件监听器类型</typeparam>
    /// <param name="client">PulseRPC客户端</param>
    /// <param name="listener">事件监听器实例</param>
    /// <param name="channelName">通道名称</param>
    /// <returns>订阅令牌</returns>
    public static ISubscriptionToken RegisterEventListener<T>(this IPulseRpcClient client, T listener, string channelName)
        where T : class, IPulseReceiver
    {
        return client.ConfigureEventListener(listener)
            .WithChannel(channelName)
            .Register();
    }

    /// <summary>
    /// 快捷方法：注册事件监听器并设置错误处理
    /// </summary>
    /// <typeparam name="T">事件监听器类型</typeparam>
    /// <param name="client">PulseRPC客户端</param>
    /// <param name="listener">事件监听器实例</param>
    /// <param name="errorHandler">错误处理回调</param>
    /// <returns>订阅令牌</returns>
    public static ISubscriptionToken RegisterEventListener<T>(this IPulseRpcClient client, T listener, Action<Exception, string> errorHandler)
        where T : class, IPulseReceiver
    {
        return client.ConfigureEventListener(listener)
            .WithErrorHandler(errorHandler)
            .Register();
    }

    /// <summary>
    /// 快捷方法：注册具有重试能力的事件监听器
    /// </summary>
    /// <typeparam name="T">事件监听器类型</typeparam>
    /// <param name="client">PulseRPC客户端</param>
    /// <param name="listener">事件监听器实例</param>
    /// <param name="maxRetries">最大重试次数</param>
    /// <returns>订阅令牌</returns>
    public static ISubscriptionToken RegisterEventListenerWithRetry<T>(this IPulseRpcClient client, T listener, int maxRetries = 3)
        where T : class, IPulseReceiver
    {
        return client.ConfigureEventListener(listener)
            .WithRetry(maxRetries)
            .Register();
    }

    #region 内部实现方法 - 将由源代码生成器实现

    /// <summary>
    /// 内部实现 - 简单注册（将由源代码生成器实现）
    /// </summary>
    private static ISubscriptionToken RegisterEventListenerInternal<T>(IPulseRpcClient client, T listener) where T : class
    {
        // 这个方法的实现将由源代码生成器生成
        // 生成器会根据T的类型生成特定的实现
        throw new NotImplementedException("This method will be implemented by source generator for each event listener type");
    }

    /// <summary>
    /// 内部实现 - 配置注册（将由源代码生成器实现）
    /// </summary>
    private static ISubscriptionToken RegisterEventListenerWithConfiguration<T>(IPulseRpcClient client, T listener, EventListenerConfiguration configuration) where T : class
    {
        // 这个方法的实现将由源代码生成器生成
        // 生成器会根据T的类型和配置生成特定的实现
        throw new NotImplementedException("This method will be implemented by source generator for each event listener type");
    }

    #endregion

    #region 便捷预设配置方法

    /// <summary>
    /// 游戏场景预设：低延迟，快速重试，容错处理
    /// </summary>
    public static ISubscriptionToken RegisterGameEventListener<T>(this IPulseRpcClient client, T listener)
        where T : class, IPulseReceiver
    {
        return client.ConfigureEventListener(listener)
            .WithGameSettings()
            .Register();
    }

    /// <summary>
    /// 关键业务场景预设：高可靠性，持久重试，严格错误处理
    /// </summary>
    public static ISubscriptionToken RegisterCriticalEventListener<T>(this IPulseRpcClient client, T listener)
        where T : class, IPulseReceiver
    {
        return client.ConfigureEventListener(listener)
            .WithCriticalSettings()
            .Register();
    }

    /// <summary>
    /// 开发调试场景预设：详细日志，性能监控，错误详情
    /// </summary>
    public static ISubscriptionToken RegisterDebugEventListener<T>(this IPulseRpcClient client, T listener)
        where T : class, IPulseReceiver
    {
        return client.ConfigureEventListener(listener)
            .WithPerformanceMonitoring()
            .WithErrorHandler((ex, eventName) =>
            {
                Console.WriteLine($"[DEBUG] Event {eventName} failed: {ex}");
            })
            .Register();
    }

    #endregion
}
