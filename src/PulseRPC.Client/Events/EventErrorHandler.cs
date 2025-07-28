using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Events;

namespace PulseRPC.Client;

/// <summary>
/// 事件错误处理管理器
/// </summary>
public class EventErrorManager
{
    private readonly ILogger<EventErrorManager> _logger;

    public EventErrorManager(ILogger<EventErrorManager>? logger = null)
    {
        _logger = logger ?? NullLogger<EventErrorManager>.Instance;
    }

    /// <summary>
    /// 处理事件执行错误
    /// </summary>
    /// <param name="exception">发生的异常</param>
    /// <param name="eventName">事件名称</param>
    /// <param name="eventData">事件数据</param>
    /// <param name="configuration">配置</param>
    /// <param name="eventHandler">原始事件处理方法</param>
    /// <returns>是否应该继续处理其他事件</returns>
    public async Task<bool> HandleEventErrorAsync(
        Exception exception,
        string eventName,
        object? eventData,
        EventListenerConfiguration configuration,
        Func<object?, Task> eventHandler)
    {
        _logger.LogError(exception, "Event {EventName} execution failed", eventName);

        try
        {
            switch (configuration.ErrorHandling)
            {
                case ErrorHandlingStrategy.LogAndContinue:
                    return await HandleLogAndContinue(exception, eventName, eventData);

                case ErrorHandlingStrategy.RetryThenSkip:
                    return await HandleRetryThenSkip(exception, eventName, eventData, configuration, eventHandler);

                case ErrorHandlingStrategy.RetryThenThrow:
                    return await HandleRetryThenThrow(exception, eventName, eventData, configuration, eventHandler);

                case ErrorHandlingStrategy.ThrowImmediately:
                    throw new EventProcessingException($"Event {eventName} failed", exception);

                case ErrorHandlingStrategy.Custom:
                    return await HandleCustom(exception, eventName, eventData, configuration);

                default:
                    _logger.LogWarning("Unknown error handling strategy: {Strategy}, falling back to LogAndContinue", configuration.ErrorHandling);
                    return await HandleLogAndContinue(exception, eventName, eventData);
            }
        }
        catch (Exception handlerException)
        {
            _logger.LogError(handlerException, "Error handler itself failed for event {EventName}", eventName);
            return false; // 停止处理以避免无限循环
        }
    }

    /// <summary>
    /// 记录错误并继续处理
    /// </summary>
    private async Task<bool> HandleLogAndContinue(Exception exception, string eventName, object? eventData)
    {
        _logger.LogWarning(exception, "Event {EventName} failed, continuing with next events", eventName);
        return await Task.FromResult(true);
    }

    /// <summary>
    /// 重试后跳过
    /// </summary>
    private async Task<bool> HandleRetryThenSkip(
        Exception exception,
        string eventName,
        object? eventData,
        EventListenerConfiguration configuration,
        Func<object?, Task> eventHandler)
    {
        if (configuration.RetryConfig == null)
        {
            _logger.LogWarning("RetryThenSkip strategy specified but no retry configuration provided for event {EventName}", eventName);
            return true;
        }

        var success = await ExecuteWithRetry(eventHandler, eventData, configuration.RetryConfig, eventName);
        if (!success)
        {
            _logger.LogWarning("Event {EventName} failed after all retry attempts, skipping", eventName);
        }

        return true; // 总是继续处理其他事件
    }

    /// <summary>
    /// 重试后抛出异常
    /// </summary>
    private async Task<bool> HandleRetryThenThrow(
        Exception exception,
        string eventName,
        object? eventData,
        EventListenerConfiguration configuration,
        Func<object?, Task> eventHandler)
    {
        if (configuration.RetryConfig == null)
        {
            throw new EventProcessingException($"Event {eventName} failed and no retry configuration provided", exception);
        }

        var success = await ExecuteWithRetry(eventHandler, eventData, configuration.RetryConfig, eventName);
        if (!success)
        {
            throw new EventProcessingException($"Event {eventName} failed after all retry attempts", exception);
        }

        return true;
    }

    /// <summary>
    /// 自定义错误处理
    /// </summary>
    private async Task<bool> HandleCustom(
        Exception exception,
        string eventName,
        object? eventData,
        EventListenerConfiguration configuration)
    {
        if (configuration.ErrorHandler == null)
        {
            _logger.LogError("Custom error handling strategy specified but no error handler provided for event {EventName}", eventName);
            return false;
        }

        try
        {
            return await configuration.ErrorHandler(exception, eventName, eventData, 1);
        }
        catch (Exception customHandlerException)
        {
            _logger.LogError(customHandlerException, "Custom error handler failed for event {EventName}", eventName);
            return false;
        }
    }

