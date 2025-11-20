using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Protocol;

/// <summary>
/// 待处理请求管理器 - 用于匹配请求和响应
/// </summary>
public sealed class PendingRequestManager : IDisposable
{
    private readonly ConcurrentDictionary<int, PendingRequest> _pendingRequests = new();
    private readonly Timer _cleanupTimer;
    private int _nextRequestId = 1;
    private bool _disposed;

    /// <summary>
    /// 默认超时时间（30秒）
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public PendingRequestManager()
    {
        // 每10秒清理一次超时的请求
        _cleanupTimer = new Timer(CleanupTimedOutRequests, null,
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// 创建新的请求ID并注册等待响应
    /// </summary>
    /// <param name="timeout">超时时间，null 使用默认超时</param>
    /// <returns>请求ID和等待任务</returns>
    public (int requestId, Task<ReadOnlyMemory<byte>> responseTask) CreateRequest(TimeSpan? timeout = null)
    {
        ThrowIfDisposed();

        var requestId = Interlocked.Increment(ref _nextRequestId);
        var tcs = new TaskCompletionSource<ReadOnlyMemory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var actualTimeout = timeout ?? DefaultTimeout;
        var expirationTime = DateTime.UtcNow.Add(actualTimeout);

        var pendingRequest = new PendingRequest(requestId, tcs, expirationTime);

        if (!_pendingRequests.TryAdd(requestId, pendingRequest))
        {
            throw new InvalidOperationException($"Request ID {requestId} already exists");
        }

        // 设置超时取消
        var cts = new CancellationTokenSource(actualTimeout);
        cts.Token.Register(() =>
        {
            if (_pendingRequests.TryRemove(requestId, out var req))
            {
                req.TaskCompletionSource.TrySetException(
                    new TimeoutException($"Request {requestId} timed out after {actualTimeout.TotalSeconds}s"));
            }
        });

        return (requestId, tcs.Task);
    }

    /// <summary>
    /// 完成请求（设置响应）
    /// </summary>
    /// <param name="requestId">请求ID</param>
    /// <param name="response">响应数据</param>
    /// <returns>是否成功完成</returns>
    public bool CompleteRequest(int requestId, ReadOnlyMemory<byte> response)
    {
        ThrowIfDisposed();

        if (_pendingRequests.TryRemove(requestId, out var pendingRequest))
        {
            return pendingRequest.TaskCompletionSource.TrySetResult(response);
        }

        return false;
    }

    /// <summary>
    /// 取消请求（设置异常）
    /// </summary>
    /// <param name="requestId">请求ID</param>
    /// <param name="exception">异常</param>
    /// <returns>是否成功取消</returns>
    public bool CancelRequest(int requestId, Exception exception)
    {
        ThrowIfDisposed();

        if (_pendingRequests.TryRemove(requestId, out var pendingRequest))
        {
            return pendingRequest.TaskCompletionSource.TrySetException(exception);
        }

        return false;
    }

    /// <summary>
    /// 取消所有待处理的请求
    /// </summary>
    /// <param name="exception">异常，null 则使用默认异常</param>
    public void CancelAllRequests(Exception? exception = null)
    {
        var ex = exception ?? new OperationCanceledException("All pending requests cancelled");

        foreach (var kvp in _pendingRequests)
        {
            if (_pendingRequests.TryRemove(kvp.Key, out var pendingRequest))
            {
                pendingRequest.TaskCompletionSource.TrySetException(ex);
            }
        }
    }

    /// <summary>
    /// 获取待处理请求数量
    /// </summary>
    public int PendingCount => _pendingRequests.Count;

    private void CleanupTimedOutRequests(object? state)
    {
        if (_disposed) return;

        var now = DateTime.UtcNow;
        var timedOutRequests = new List<int>();

        foreach (var kvp in _pendingRequests)
        {
            if (kvp.Value.ExpirationTime <= now)
            {
                timedOutRequests.Add(kvp.Key);
            }
        }

        foreach (var requestId in timedOutRequests)
        {
            if (_pendingRequests.TryRemove(requestId, out var pendingRequest))
            {
                pendingRequest.TaskCompletionSource.TrySetException(
                    new TimeoutException($"Request {requestId} timed out"));
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PendingRequestManager));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _cleanupTimer?.Dispose();
        CancelAllRequests(new ObjectDisposedException(nameof(PendingRequestManager)));
    }

    private sealed class PendingRequest
    {
        public int RequestId { get; }
        public TaskCompletionSource<ReadOnlyMemory<byte>> TaskCompletionSource { get; }
        public DateTime ExpirationTime { get; }

        public PendingRequest(int requestId, TaskCompletionSource<ReadOnlyMemory<byte>> tcs, DateTime expirationTime)
        {
            RequestId = requestId;
            TaskCompletionSource = tcs;
            ExpirationTime = expirationTime;
        }
    }
}

