namespace PulseRPC.Caching;

// 分布式锁
public interface IDistributedLock : IDisposable
{
    string Key { get; }
    string LockValue { get; }
    DateTime AcquiredAt { get; }
    TimeSpan Expiry { get; }
    bool IsAcquired { get; }
    Task<bool> ExtendAsync(TimeSpan extension, CancellationToken cancellationToken = default);
    Task<bool> ReleaseAsync(CancellationToken cancellationToken = default);
}
