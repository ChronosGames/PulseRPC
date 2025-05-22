namespace PulseRPC;

#if !NET6_0_OR_GREATER

internal static class TaskExtensions
{
    /// <summary>
    /// 等待任务在指定的超时时间内完成，或者在取消token触发时取消等待
    /// </summary>
    /// <typeparam name="TResult">任务结果类型</typeparam>
    /// <param name="task">要等待的任务</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>与原始任务具有相同结果的任务</returns>
    /// <exception cref="TaskCanceledException">如果等待被取消</exception>
    public static async Task<TResult> WaitAsync<TResult>(
        this Task<TResult> task, CancellationToken cancellationToken)
    {
        // .NET 6+ 可以直接使用 WaitAsync，但为了兼容性我们自己实现
        var tcs = new TaskCompletionSource<bool>();

        await using var registration = cancellationToken.Register(
            () => tcs.TrySetResult(true),
            useSynchronizationContext: false);

        var completedTask = await Task.WhenAny(task, tcs.Task).ConfigureAwait(false);

        if (completedTask == tcs.Task)
        {
            // 超时或取消
            throw new OperationCanceledException(
                cancellationToken.IsCancellationRequested
                    ? "The operation was canceled."
                    : "The operation timed out.",
                cancellationToken);
        }

        // 任务完成，返回结果
        return await task;
    }

    /// <summary>
    /// 等待任务在指定的超时时间内完成，或者在超时后抛出异常
    /// </summary>
    /// <typeparam name="TResult">任务结果类型</typeparam>
    /// <param name="task">要等待的任务</param>
    /// <param name="timeout">超时时间</param>
    /// <returns>与原始任务具有相同结果的任务</returns>
    /// <exception cref="TimeoutException">如果任务超时</exception>
    public static async Task<TResult> WaitAsync<TResult>(
        this Task<TResult> task, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(timeout);

        try
        {
            return await WaitAsync(task, cts.Token);
        }
        catch (OperationCanceledException) when (!task.IsCanceled)
        {
            throw new TimeoutException($"The operation timed out after {timeout.TotalSeconds} seconds.");
        }
    }

    /// <summary>
    /// 等待任务在指定的超时时间内完成，或者在取消token触发时取消等待
    /// </summary>
    /// <param name="task">要等待的任务</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>与原始任务具有相同结果的任务</returns>
    /// <exception cref="TaskCanceledException">如果等待被取消</exception>
    public static async Task WaitAsync(
        this Task task, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();

        await using var registration = cancellationToken.Register(
            () => tcs.TrySetResult(true),
            useSynchronizationContext: false);

        var completedTask = await Task.WhenAny(task, tcs.Task).ConfigureAwait(false);

        if (completedTask == tcs.Task)
        {
            // 超时或取消
            throw new OperationCanceledException(
                cancellationToken.IsCancellationRequested
                    ? "The operation was canceled."
                    : "The operation timed out.",
                cancellationToken);
        }

        // 任务完成，等待它以传播任何异常
        await task;
    }
}

#endif
