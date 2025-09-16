using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace PulseRPC.Client.Core.Health;

/// <summary>
/// 健康检查管理器
/// </summary>
public sealed class HealthCheckManager : IHealthCheckManager, IDisposable
{
    private readonly ConcurrentDictionary<string, IHealthChecker> _checkers = new();
    private readonly ConcurrentQueue<HealthCheckEventArgs> _history = new();
    private readonly ILogger<HealthCheckManager> _logger;
    private readonly object _historyLock = new();
    private volatile bool _disposed;

    /// <summary>
    /// 健康检查完成事件
    /// </summary>
    public event EventHandler<HealthCheckEventArgs>? HealthCheckCompleted;

    /// <summary>
    /// 构造函数
    /// </summary>
    public HealthCheckManager(ILogger<HealthCheckManager>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<HealthCheckManager>.Instance;
        _logger.LogDebug("健康检查管理器已创建");
    }

    /// <summary>
    /// 注册健康检查器
    /// </summary>
    public void RegisterChecker(IHealthChecker checker)
    {
        ArgumentNullException.ThrowIfNull(checker);

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(HealthCheckManager));
        }

        if (_checkers.TryAdd(checker.Name, checker))
        {
            _logger.LogInformation("健康检查器已注册: {CheckerName}, 支持类型: {SupportedTypes}",
                checker.Name, string.Join(", ", checker.SupportedTargetTypes.Select(t => t.Name)));
        }
        else
        {
            _logger.LogWarning("健康检查器已存在，注册失败: {CheckerName}", checker.Name);
            throw new InvalidOperationException($"健康检查器已存在: {checker.Name}");
        }
    }

    /// <summary>
    /// 取消注册健康检查器
    /// </summary>
    public bool UnregisterChecker(string checkerName)
    {
        ArgumentException.ThrowIfNullOrEmpty(checkerName);

        if (_disposed)
        {
            return false;
        }

        if (_checkers.TryRemove(checkerName, out var checker))
        {
            _logger.LogInformation("健康检查器已取消注册: {CheckerName}", checkerName);

            // 如果检查器实现了IDisposable，释放资源
            if (checker is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "释放健康检查器资源失败: {CheckerName}", checkerName);
                }
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// 获取健康检查器
    /// </summary>
    public IHealthChecker? GetChecker(string checkerName)
    {
        ArgumentException.ThrowIfNullOrEmpty(checkerName);

        return _checkers.TryGetValue(checkerName, out var checker) ? checker : null;
    }

    /// <summary>
    /// 获取所有检查器
    /// </summary>
    public IReadOnlyList<IHealthChecker> GetAllCheckers()
    {
        return _checkers.Values.ToList();
    }

    /// <summary>
    /// 执行健康检查
    /// </summary>
    public async Task<HealthCheckResult> CheckAsync(string checkerName, object target, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(checkerName);
        ArgumentNullException.ThrowIfNull(target);

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(HealthCheckManager));
        }

        if (!_checkers.TryGetValue(checkerName, out var checker))
        {
            var result = HealthCheckResult.Unknown(
                $"未找到健康检查器: {checkerName}",
                TimeSpan.Zero);

            RecordHealthCheck(checkerName, target, result);
            return result;
        }

        var context = new HealthCheckContext(checkerName, target, cancellationToken: cancellationToken);

        _logger.LogDebug("开始健康检查: {CheckerName}, 目标类型: {TargetType}",
            checkerName, target.GetType().Name);

        var startTime = DateTime.UtcNow;
        HealthCheckResult checkResult;

        try
        {
            checkResult = await checker.CheckAsync(context);

            var duration = DateTime.UtcNow - startTime;
            _logger.LogDebug("健康检查完成: {CheckerName}, 状态: {Status}, 耗时: {Duration}ms",
                checkerName, checkResult.Status, duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            checkResult = HealthCheckResult.Unhealthy(
                $"健康检查异常: {ex.Message}",
                duration,
                ex);

            _logger.LogError(ex, "健康检查失败: {CheckerName}", checkerName);
        }

        RecordHealthCheck(checkerName, target, checkResult);
        return checkResult;
    }

    /// <summary>
    /// 执行批量健康检查
    /// </summary>
    public async Task<IReadOnlyDictionary<string, HealthCheckResult>> CheckAllAsync(object target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(HealthCheckManager));
        }

        var targetType = target.GetType();
        var applicableCheckers = _checkers.Values
            .Where(checker => checker.SupportsTarget(targetType))
            .ToList();

        _logger.LogDebug("开始批量健康检查: 目标类型: {TargetType}, 适用检查器数量: {CheckerCount}",
            targetType.Name, applicableCheckers.Count);

        var results = new ConcurrentDictionary<string, HealthCheckResult>();

        // 并行执行所有适用的健康检查
        var tasks = applicableCheckers.Select(async checker =>
        {
            try
            {
                var result = await CheckAsync(checker.Name, target, cancellationToken);
                results.TryAdd(checker.Name, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量健康检查失败: {CheckerName}", checker.Name);
                var errorResult = HealthCheckResult.Unhealthy(
                    $"检查失败: {ex.Message}",
                    TimeSpan.Zero,
                    ex);
                results.TryAdd(checker.Name, errorResult);
            }
        });

        await Task.WhenAll(tasks);

        _logger.LogDebug("批量健康检查完成: 目标类型: {TargetType}, 结果数量: {ResultCount}",
            targetType.Name, results.Count);

        return results;
    }

    /// <summary>
    /// 获取健康检查历史
    /// </summary>
    public IReadOnlyList<HealthCheckEventArgs> GetHistory(string? checkerName = null, int maxCount = 100)
    {
        if (_disposed)
        {
            return Array.Empty<HealthCheckEventArgs>();
        }

        lock (_historyLock)
        {
            var history = _history.ToList();

            if (!string.IsNullOrEmpty(checkerName))
            {
                history = history.Where(h => h.CheckerName.Equals(checkerName, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return history
                .OrderByDescending(h => h.Result.CheckTime)
                .Take(maxCount)
                .ToList();
        }
    }

    /// <summary>
    /// 清理历史记录
    /// </summary>
    public void ClearHistory()
    {
        if (_disposed)
        {
            return;
        }

        lock (_historyLock)
        {
            while (_history.TryDequeue(out _))
            {
                // 清空队列
            }
        }

        _logger.LogDebug("健康检查历史已清理");
    }

    /// <summary>
    /// 记录健康检查结果
    /// </summary>
    private void RecordHealthCheck(string checkerName, object target, HealthCheckResult result)
    {
        if (_disposed)
        {
            return;
        }

        var eventArgs = new HealthCheckEventArgs(checkerName, target, result);

        // 添加到历史记录
        lock (_historyLock)
        {
            _history.Enqueue(eventArgs);

            // 限制历史记录数量，避免内存泄漏
            while (_history.Count > 1000)
            {
                _history.TryDequeue(out _);
            }
        }

        // 触发事件
        try
        {
            HealthCheckCompleted?.Invoke(this, eventArgs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "健康检查事件处理失败: {CheckerName}", checkerName);
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _logger.LogInformation("正在关闭健康检查管理器");

        // 释放所有检查器
        foreach (var checker in _checkers.Values)
        {
            if (checker is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "释放健康检查器失败: {CheckerName}", checker.Name);
                }
            }
        }

        _checkers.Clear();
        ClearHistory();

        _logger.LogInformation("健康检查管理器已关闭");
    }
}
