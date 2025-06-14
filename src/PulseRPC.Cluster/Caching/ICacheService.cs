namespace PulseRPC.Caching;

public interface ICacheService
{
    // 基本操作
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    Task SetStringAsync(string key, string value, TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task<long> RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);

    // 批量操作
    Task<IDictionary<string, T?>> GetMultipleAsync<T>(IEnumerable<string> keys,
        CancellationToken cancellationToken = default);

    Task SetMultipleAsync<T>(IDictionary<string, T> keyValuePairs, TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    Task<long> RemoveMultipleAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    // 原子操作
    Task<T?> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    Task<long> IncrementAsync(string key, long value = 1, TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    Task<long> DecrementAsync(string key, long value = 1, TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    Task<double> IncrementAsync(string key, double value, TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    // 过期时间管理
    Task<bool> ExpireAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default);
    Task<TimeSpan?> GetTimeToLiveAsync(string key, CancellationToken cancellationToken = default);
    Task<bool> PersistAsync(string key, CancellationToken cancellationToken = default);

    // 哈希操作
    Task<T?> HashGetAsync<T>(string key, string field, CancellationToken cancellationToken = default);
    Task HashSetAsync<T>(string key, string field, T value, CancellationToken cancellationToken = default);
    Task<bool> HashExistsAsync(string key, string field, CancellationToken cancellationToken = default);
    Task<bool> HashDeleteAsync(string key, string field, CancellationToken cancellationToken = default);
    Task<IDictionary<string, T?>> HashGetAllAsync<T>(string key, CancellationToken cancellationToken = default);
    Task<long> HashLengthAsync(string key, CancellationToken cancellationToken = default);

    // 列表操作
    Task<long> ListPushAsync<T>(string key, T value, bool toLeft = true, CancellationToken cancellationToken = default);
    Task<T?> ListPopAsync<T>(string key, bool fromLeft = true, CancellationToken cancellationToken = default);

    Task<IList<T?>> ListRangeAsync<T>(string key, long start = 0, long stop = -1, CancellationToken cancellationToken = default);

    Task<long> ListLengthAsync(string key, CancellationToken cancellationToken = default);

    // 集合操作
    Task<bool> SetAddAsync<T>(string key, T value, CancellationToken cancellationToken = default);
    Task<bool> SetRemoveAsync<T>(string key, T value, CancellationToken cancellationToken = default);
    Task<bool> SetContainsAsync<T>(string key, T value, CancellationToken cancellationToken = default);
    Task<ISet<T?>> SetMembersAsync<T>(string key, CancellationToken cancellationToken = default);
    Task<long> SetLengthAsync(string key, CancellationToken cancellationToken = default);

    // 分布式锁
    Task<IDisposable?> AcquireLockAsync(string key, TimeSpan expiry, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    Task<bool> ReleaseLockAsync(string key, string lockValue, CancellationToken cancellationToken = default);

    // 发布订阅
    Task PublishAsync<T>(string channel, T message, CancellationToken cancellationToken = default);

    Task SubscribeAsync<T>(string channel, Func<string, T, Task> handler, CancellationToken cancellationToken = default);

    Task UnsubscribeAsync(string channel, CancellationToken cancellationToken = default);

    // 监控和统计
    Task<Dictionary<string, object>> GetStatisticsAsync(CancellationToken cancellationToken = default);
    Task<bool> PingAsync(CancellationToken cancellationToken = default);
    Task FlushAllAsync(CancellationToken cancellationToken = default);
}

