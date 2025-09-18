using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace PulseRPC.Client.Reliability;

/// <summary>
/// 重试策略实现
/// </summary>
public sealed class RetryPolicy : IRetryPolicy
{
    private readonly ILogger<RetryPolicy> _logger;
    private readonly RetryPolicyConfiguration _configuration;
    private readonly Random _random = new();
    private readonly RetryPolicyStatistics _statistics = new();
    private readonly object _statisticsLock = new();

    /// <summary>
    /// 策略名称
    /// </summary>
    public string Name => _configuration.Name;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetryCount => _configuration.MaxRetryCount;

    /// <summary>
    /// 最大总耗时
    /// </summary>
    public TimeSpan? MaxTotalTime => _configuration.MaxTotalTime;

    /// <summary>
    /// 重试开始事件
    /// </summary>
    public event EventHandler<RetryEventArgs>? RetryStarted;

    /// <summary>
    /// 重试失败事件
    /// </summary>
    public event EventHandler<RetryEventArgs>? RetryFailed;

    /// <summary>
    /// 重试成功事件
    /// </summary>
    public event EventHandler<RetryEventArgs>? RetrySucceeded;

    /// <summary>
    /// 构造函数
    /// </summary>
    public RetryPolicy(RetryPolicyConfiguration configuration, ILoggerFactory? loggerFactory = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = loggerFactory?.CreateLogger<RetryPolicy>() ?? NullLogger<RetryPolicy>.Instance;

        _statistics.PolicyName = _configuration.Name;

        ValidateConfiguration();
    }

    /// <summary>
    /// 是否应该重试
    /// </summary>
    public bool ShouldRetry(RetryContext context)
    {
        // 检查重试次数限制
        if (context.AttemptNumber > _configuration.MaxRetryCount)
        {
            return false;
        }

        // 检查总时间限制
        if (_configuration.MaxTotalTime.HasValue && context.TotalElapsedTime > _configuration.MaxTotalTime.Value)
        {
            return false;
        }

        // 检查取消令牌
        if (context.CancellationToken.IsCancellationRequested)
        {
            return false;
        }

        // 检查异常过滤器
        if (context.LastException != null && _configuration.ExceptionFilter != null)
        {
            if (!_configuration.ExceptionFilter(context.LastException))
            {
                return false;
            }
        }

        // 自定义重试条件
        if (_configuration.RetryConditionEvaluator != null)
        {
            return _configuration.RetryConditionEvaluator(context);
        }

        return true;
    }

    /// <summary>
    /// 计算下次重试延迟
    /// </summary>
    public TimeSpan CalculateDelay(RetryContext context)
    {
        // 使用自定义延迟计算器
        if (_configuration.CustomDelayCalculator != null)
        {
            return ApplyJitter(_configuration.CustomDelayCalculator(context));
        }

        TimeSpan delay = _configuration.StrategyType switch
        {
            RetryStrategyType.FixedDelay => _configuration.BaseDelay,
            RetryStrategyType.LinearBackoff => CalculateLinearBackoffDelay(context),
            RetryStrategyType.ExponentialBackoff => CalculateExponentialBackoffDelay(context),
            _ => _configuration.BaseDelay
        };

        // 应用最大延迟限制
        if (delay > _configuration.MaxDelay)
        {
            delay = _configuration.MaxDelay;
        }

        // 应用抖动
        return ApplyJitter(delay);
    }

    /// <summary>
    /// 执行带重试的操作
    /// </summary>
    public async Task<RetryResult<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        if (operation == null)
            throw new ArgumentNullException(nameof(operation));

        var stopwatch = Stopwatch.StartNew();
        var attemptNumber = 0;
        Exception? lastException = null;

        UpdateStatistics(stats => stats.TotalExecutions++);

