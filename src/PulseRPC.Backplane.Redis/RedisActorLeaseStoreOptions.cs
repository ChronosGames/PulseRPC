namespace PulseRPC.Backplane.Redis;

/// <summary>
/// <see cref="RedisActorLeaseStore"/> 配置项。
/// </summary>
public sealed class RedisActorLeaseStoreOptions
{
    /// <summary>
    /// Redis 键前缀，用于隔离不同 PulseRPC 集群或环境。默认 <c>pulserpc</c>。
    /// </summary>
    public string KeyPrefix { get; set; } = "pulserpc";

    /// <summary>
    /// Redis 数据库编号。默认 <c>-1</c>，表示使用 <see cref="StackExchange.Redis.IConnectionMultiplexer"/>
    /// 配置的默认数据库。
    /// </summary>
    public int Database { get; set; } = -1;
}
