using System.Collections.Concurrent;

namespace PulseRPC.Server;

/// <summary>
/// 线程安全的服务集合
/// </summary>
public class ConcurrentServiceCollection
{
    private readonly ConcurrentDictionary<string, ServiceRegistration> _services = new();
    private readonly ReaderWriterLockSlim _lock = new();

    // 按时间分桶存储心跳信息,key为时间戳(按分钟)
    private readonly ConcurrentDictionary<long, HashSet<string>> _heartbeatBuckets = new();

    // 服务心跳时间记录
    private readonly ConcurrentDictionary<string, DateTime> _lastHeartbeats = new();

    /// <summary>
    /// 添加服务
    /// </summary>
    public bool TryAdd(string serviceKey, ServiceRegistration registration)
    {
        var result = _services.TryAdd(serviceKey, registration);
        if (result)
        {
            UpdateHeartbeat(serviceKey);
        }
        return result;
    }

    /// <summary>
    /// 移除服务
    /// </summary>
    public bool TryRemove(string serviceKey, out ServiceRegistration? registration)
    {
        var result = _services.TryRemove(serviceKey, out registration);
        if (result)
        {
            RemoveHeartbeat(serviceKey);
        }
        return result;
    }

    /// <summary>
    /// 获取服务
    /// </summary>
    public bool TryGetValue(string serviceKey, out ServiceRegistration? registration)
    {
        return _services.TryGetValue(serviceKey, out registration);
    }

    /// <summary>
    /// 获取所有服务
    /// </summary>
    public List<ServiceRegistration> GetAll()
    {
        _lock.EnterReadLock();
        try
        {
            return [.._services.Values];
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// 更新服务心跳
    /// </summary>
    public bool UpdateHeartbeat(string serviceKey)
    {
        if (!_services.ContainsKey(serviceKey))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        _lastHeartbeats.AddOrUpdate(serviceKey, now, (_, _) => now);

        // 添加到时间分桶
        var bucket = GetTimeBucket(now);
        _heartbeatBuckets.AddOrUpdate(bucket,
            _ =>
            {
                var set = new HashSet<string> { serviceKey };
                return set;
            },
            (_, set) =>
            {
                _lock.EnterWriteLock();
                try
                {
                    set.Add(serviceKey);
                    return set;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            });

        return true;
    }

    /// <summary>
    /// 移除心跳记录
    /// </summary>
    private void RemoveHeartbeat(string serviceKey)
    {
        _lastHeartbeats.TryRemove(serviceKey, out _);

        // 从所有时间分桶中移除
        _lock.EnterWriteLock();
        try
        {
            foreach (var bucket in _heartbeatBuckets.Values)
            {
                bucket.Remove(serviceKey);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 获取过期的服务
    /// </summary>
    public List<(string serviceKey, ServiceRegistration registration)> GetExpiredServices(TimeSpan timeout)
    {
        var result = new List<(string, ServiceRegistration)>();
        var threshold = DateTime.UtcNow - timeout;
        var thresholdBucket = GetTimeBucket(threshold);

        // 清理过期的时间分桶
        _lock.EnterWriteLock();
        try
        {
            foreach (var bucket in _heartbeatBuckets.Keys)
            {
                if (bucket < thresholdBucket)
                {
                    _heartbeatBuckets.TryRemove(bucket, out _);
                }
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        // 检查所有服务的心跳
        foreach (var (serviceKey, lastHeartbeat) in _lastHeartbeats)
        {
            if (lastHeartbeat < threshold && _services.TryGetValue(serviceKey, out var registration))
            {
                result.Add((serviceKey, registration));
            }
        }

        return result;
    }

    /// <summary>
    /// 获取时间分桶(按分钟)
    /// </summary>
    private static long GetTimeBucket(DateTime time)
    {
        return new DateTimeOffset(time).ToUnixTimeSeconds() / 60;
    }

    /// <summary>
    /// 清理资源
    /// </summary>
    public void Dispose()
    {
        _lock.Dispose();
    }
}