        while (true)
        {
            attemptNumber++;

            try
            {
                _logger.LogDebug("执行操作，尝试次数: {AttemptNumber}/{MaxRetryCount}", attemptNumber, MaxRetryCount + 1);

                var result = await operation(cancellationToken);

                // 执行成功
                var successResult = new RetryResult<T>(result, attemptNumber, stopwatch.Elapsed);
                UpdateStatistics(stats =>
                {
                    stats.SuccessfulExecutions++;
                    stats.TotalRetries += attemptNumber - 1;
                    UpdateExecutionTime(stats, stopwatch.Elapsed);
                    UpdateRetryDistribution(stats, attemptNumber - 1);
                });

                OnRetrySucceeded(new RetryEventArgs(
                    new RetryContext(attemptNumber, lastException, DateTime.UtcNow, stopwatch.Elapsed, cancellationToken),
                    TimeSpan.Zero));

                return successResult;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = ex;

                var context = new RetryContext(
                    attemptNumber,
                    ex,
                    DateTime.UtcNow,
                    stopwatch.Elapsed,
                    cancellationToken);

                _logger.LogWarning(ex, "操作执行失败，尝试次数: {AttemptNumber}, 异常: {ExceptionType}",
                    attemptNumber, ex.GetType().Name);

                // 检查是否应该重试
                if (!ShouldRetry(context))
                {
                    // 不再重试，返回失败结果
                    var failureResult = new RetryResult<T>(ex, attemptNumber, stopwatch.Elapsed);
                    UpdateStatistics(stats =>
                    {
                        stats.FailedExecutions++;
                        stats.TotalRetries += attemptNumber - 1;
                        UpdateExecutionTime(stats, stopwatch.Elapsed);
                        UpdateRetryDistribution(stats, attemptNumber - 1);
                        UpdateExceptionDistribution(stats, ex);
                    });

                    OnRetryFailed(new RetryEventArgs(context, TimeSpan.Zero));
                    return failureResult;
                }

                // 计算延迟时间
                var delay = CalculateDelay(context);

                _logger.LogDebug("将在 {Delay} 后重试，尝试次数: {AttemptNumber}/{MaxRetryCount}",
                    delay, attemptNumber, MaxRetryCount + 1);

                OnRetryStarted(new RetryEventArgs(context, delay));

                // 等待延迟时间
                if (delay > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(delay, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        var cancelledResult = new RetryResult<T>(
                            new OperationCanceledException("操作被取消", cancellationToken),
                            attemptNumber,
                            stopwatch.Elapsed);

                        UpdateStatistics(stats =>
                        {
                            stats.FailedExecutions++;
                            stats.TotalRetries += attemptNumber - 1;
                            UpdateExecutionTime(stats, stopwatch.Elapsed);
                            UpdateRetryDistribution(stats, attemptNumber - 1);
                        });

                        return cancelledResult;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 执行带重试的操作（无返回值）
    /// </summary>
    public async Task<RetryResult<object?>> ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        if (operation == null)
            throw new ArgumentNullException(nameof(operation));

        return await ExecuteAsync(async ct =>
        {
            await operation(ct);
            return (object?)null;
        }, cancellationToken);
    }

    /// <summary>
    /// 获取统计信息
    /// </summary>
    public RetryPolicyStatistics GetStatistics()
    {
        lock (_statisticsLock)
        {
            return new RetryPolicyStatistics
            {
                PolicyName = _statistics.PolicyName,
                TotalExecutions = _statistics.TotalExecutions,
                SuccessfulExecutions = _statistics.SuccessfulExecutions,
                FailedExecutions = _statistics.FailedExecutions,
                TotalRetries = _statistics.TotalRetries,
                AverageRetries = _statistics.TotalExecutions > 0 ? (double)_statistics.TotalRetries / _statistics.TotalExecutions : 0,
                MaxRetries = _statistics.MaxRetries,
                AverageExecutionTime = _statistics.AverageExecutionTime,
                MaxExecutionTime = _statistics.MaxExecutionTime,
                ExceptionDistribution = new Dictionary<string, long>(_statistics.ExceptionDistribution),
                RetryDistribution = new Dictionary<int, long>(_statistics.RetryDistribution),
                LastUpdated = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// 重置统计信息
    /// </summary>
    public void ResetStatistics()
    {
        lock (_statisticsLock)
        {
            _statistics.TotalExecutions = 0;
            _statistics.SuccessfulExecutions = 0;
            _statistics.FailedExecutions = 0;
            _statistics.TotalRetries = 0;
            _statistics.AverageRetries = 0;
            _statistics.MaxRetries = 0;
            _statistics.AverageExecutionTime = TimeSpan.Zero;
            _statistics.MaxExecutionTime = TimeSpan.Zero;
            _statistics.ExceptionDistribution.Clear();
            _statistics.RetryDistribution.Clear();
            _statistics.LastUpdated = DateTime.UtcNow;
        }

        _logger.LogInformation("重置重试策略统计信息: {PolicyName}", Name);
    }

    /// <summary>
    /// 计算线性退避延迟
    /// </summary>
    private TimeSpan CalculateLinearBackoffDelay(RetryContext context)
    {
        var multiplier = Math.Max(1, context.AttemptNumber - 1);
        return TimeSpan.FromMilliseconds(
            _configuration.BaseDelay.TotalMilliseconds +
            _configuration.LinearStep.TotalMilliseconds * multiplier);
    }

    /// <summary>
    /// 计算指数退避延迟
    /// </summary>
    private TimeSpan CalculateExponentialBackoffDelay(RetryContext context)
    {
        var multiplier = Math.Pow(_configuration.BackoffMultiplier, Math.Max(0, context.AttemptNumber - 1));
        return TimeSpan.FromMilliseconds(_configuration.BaseDelay.TotalMilliseconds * multiplier);
    }

    /// <summary>
    /// 应用抖动
    /// </summary>
    private TimeSpan ApplyJitter(TimeSpan delay)
    {
        if (!_configuration.EnableJitter || _configuration.JitterFactor <= 0)
        {
            return delay;
        }

        var jitterRange = delay.TotalMilliseconds * _configuration.JitterFactor;
        var jitter = (_random.NextDouble() - 0.5) * 2 * jitterRange;
        var jitteredDelay = Math.Max(0, delay.TotalMilliseconds + jitter);

        return TimeSpan.FromMilliseconds(jitteredDelay);
    }

    /// <summary>
    /// 更新统计信息
    /// </summary>
    private void UpdateStatistics(Action<RetryPolicyStatistics> updateAction)
    {
        lock (_statisticsLock)
        {
            updateAction(_statistics);
            _statistics.LastUpdated = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 更新执行时间统计
    /// </summary>
    private void UpdateExecutionTime(RetryPolicyStatistics stats, TimeSpan executionTime)
    {
        if (executionTime > stats.MaxExecutionTime)
        {
            stats.MaxExecutionTime = executionTime;
        }

        // 更新平均执行时间
        var totalTime = stats.AverageExecutionTime.TotalMilliseconds * (stats.TotalExecutions - 1) + executionTime.TotalMilliseconds;
        stats.AverageExecutionTime = TimeSpan.FromMilliseconds(totalTime / stats.TotalExecutions);
    }

    /// <summary>
    /// 更新重试分布统计
    /// </summary>
    private void UpdateRetryDistribution(RetryPolicyStatistics stats, int retryCount)
    {
        stats.RetryDistribution.TryGetValue(retryCount, out var count);
        stats.RetryDistribution[retryCount] = count + 1;

        if (retryCount > stats.MaxRetries)
        {
            stats.MaxRetries = retryCount;
        }
    }

    /// <summary>
    /// 更新异常分布统计
    /// </summary>
    private void UpdateExceptionDistribution(RetryPolicyStatistics stats, Exception exception)
    {
        var exceptionType = exception.GetType().Name;
        stats.ExceptionDistribution.TryGetValue(exceptionType, out var count);
        stats.ExceptionDistribution[exceptionType] = count + 1;
    }

    /// <summary>
    /// 验证配置
    /// </summary>
    private void ValidateConfiguration()
    {
        if (_configuration.MaxRetryCount < 0)
            throw new ArgumentException("最大重试次数不能为负数");

        if (_configuration.BaseDelay < TimeSpan.Zero)
            throw new ArgumentException("基础延迟时间不能为负数");

        if (_configuration.MaxDelay < _configuration.BaseDelay)
            throw new ArgumentException("最大延迟时间不能小于基础延迟时间");

        if (_configuration.BackoffMultiplier <= 1.0)
            throw new ArgumentException("退避倍数必须大于1");

        if (_configuration.JitterFactor < 0 || _configuration.JitterFactor > 1)
            throw new ArgumentException("抖动因子必须在0-1之间");
    }

    /// <summary>
    /// 触发重试开始事件
    /// </summary>
    private void OnRetryStarted(RetryEventArgs e)
    {
        try
        {
            RetryStarted?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "触发重试开始事件时发生错误");
        }
    }

    /// <summary>
    /// 触发重试失败事件
    /// </summary>
    private void OnRetryFailed(RetryEventArgs e)
    {
        try
        {
            RetryFailed?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "触发重试失败事件时发生错误");
        }
    }

    /// <summary>
    /// 触发重试成功事件
    /// </summary>
    private void OnRetrySucceeded(RetryEventArgs e)
    {
        try
        {
            RetrySucceeded?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "触发重试成功事件时发生错误");
        }
    }
}

/// <summary>
/// 重试策略构建器实现
/// </summary>
public sealed class RetryPolicyBuilder : IRetryPolicyBuilder
{
    private readonly RetryPolicyConfiguration _configuration = new();

    /// <summary>
    /// 设置最大重试次数
    /// </summary>
    public IRetryPolicyBuilder WithMaxRetryCount(int maxRetryCount)
    {
        _configuration.MaxRetryCount = maxRetryCount;
        return this;
    }

    /// <summary>
    /// 设置基础延迟
    /// </summary>
    public IRetryPolicyBuilder WithBaseDelay(TimeSpan baseDelay)
    {
        _configuration.BaseDelay = baseDelay;
        return this;
    }

    /// <summary>
    /// 设置最大延迟
    /// </summary>
    public IRetryPolicyBuilder WithMaxDelay(TimeSpan maxDelay)
    {
        _configuration.MaxDelay = maxDelay;
        return this;
    }

    /// <summary>
    /// 设置最大总耗时
    /// </summary>
    public IRetryPolicyBuilder WithMaxTotalTime(TimeSpan maxTotalTime)
    {
        _configuration.MaxTotalTime = maxTotalTime;
        return this;
    }

    /// <summary>
    /// 使用固定延迟策略
    /// </summary>
    public IRetryPolicyBuilder WithFixedDelay()
    {
        _configuration.StrategyType = RetryStrategyType.FixedDelay;
        return this;
    }

    /// <summary>
    /// 使用线性退避策略
    /// </summary>
    public IRetryPolicyBuilder WithLinearBackoff(TimeSpan step)
    {
        _configuration.StrategyType = RetryStrategyType.LinearBackoff;
        _configuration.LinearStep = step;
        return this;
    }

    /// <summary>
    /// 使用指数退避策略
    /// </summary>
    public IRetryPolicyBuilder WithExponentialBackoff(double multiplier = 2.0)
    {
        _configuration.StrategyType = RetryStrategyType.ExponentialBackoff;
        _configuration.BackoffMultiplier = multiplier;
        return this;
    }

    /// <summary>
    /// 启用抖动
    /// </summary>
    public IRetryPolicyBuilder WithJitter(double factor = 0.1)
    {
        _configuration.EnableJitter = true;
        _configuration.JitterFactor = factor;
        return this;
    }

    /// <summary>
    /// 设置异常过滤器
    /// </summary>
    public IRetryPolicyBuilder WithExceptionFilter(Func<Exception, bool> filter)
    {
        _configuration.ExceptionFilter = filter;
        return this;
    }

    /// <summary>
    /// 设置自定义延迟计算器
    /// </summary>
    public IRetryPolicyBuilder WithCustomDelayCalculator(Func<RetryContext, TimeSpan> calculator)
    {
        _configuration.StrategyType = RetryStrategyType.Custom;
        _configuration.CustomDelayCalculator = calculator;
        return this;
    }

    /// <summary>
    /// 设置重试条件评估器
    /// </summary>
    public IRetryPolicyBuilder WithRetryCondition(Func<RetryContext, bool> evaluator)
    {
        _configuration.RetryConditionEvaluator = evaluator;
        return this;
    }

    /// <summary>
    /// 构建重试策略
    /// </summary>
    public IRetryPolicy Build()
    {
        return new RetryPolicy(_configuration);
    }
}