    /// <summary>
    /// 执行重试逻辑
    /// </summary>
    private async Task<bool> ExecuteWithRetry(
        Func<object?, Task> eventHandler,
        object? eventData,
        RetryConfiguration retryConfig,
        string eventName)
    {
        var attempt = 1;
        var delay = retryConfig.RetryInterval;

        while (attempt <= retryConfig.MaxAttempts)
        {
            try
            {
                if (attempt > 1)
                {
                    _logger.LogDebug("Retrying event {EventName}, attempt {Attempt}/{MaxAttempts}",
                        eventName, attempt, retryConfig.MaxAttempts);
                }

                await eventHandler(eventData);

                if (attempt > 1)
                {
                    _logger.LogInformation("Event {EventName} succeeded on attempt {Attempt}", eventName, attempt);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Event {EventName} failed on attempt {Attempt}/{MaxAttempts}",
                    eventName, attempt, retryConfig.MaxAttempts);

                // 检查是否应该重试
                if (retryConfig.ShouldRetry != null && !retryConfig.ShouldRetry(ex))
                {
                    _logger.LogInformation("Retry condition not met for event {EventName}, stopping retries", eventName);
                    break;
                }

                attempt++;

                // 如果还有重试机会，等待后继续
                if (attempt <= retryConfig.MaxAttempts)
                {
                    await Task.Delay(delay);

                    // 计算下次重试延迟
                    if (retryConfig.UseExponentialBackoff)
                    {
                        delay = TimeSpan.FromMilliseconds(
                            Math.Min(
                                delay.TotalMilliseconds * retryConfig.BackoffMultiplier,
                                retryConfig.MaxRetryInterval.TotalMilliseconds
                            )
                        );
                    }
                }
            }
        }

        return false;
    }
}

/// <summary>
/// 事件处理异常
/// </summary>
public class EventProcessingException : Exception
{
    public string? EventName { get; }

    public EventProcessingException(string message) : base(message) { }

    public EventProcessingException(string message, Exception innerException) : base(message, innerException) { }

    public EventProcessingException(string message, string eventName) : base(message)
    {
        EventName = eventName;
    }

    public EventProcessingException(string message, string eventName, Exception innerException) : base(message, innerException)
    {
        EventName = eventName;
    }
}

/// <summary>
/// 性能监控的事件包装器
/// </summary>
public class EventPerformanceWrapper
{
    private readonly ILogger<EventPerformanceWrapper> _logger;

    public EventPerformanceWrapper(ILogger<EventPerformanceWrapper>? logger = null)
    {
        _logger = logger ?? NullLogger<EventPerformanceWrapper>.Instance;
    }

    /// <summary>
    /// 包装事件处理以添加性能监控
    /// </summary>
    public async Task<T> WrapWithPerformanceMonitoring<T>(
        string eventName,
        Func<Task<T>> eventHandler,
        bool enableMonitoring = false)
    {
        if (!enableMonitoring)
        {
            return await eventHandler();
        }

        var stopwatch = Stopwatch.StartNew();
        var startTime = DateTime.UtcNow;

        try
        {
            var result = await eventHandler();
            stopwatch.Stop();

            _logger.LogDebug("Event {EventName} completed successfully in {ElapsedMilliseconds}ms",
                eventName, stopwatch.ElapsedMilliseconds);

            // 这里可以添加指标收集
            RecordEventMetrics(eventName, stopwatch.Elapsed, true);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogWarning(ex, "Event {EventName} failed after {ElapsedMilliseconds}ms",
                eventName, stopwatch.ElapsedMilliseconds);

            // 记录失败指标
            RecordEventMetrics(eventName, stopwatch.Elapsed, false);

            throw;
        }
    }

    /// <summary>
    /// 包装无返回值的事件处理
    /// </summary>
    public async Task WrapWithPerformanceMonitoring(
        string eventName,
        Func<Task> eventHandler,
        bool enableMonitoring = false)
    {
        await WrapWithPerformanceMonitoring(eventName, async () =>
        {
            await eventHandler();
            return 0; // 返回虚拟值
        }, enableMonitoring);
    }

    /// <summary>
    /// 记录事件指标
    /// </summary>
    private void RecordEventMetrics(string eventName, TimeSpan elapsed, bool success)
    {
        // 这里可以集成到PulseRPC的指标系统
        // 例如：增加计数器、记录延迟分布等

        if (elapsed.TotalMilliseconds > 1000) // 超过1秒的事件
        {
            _logger.LogInformation("Slow event detected: {EventName} took {ElapsedMilliseconds}ms",
                eventName, elapsed.TotalMilliseconds);
        }
    }
}

/// <summary>
/// 超时处理包装器
/// </summary>
public class EventTimeoutWrapper
{
    private readonly ILogger<EventTimeoutWrapper> _logger;

    public EventTimeoutWrapper(ILogger<EventTimeoutWrapper>? logger = null)
    {
        _logger = logger ?? NullLogger<EventTimeoutWrapper>.Instance;
    }

    /// <summary>
    /// 包装事件处理以添加超时控制
    /// </summary>
    public async Task<T> WrapWithTimeout<T>(
        string eventName,
        Func<CancellationToken, Task<T>> eventHandler,
        TimeSpan? timeout = null)
    {
        if (timeout == null)
        {
            return await eventHandler(CancellationToken.None);
        }

        using var cts = new CancellationTokenSource(timeout.Value);

        try
        {
            return await eventHandler(cts.Token);
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
        {
            _logger.LogWarning("Event {EventName} timed out after {TimeoutMilliseconds}ms",
                eventName, timeout.Value.TotalMilliseconds);

            throw new EventProcessingException($"Event {eventName} timed out after {timeout.Value.TotalMilliseconds}ms", eventName);
        }
    }

    /// <summary>
    /// 包装无返回值的事件处理
    /// </summary>
    public async Task WrapWithTimeout(
        string eventName,
        Func<CancellationToken, Task> eventHandler,
        TimeSpan? timeout = null)
    {
        await WrapWithTimeout(eventName, async (ct) =>
        {
            await eventHandler(ct);
            return 0; // 返回虚拟值
        }, timeout);
    }
}
