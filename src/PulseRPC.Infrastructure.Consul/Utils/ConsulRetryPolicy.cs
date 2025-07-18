using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PulseRPC.Infrastructure.Consul;

/// <summary>
/// Consul重试策略
/// </summary>
public class ConsulRetryPolicy
{
    private readonly ConsulRetryOptions _options;
    private readonly ILogger<ConsulRetryPolicy> _logger;

    public ConsulRetryPolicy(IOptions<ConsulOptions> options, ILogger<ConsulRetryPolicy> logger)
    {
        _options = options?.Value?.Retry ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 执行带重试的操作
    /// </summary>
    /// <typeparam name="T">返回类型</typeparam>
    /// <param name="operation">要执行的操作</param>
    /// <param name="operationName">操作名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    public async Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return await operation();
        }

        var attempt = 0;
        var delay = _options.InitialDelay;

        while (true)
        {
            attempt++;
            
            try
            {
                var result = await operation();
                
                if (attempt > 1)
                {
                    _logger.LogInformation("Operation {OperationName} succeeded on attempt {Attempt}", 
                        operationName, attempt);
                }
                
                return result;
            }
            catch (Exception ex) when (ShouldRetry(ex, attempt))
            {
                _logger.LogWarning(ex, "Operation {OperationName} failed on attempt {Attempt}, retrying in {Delay}ms", 
                    operationName, attempt, delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
                delay = CalculateNextDelay(delay);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Operation {OperationName} failed permanently after {Attempt} attempts", 
                    operationName, attempt);
                throw;
            }
        }
    }

    /// <summary>
    /// 执行带重试的操作（无返回值）
    /// </summary>
    /// <param name="operation">要执行的操作</param>
    /// <param name="operationName">操作名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task ExecuteAsync(
        Func<Task> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async () =>
        {
            await operation();
            return true;
        }, operationName, cancellationToken);
    }

    private bool ShouldRetry(Exception exception, int attempt)
    {
        // 检查是否达到最大重试次数
        if (attempt >= _options.MaxAttempts)
        {
            return false;
        }

        // 检查是否是可重试的异常类型
        var exceptionType = exception.GetType();
        if (_options.RetryableExceptions.Contains(exceptionType))
        {
            return true;
        }

        // 检查是否是可重试的异常基类
        return _options.RetryableExceptions.Any(retryableType => 
            retryableType.IsAssignableFrom(exceptionType));
    }

    private TimeSpan CalculateNextDelay(TimeSpan currentDelay)
    {
        // 指数退避
        var nextDelay = TimeSpan.FromMilliseconds(
            currentDelay.TotalMilliseconds * _options.BackoffMultiplier);

        // 添加随机抖动
        if (_options.JitterFactor > 0)
        {
            var jitter = Random.Shared.NextDouble() * _options.JitterFactor;
            var jitterMultiplier = 1.0 + (jitter * 2 - 1); // -jitter to +jitter
            nextDelay = TimeSpan.FromMilliseconds(nextDelay.TotalMilliseconds * jitterMultiplier);
        }

        // 确保不超过最大延迟
        return nextDelay > _options.MaxDelay ? _options.MaxDelay : nextDelay;
    }
}

/// <summary>
/// Consul异常处理扩展
/// </summary>
public static class ConsulExceptionExtensions
{
    /// <summary>
    /// 判断是否是临时性错误
    /// </summary>
    /// <param name="exception">异常</param>
    /// <returns>是否是临时性错误</returns>
    public static bool IsTransientError(this Exception exception)
    {
        return exception switch
        {
            TimeoutException => true,
            TaskCanceledException => true,
            HttpRequestException httpEx => IsTransientHttpError(httpEx),
            OperationCanceledException => false, // 通常表示正常取消
            _ => false
        };
    }

    private static bool IsTransientHttpError(HttpRequestException httpException)
    {
        // 检查HTTP状态码
        var message = httpException.Message;
        
        // 服务器错误 (5xx) 通常是临时性的
        if (message.Contains("500") || message.Contains("502") || 
            message.Contains("503") || message.Contains("504"))
        {
            return true;
        }

        // 连接错误通常是临时性的
        if (message.Contains("Connection refused") || 
            message.Contains("Connection timeout") ||
            message.Contains("Connection reset"))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 获取用户友好的错误消息
    /// </summary>
    /// <param name="exception">异常</param>
    /// <returns>用户友好的错误消息</returns>
    public static string GetFriendlyMessage(this Exception exception)
    {
        return exception switch
        {
            TimeoutException => "操作超时，请检查网络连接或服务器状态",
            TaskCanceledException => "操作被取消",
            HttpRequestException httpEx when httpEx.Message.Contains("Connection refused") => 
                "无法连接到Consul服务器，请检查服务器是否运行",
            HttpRequestException httpEx when httpEx.Message.Contains("401") => 
                "认证失败，请检查Consul访问令牌",
            HttpRequestException httpEx when httpEx.Message.Contains("403") => 
                "权限不足，请检查Consul访问权限",
            HttpRequestException httpEx when httpEx.Message.Contains("404") => 
                "请求的资源不存在",
            HttpRequestException httpEx when httpEx.Message.Contains("5") => 
                "Consul服务器内部错误",
            _ => exception.Message
        };
    }
}

/// <summary>
/// Consul操作结果
/// </summary>
/// <typeparam name="T">结果类型</typeparam>
public class ConsulResult<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? ErrorMessage { get; set; }
    public Exception? Exception { get; set; }

    public static ConsulResult<T> SuccessResult(T data)
    {
        return new ConsulResult<T>
        {
            Success = true,
            Data = data
        };
    }

    public static ConsulResult<T> ErrorResult(string errorMessage, Exception? exception = null)
    {
        return new ConsulResult<T>
        {
            Success = false,
            ErrorMessage = errorMessage,
            Exception = exception
        };
    }
}

/// <summary>
/// Consul操作结果（无返回值）
/// </summary>
public class ConsulResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public Exception? Exception { get; set; }

    public static ConsulResult SuccessResult()
    {
        return new ConsulResult { Success = true };
    }

    public static ConsulResult ErrorResult(string errorMessage, Exception? exception = null)
    {
        return new ConsulResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            Exception = exception
        };
    }
}