using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ChatApp.Server;

// 分布式锁服务接口
public interface IDistributedLockService {
    Task<LockResult> AcquireGlobalLock(string resourceKey, string requestId, string serverNodeId, TimeSpan timeout);
    Task ReleaseGlobalLock(string resourceKey, string requestId, string serverNodeId);
    Task<LockOwnerInfo> GetLockInfo(string resourceKey);
}

// Redis实现的分布式锁服务
public class RedisDistributedLockService : IDistributedLockService {
    private readonly ConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger _logger;

    public RedisDistributedLockService(string redisConnectionString, ILogger logger) {
        _redis = ConnectionMultiplexer.Connect(redisConnectionString);
        _db = _redis.GetDatabase();
        _logger = logger;
    }

    // 获取全局分布式锁
    public async Task<LockResult> AcquireGlobalLock(
        string resourceKey, string requestId, string serverNodeId, TimeSpan timeout) {

        string lockKey = $"global:lock:{resourceKey}";
        string lockValue = CreateLockValue(requestId, serverNodeId);

        bool acquired = await _db.StringSetAsync(
            lockKey,
            lockValue,
            timeout,
            When.NotExists);

        if (acquired) {
            _logger.Debug($"Global lock acquired: {resourceKey} by {requestId} on {serverNodeId}");
            return new LockResult { Success = true };
        }

        // 锁获取失败，返回当前持有者信息
        string currentValue = await _db.StringGetAsync(lockKey);
        if (!string.IsNullOrEmpty(currentValue)) {
            var parts = currentValue.Split(':');
            if (parts.Length == 2) {
                return new LockResult {
                    Success = false,
                    CurrentOwner = new LockOwnerInfo {
                        RequestId = parts[0],
                        ServerNodeId = parts[1]
                    }
                };
            }
        }

        return new LockResult { Success = false };
    }

    // 释放全局分布式锁
    public async Task ReleaseGlobalLock(string resourceKey, string requestId, string serverNodeId) {
        string lockKey = $"global:lock:{resourceKey}";
        string expectedValue = CreateLockValue(requestId, serverNodeId);

        // 使用Lua脚本确保只有锁持有者可以释放锁
        string script = @"
            if redis.call('get', KEYS[1]) == ARGV[1] then
                return redis.call('del', KEYS[1])
            else
                return 0
            end";

        var result = await _db.ScriptEvaluateAsync(
            script,
            new RedisKey[] { lockKey },
            new RedisValue[] { expectedValue });

        bool released = result.ToString() == "1";

        if (released) {
            _logger.Debug($"Global lock released: {resourceKey} by {requestId} on {serverNodeId}");
        }
        else {
            _logger.Warning($"Failed to release global lock {resourceKey}: not the owner");
        }
    }

    // 获取锁信息
    public async Task<LockOwnerInfo> GetLockInfo(string resourceKey) {
        string lockKey = $"global:lock:{resourceKey}";
        string value = await _db.StringGetAsync(lockKey);

        if (string.IsNullOrEmpty(value)) {
            return null; // 没有锁
        }

        var parts = value.Split(':');
        if (parts.Length == 2) {
            return new LockOwnerInfo {
                RequestId = parts[0],
                ServerNodeId = parts[1]
            };
        }

        return null;
    }

    private string CreateLockValue(string requestId, string serverNodeId) {
        return $"{requestId}:{serverNodeId}";
    }
}

// 锁结果
public class LockResult {
    public bool Success { get; set; }
    public LockOwnerInfo CurrentOwner { get; set; }
}

// 锁持有者信息
public class LockOwnerInfo {
    public string RequestId { get; set; }
    public string ServerNodeId { get; set; }
}
