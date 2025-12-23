using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace PulseRPC.Client.Events;

/// <summary>
/// 异步方法辅助扩展 - 为监控包装器提供异步支持
/// </summary>
public static class AsyncMethodHelpers
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async Task CompleteTaskWithMetrics(Task task, string methodName, Stopwatch stopwatch)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask CompleteValueTaskWithMetrics(ValueTask valueTask, string methodName, Stopwatch stopwatch)
    {
        try
        {
            await valueTask.ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    public static async Task<T> WithRetryAsync<T>(Func<Task<T>> operation, int maxRetries, TimeSpan delay)
    {
        Exception? lastException = null;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt == maxRetries)
                    break;
                await Task.Delay(delay).ConfigureAwait(false);
            }
        }
        throw lastException!;
    }
}
