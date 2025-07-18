namespace PulseRPC.Caching;

// 缓存统计信息
public class CacheStatistics2
{
    public long HitCount { get; set; }
    public long MissCount { get; set; }
    public double HitRatio => HitCount + MissCount > 0 ? (double)HitCount / (HitCount + MissCount) : 0;
    public long KeyCount { get; set; }
    public long MemoryUsage { get; set; }
    public DateTime LastResetTime { get; set; }
    public IDictionary<string, object> AdditionalMetrics { get; set; } = new Dictionary<string, object>();
}

public record CacheStatistics(long HitCount, long MissCount, long EvictionCount, int CurrentSize, TimeSpan AverageLoadTime)
{
    public double HitRatio => HitCount + MissCount > 0 ? (double)HitCount / (HitCount + MissCount) : 0.0;
}
