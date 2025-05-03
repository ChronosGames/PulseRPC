using System.Runtime.CompilerServices;

namespace PulseRPC.Client.Extensions;

/// <summary>
/// WaitHandle的异步扩展方法
/// </summary>
public static class WaitHandleExtensions
{
    /// <summary>
    /// 异步等待WaitHandle被触发
    /// </summary>
    /// <param name="handle">要等待的WaitHandle</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>如果等待成功返回true，如果等待被取消则抛出OperationCanceledException</returns>
    public static async Task<bool> WaitOneAsync(this WaitHandle handle, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();
        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled());

        RegisteredWaitHandle? registeredHandle = null;
        try
        {
            registeredHandle = ThreadPool.RegisterWaitForSingleObject(
                handle,
                (state, timedOut) => ((TaskCompletionSource<bool>)state!).TrySetResult(!timedOut),
                tcs,
                -1,
                true);

            return await tcs.Task;
        }
        finally
        {
            registeredHandle?.Unregister(null);
        }
    }
}
