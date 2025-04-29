using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ChatApp.Server;

public interface IResourceLockManager
{
    Task<bool> LockResource(string resourceKey, string requestId, TimeSpan timeout);

    Task UnlockResource(string resourceKey, string requestId);

    bool IsResourceLocked(string resourceKey);

    string GetLockOwner(string resourceKey);
}

public interface ILocalResourceLockManager : IResourceLockManager
{

}

// GameServer上的本地资源锁管理器
public class LocalResourceLockManager : ILocalResourceLockManager {
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _resourceLocks =
        new ConcurrentDictionary<string, SemaphoreSlim>();
    private readonly ConcurrentDictionary<string, string> _lockOwners =
        new ConcurrentDictionary<string, string>();
    private readonly ILogger _logger;

    public LocalResourceLockManager(ILogger logger) {
        _logger = logger;
    }

    // 锁定本地资源
    public async Task<bool> LockResource(string resourceKey, string requestId, TimeSpan timeout) {
        // 获取或创建信号量
        var semaphore = _resourceLocks.GetOrAdd(resourceKey, _ => new SemaphoreSlim(1, 1));

        try {
            // 尝试获取锁
            bool acquired = await semaphore.WaitAsync(timeout);

            if (acquired) {
                // 记录锁持有者
                _lockOwners[resourceKey] = requestId;
                _logger.Debug($"Local lock acquired: {resourceKey} by {requestId}");
                return true;
            }

            _logger.Warning($"Failed to acquire local lock: {resourceKey} by {requestId}, timeout");
            return false;
        }
        catch (Exception ex) {
            _logger.Error($"Error acquiring local lock {resourceKey}: {ex.Message}");
            return false;
        }
    }

    // 解锁本地资源
    public Task UnlockResource(string resourceKey, string requestId) {
        // 验证锁持有者
        if (_lockOwners.TryGetValue(resourceKey, out var currentOwner) &&
            currentOwner == requestId) {

            if (_resourceLocks.TryGetValue(resourceKey, out var semaphore)) {
                try {
                    semaphore.Release();
                    _lockOwners.TryRemove(resourceKey, out _);
                    _logger.Debug($"Local lock released: {resourceKey} by {requestId}");
                }
                catch (SemaphoreFullException) {
                    _logger.Error($"Attempted to release unheld lock: {resourceKey}");
                }
            }
        }
        else {
            _logger.Warning($"Attempted to release lock {resourceKey} by non-owner {requestId}");
        }

        return Task.CompletedTask;
    }

    // 检查资源是否已锁定
    public bool IsResourceLocked(string resourceKey) {
        if (_resourceLocks.TryGetValue(resourceKey, out var semaphore)) {
            return semaphore.CurrentCount == 0;
        }
        return false;
    }

    // 获取锁持有者
    public string GetLockOwner(string resourceKey) {
        _lockOwners.TryGetValue(resourceKey, out var owner);
        return owner;
    }
}
