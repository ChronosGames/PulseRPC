using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PulseRPC.Events;

namespace PulseRPC.Client;

/// <summary>
/// 事件监听器构建器 - 提供流式配置API
/// </summary>
/// <typeparam name="T">事件监听器类型</typeparam>
public class EventListenerBuilder<T> where T : class
{
    private readonly IPulseClient _client;
    private readonly T _listener;
    private readonly EventListenerConfiguration _configuration;

    public EventListenerBuilder(IPulseClient client, T listener)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _listener = listener ?? throw new ArgumentNullException(nameof(listener));
        _configuration = new EventListenerConfiguration();
    }

    /// <summary>
    /// 指定使用的通道
    /// </summary>
    /// <param name="channelName">通道名称</param>
    /// <returns>构建器实例</returns>
    public EventListenerBuilder<T> WithChannel(string channelName)
    {
        _configuration.ChannelName = channelName;
        return this;
    }

    /// <summary>
    /// 设置事件过滤器 - 基于事件名称过滤
    /// </summary>
    /// <param name="filter">过滤器委托</param>
    /// <returns>构建器实例</returns>
    public EventListenerBuilder<T> WithEventFilter(Func<string, bool> filter)
    {
        _configuration.EventFilter = filter;
        return this;
    }

    /// <summary>
    /// 设置特定事件的数据过滤器
    /// </summary>
    /// <typeparam name="TEvent">事件数据类型</typeparam>
    /// <param name="eventName">事件名称</param>
    /// <param name="filter">数据过滤器</param>
    /// <returns>构建器实例</returns>
    public EventListenerBuilder<T> WithDataFilter<TEvent>(string eventName, EventDataFilter<TEvent> filter)
    {
        _configuration.DataFilters[eventName] = data => data is TEvent eventData && filter(eventData);
        return this;
    }

    /// <summary>
    /// 自动推断事件名称并设置数据过滤器
    /// </summary>
    /// <typeparam name="TEvent">事件数据类型</typeparam>
    /// <param name="filter">数据过滤器</param>
    /// <returns>构建器实例</returns>
    public EventListenerBuilder<T> WithDataFilter<TEvent>(EventDataFilter<TEvent> filter)
    {
        var eventName = typeof(TEvent).Name;
        return WithDataFilter(eventName, filter);
    }

    /// <summary>
    /// 设置错误处理策略
    /// </summary>
    /// <param name="strategy">错误处理策略</param>
    /// <returns>构建器实例</returns>
    public EventListenerBuilder<T> WithErrorHandling(ErrorHandlingStrategy strategy)
    {
        _configuration.ErrorHandling = strategy;
        return this;
    }

    /// <summary>
    /// 设置自定义错误处理器
    /// </summary>
    /// <param name="errorHandler">错误处理器</param>
    /// <returns>构建器实例</returns>
    public EventListenerBuilder<T> WithErrorHandler(EventErrorHandler errorHandler)
    {
        _configuration.ErrorHandler = errorHandler;
        _configuration.ErrorHandling = ErrorHandlingStrategy.Custom;
        return this;
    }

    /// <summary>
    /// 设置简单的错误处理回调
    /// </summary>
    /// <param name="onError">错误回调</param>
    /// <returns>构建器实例</returns>
    public EventListenerBuilder<T> WithErrorHandler(Action<Exception, string> onError)
    {
        _configuration.ErrorHandler = (ex, eventName, _, _) =>
        {
            onError(ex, eventName);
            return Task.FromResult(true); // 继续处理
        };
        _configuration.ErrorHandling = ErrorHandlingStrategy.Custom;
        return this;
    }

    /// <summary>
    /// 设置重试配置
    /// </summary>
    /// <param name="retryConfig">重试配置</param>
    /// <returns>构建器实例</returns>
    public EventListenerBuilder<T> WithRetry(RetryConfiguration retryConfig)
    {
        _configuration.RetryConfig = retryConfig;
        return this;
    }

    /// <summary>
    /// 设置简单重试参数
    /// </summary>
    /// <param name="maxAttempts">最大重试次数</param>
    /// <param name="retryInterval">重试间隔</param>
    /// <param name="useExponentialBackoff">是否使用指数退避</param>
    /// <returns>构建器实例</returns>
    public EventListenerBuilder<T> WithRetry(int maxAttempts, TimeSpan? retryInterval = null, bool useExponentialBackoff = true)
    {
        _configuration.RetryConfig = new RetryConfiguration
        {
            MaxAttempts = maxAttempts,
            RetryInterval = retryInterval ?? TimeSpan.FromSeconds(1),
            UseExponentialBackoff = useExponentialBackoff
        };
        return this;
    }

    /// <summary>
    /// 设置事件处理超时
    /// </summary>
    /// <param name="timeout">超时时间</param>
    /// <returns>构建器实例</returns>
    public EventListenerBuilder<T> WithTimeout(TimeSpan timeout)
    {
        _configuration.Timeout = timeout;
        return this;
    }

    /// <summary>
    /// 启用批量事件处理
    /// </summary>
    /// <param name="batchSize">批量大小</param>
    /// <returns>构建器实例</returns>
    public EventListenerBuilder<T> WithBatchProcessing(int batchSize = 10)
    {
        _configuration.EnableBatchProcessing = true;
        _configuration.BatchSize = batchSize;
        return this;
    }

    /// <summary>
    /// 设置事件优先级
    /// </summary>
    /// <param name="eventName">事件名称</param>
    /// <param name="priority">优先级</param>
    /// <returns>构建器实例</returns>
    public EventListenerBuilder<T> WithEventPriority(string eventName, EventPriority priority)
    {
        _configuration.EventPriorities[eventName] = priority;
        return this;
    }

    /// <summary>
    /// 启用性能监控
    /// </summary>
    /// <returns>构建器实例</returns>
    public EventListenerBuilder<T> WithPerformanceMonitoring()
    {
        _configuration.EnablePerformanceMonitoring = true;
        return this;
    }

    /// <summary>
    /// 注册事件监听器
    /// </summary>
    /// <returns>订阅令牌</returns>
    public ISubscriptionToken Register()
    {
        // 使用辅助类来避免直接依赖源代码生成的方法
        // 辅助类的委托将由源代码生成器设置
        return EventListenerRegistrar.RegisterWithConfiguration(_client, _listener, _configuration);
    }
}

