using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PulseRPC.Client.Events;

/// <summary>
/// 性能监控包装器 - 为事件接收器添加监控功能
/// </summary>
public sealed class MonitoredEventReceiver<T> where T : class
{
    private readonly T _inner;
    private readonly SubscriptionContext _context;

    public MonitoredEventReceiver(T inner, SubscriptionContext context)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public T Inner => _inner;
    public SubscriptionContext Context => _context;

    /// <summary>
    /// 创建监控方法调用
    /// </summary>
    public async Task InvokeMethodAsync(string methodName, Func<T, Task> method)
    {
        var stopwatch = Stopwatch.StartNew();
        _context.Metrics.RecordEventReceived(methodName);

        try
        {
            await method(_inner).ConfigureAwait(false);
            _context.Metrics.RecordEventProcessed(methodName, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _context.Metrics.RecordEventError(methodName, ex, stopwatch.Elapsed);
            throw;
        }
    }

    /// <summary>
    /// 创建监控方法调用（同步版本）
    /// </summary>
    public void InvokeMethod(string methodName, Action<T> method)
    {
        var stopwatch = Stopwatch.StartNew();
        _context.Metrics.RecordEventReceived(methodName);

        try
        {
            method(_inner);
            _context.Metrics.RecordEventProcessed(methodName, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _context.Metrics.RecordEventError(methodName, ex, stopwatch.Elapsed);
            throw;
        }
    }
}
