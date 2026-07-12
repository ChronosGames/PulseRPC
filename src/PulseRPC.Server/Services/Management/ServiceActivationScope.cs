using System.Threading;

namespace PulseRPC.Server.Services.Management;

/// <summary>keyed service 实例回收与外部租约回收之间的内部桥接。</summary>
internal interface IServiceInstanceLeaseLifetime
{
    ValueTask ReleaseAsync(string serviceType, string serviceId, CancellationToken cancellationToken = default);
}

internal sealed class NoopServiceInstanceLeaseLifetime : IServiceInstanceLeaseLifetime
{
    public static readonly NoopServiceInstanceLeaseLifetime Instance = new();

    public ValueTask ReleaseAsync(
        string serviceType,
        string serviceId,
        CancellationToken cancellationToken = default)
        => default;
}

/// <summary>
/// 将 keyed service 的创建结果传回当前路由调用，使 Actor 租约只在实例真正启动后进入心跳，
/// 并且只在激活失败时释放。本类型是运行时内部事务边界，不属于公共服务 API。
/// </summary>
internal sealed class ServiceActivationScope : IDisposable
{
    private const int ActivatedFlag = 1;
    private const int FailedFlag = 2;
    private static readonly AsyncLocal<ServiceActivationScope?> CurrentSlot = new();

    private readonly ServiceActivationScope? _previous;
    private readonly Action? _onActivated;
    private readonly Func<ValueTask>? _onActivationFailed;
    private readonly object _failureSync = new();
    private int _state;
    private int _activatedCallbackState;
    private Task? _failureCallbackTask;
    private int _disposed;

    private ServiceActivationScope(
        Action? onActivated,
        Func<ValueTask>? onActivationFailed)
    {
        _onActivated = onActivated;
        _onActivationFailed = onActivationFailed;
        _previous = CurrentSlot.Value;
        CurrentSlot.Value = this;
    }

    public bool WasActivated => (Volatile.Read(ref _state) & ActivatedFlag) != 0;

    public bool ActivationFailed => (Volatile.Read(ref _state) & FailedFlag) != 0;

    public static ServiceActivationScope Enter(
        Action? onActivated = null,
        Func<ValueTask>? onActivationFailed = null)
        => new(onActivated, onActivationFailed);

    public static void MarkActivated()
        => CurrentSlot.Value?.MarkActivatedCore();

    public static void MarkFailed()
    {
        if (CurrentSlot.Value is { } scope)
        {
            _ = scope.MarkFailedCoreAsync();
        }
    }

    public static ValueTask MarkFailedAsync()
        => CurrentSlot.Value?.MarkFailedCoreAsync() ?? default;

    /// <summary>
    /// 将当前调用作用域绑定到共享激活任务。即使调用方只取消自己的等待，作用域也会在
    /// 底层激活最终成功或失败时完成 heartbeat/release 回调。
    /// </summary>
    public static void Observe(Task activationTask)
    {
        ArgumentNullException.ThrowIfNull(activationTask);
        if (CurrentSlot.Value is { } scope)
        {
            _ = scope.ObserveCoreAsync(activationTask);
        }
    }

    public ValueTask WaitForFailureCallbackAsync()
    {
        lock (_failureSync)
        {
            return _failureCallbackTask is null
                ? default
                : new ValueTask(_failureCallbackTask);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (ReferenceEquals(CurrentSlot.Value, this))
        {
            CurrentSlot.Value = _previous;
        }
    }

    private void SetFlag(int flag)
    {
        int current;
        int updated;
        do
        {
            current = Volatile.Read(ref _state);
            updated = current | flag;
        }
        while (Interlocked.CompareExchange(ref _state, updated, current) != current);
    }

    private void MarkActivatedCore()
    {
        SetFlag(ActivatedFlag);
        if (Interlocked.Exchange(ref _activatedCallbackState, 1) != 0)
        {
            return;
        }

        _onActivated?.Invoke();
    }

    private ValueTask MarkFailedCoreAsync()
    {
        SetFlag(FailedFlag);
        lock (_failureSync)
        {
            _failureCallbackTask ??= InvokeFailureCallbackAsync();
            return new ValueTask(_failureCallbackTask);
        }
    }

    private async Task ObserveCoreAsync(Task activationTask)
    {
        try
        {
            await activationTask.ConfigureAwait(false);
            MarkActivatedCore();
        }
        catch
        {
            await MarkFailedCoreAsync().ConfigureAwait(false);
        }
    }

    private async Task InvokeFailureCallbackAsync()
    {
        if (_onActivationFailed is null)
        {
            return;
        }

        try
        {
            await _onActivationFailed().ConfigureAwait(false);
        }
        catch
        {
            // 回调负责记录具体目录错误；租约清理失败不能覆盖原始激活异常。
        }
    }
}