/// <summary>
/// 事件监听器构建器的便捷扩展方法
/// </summary>
public static class EventListenerBuilderExtensions
{
    /// <summary>
    /// 快速配置常用的错误处理 + 重试策略
    /// </summary>
    public static EventListenerBuilder<T> WithResilience<T>(this EventListenerBuilder<T> builder,
        ErrorHandlingStrategy errorStrategy = ErrorHandlingStrategy.RetryThenSkip,
        RetryConfiguration? retryConfig = null) where T : class
    {
        retryConfig ??= RetryConfigurations.Standard;

        return builder
            .WithErrorHandling(errorStrategy)
            .WithRetry(retryConfig);
    }

    /// <summary>
    /// 配置游戏场景的推荐设置
    /// </summary>
    public static EventListenerBuilder<T> WithGameSettings<T>(this EventListenerBuilder<T> builder) where T : class
    {
        return builder
            .WithResilience(ErrorHandlingStrategy.LogAndContinue, RetryConfigurations.Fast)
            .WithTimeout(TimeSpan.FromSeconds(5))
            .WithPerformanceMonitoring();
    }

    /// <summary>
    /// 配置关键业务场景的推荐设置
    /// </summary>
    public static EventListenerBuilder<T> WithCriticalSettings<T>(this EventListenerBuilder<T> builder) where T : class
    {
        return builder
            .WithResilience(ErrorHandlingStrategy.RetryThenThrow, RetryConfigurations.Persistent)
            .WithTimeout(TimeSpan.FromSeconds(30))
            .WithPerformanceMonitoring();
    }
}
