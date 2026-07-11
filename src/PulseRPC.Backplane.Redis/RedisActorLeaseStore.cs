using System.Text;
using Microsoft.Extensions.Options;
using PulseRPC.Clustering;
using StackExchange.Redis;

namespace PulseRPC.Backplane.Redis;

/// <summary>
/// 基于 Redis 单键 Lua 脚本的 <see cref="IActorLeaseStore"/> 实现。
/// </summary>
/// <remarks>
/// <para>
/// 每个 <c>(Hub, Key)</c> 使用一个带 TTL 的 Redis Hash，字段为 <c>node</c> 与 <c>lease</c>。
/// 激活、解析、续租与释放均在单个 Lua 脚本内完成，避免 owner、lease 与 TTL 被分步观察或修改。
/// </para>
/// <para>
/// 本类型不拥有也不释放注入的 <see cref="IConnectionMultiplexer"/>；应用可与 Redis Backplane
/// 共享同一个连接实例及其重连、故障转移配置。
/// </para>
/// </remarks>
public sealed class RedisActorLeaseStore : IActorLeaseStore
{
    private const string ResolveScript = """
        local lease = redis.call('HMGET', KEYS[1], 'node', 'lease')
        if not lease[1] or not lease[2] then
            return {}
        end

        local ttl = redis.call('PTTL', KEYS[1])
        if ttl <= 0 then
            return {}
        end

        return { lease[1], lease[2], ttl }
        """;

    private const string ActivateScript = """
        local lease = redis.call('HMGET', KEYS[1], 'node', 'lease')
        local ttl = redis.call('PTTL', KEYS[1])
        if lease[1] and lease[2] and ttl > 0 then
            return { lease[1], lease[2], ttl }
        end

        redis.call('DEL', KEYS[1])
        redis.call('HSET', KEYS[1], 'node', ARGV[1], 'lease', ARGV[2])
        redis.call('PEXPIRE', KEYS[1], ARGV[3])
        return { ARGV[1], ARGV[2], redis.call('PTTL', KEYS[1]) }
        """;

    private const string RenewScript = """
        local lease = redis.call('HMGET', KEYS[1], 'node', 'lease')
        local ttl = redis.call('PTTL', KEYS[1])
        if lease[1] == ARGV[1] and lease[2] == ARGV[2] and ttl > 0 then
            return redis.call('PEXPIRE', KEYS[1], ARGV[3])
        end

        return 0
        """;

    private const string ReleaseScript = """
        local lease = redis.call('HMGET', KEYS[1], 'node', 'lease')
        local ttl = redis.call('PTTL', KEYS[1])
        if lease[1] == ARGV[1] and lease[2] == ARGV[2] and ttl > 0 then
            return redis.call('DEL', KEYS[1])
        end

        return 0
        """;

    private readonly IDatabase _database;
    private readonly string _keyPrefix;

    /// <summary>创建 Redis Actor 租约存储。</summary>
    public RedisActorLeaseStore(
        IConnectionMultiplexer connection,
        IOptions<RedisActorLeaseStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);

        var value = options.Value ?? throw new ArgumentException("Redis Actor 租约配置不能为空。", nameof(options));
        if (string.IsNullOrWhiteSpace(value.KeyPrefix))
        {
            throw new ArgumentException("Redis Actor 租约键前缀不能为空或空白。", nameof(options));
        }

        if (value.Database < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), value.Database, "Redis 数据库编号必须为 -1 或非负数。");
        }

        _keyPrefix = value.KeyPrefix;
        _database = connection.GetDatabase(value.Database);
    }

    /// <inheritdoc/>
    public async ValueTask<ActorPlacement?> ResolveAsync(
        string hub,
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(hub, key);

        var result = await EvaluateAsync(
            ResolveScript,
            BuildRedisKey(hub, key),
            Array.Empty<RedisValue>(),
            cancellationToken).ConfigureAwait(false);
        return ParsePlacement(result, allowEmpty: true);
    }

    /// <inheritdoc/>
    public async ValueTask<ActorPlacement> ActivateAsync(
        string hub,
        string key,
        string candidateNodeId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(hub, key);
        ValidateRequired(candidateNodeId, nameof(candidateNodeId));
        var leaseMilliseconds = GetLeaseMilliseconds(leaseDuration);
        var candidateLeaseId = Guid.NewGuid().ToString("N");

        var result = await EvaluateAsync(
            ActivateScript,
            BuildRedisKey(hub, key),
            new RedisValue[] { candidateNodeId, candidateLeaseId, leaseMilliseconds },
            cancellationToken).ConfigureAwait(false);
        return ParsePlacement(result, allowEmpty: false)
            ?? throw new InvalidOperationException("Redis 激活脚本未返回 Actor 租约。");
    }

    /// <inheritdoc/>
    public async ValueTask<bool> RenewAsync(
        string hub,
        string key,
        string nodeId,
        string leaseId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(hub, key);
        ValidateRequired(nodeId, nameof(nodeId));
        ValidateRequired(leaseId, nameof(leaseId));
        var leaseMilliseconds = GetLeaseMilliseconds(leaseDuration);

        var result = await EvaluateAsync(
            RenewScript,
            BuildRedisKey(hub, key),
            new RedisValue[] { nodeId, leaseId, leaseMilliseconds },
            cancellationToken).ConfigureAwait(false);
        return ParseBooleanResult(result, "续租");
    }

    /// <inheritdoc/>
    public async ValueTask ReleaseAsync(
        string hub,
        string key,
        string nodeId,
        string leaseId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(hub, key);
        ValidateRequired(nodeId, nameof(nodeId));
        ValidateRequired(leaseId, nameof(leaseId));

        var result = await EvaluateAsync(
            ReleaseScript,
            BuildRedisKey(hub, key),
            new RedisValue[] { nodeId, leaseId },
            cancellationToken).ConfigureAwait(false);
        _ = ParseBooleanResult(result, "释放");
    }

    private async Task<RedisResult> EvaluateAsync(
        string script,
        RedisKey key,
        RedisValue[] values,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var operation = _database.ScriptEvaluateAsync(
            script,
            new[] { key },
            values,
            CommandFlags.DemandMaster);
        return await operation.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private RedisKey BuildRedisKey(string hub, string key)
        => $"{_keyPrefix}:actor-leases:{EncodeKeyPart(hub)}:{EncodeKeyPart(key)}";

    private static string EncodeKeyPart(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static ActorPlacement? ParsePlacement(RedisResult result, bool allowEmpty)
    {
        if (result.IsNull || result.Length == 0)
        {
            if (allowEmpty)
            {
                return null;
            }

            throw new InvalidOperationException("Redis Actor 租约脚本返回了空结果。");
        }

        RedisResult[]? values;
        try
        {
            values = (RedisResult[]?)result;
        }
        catch (Exception ex) when (ex is InvalidCastException or InvalidOperationException)
        {
            throw new InvalidOperationException("Redis Actor 租约脚本返回值不是数组。", ex);
        }

        if (values is null)
        {
            throw new InvalidOperationException("Redis Actor 租约脚本返回值不是数组。");
        }

        if (values.Length != 3)
        {
            throw new InvalidOperationException($"Redis Actor 租约脚本返回了 {values.Length} 个字段，预期为 3 个。");
        }

        var nodeId = (string?)values[0];
        var leaseId = (string?)values[1];
        var ttlMilliseconds = (long)values[2];
        if (string.IsNullOrWhiteSpace(nodeId)
            || string.IsNullOrWhiteSpace(leaseId)
            || ttlMilliseconds <= 0)
        {
            throw new InvalidOperationException("Redis Actor 租约脚本返回了无效的 owner、lease 或 TTL。");
        }

        return new ActorPlacement(nodeId, leaseId, GetExpiryTicks(ttlMilliseconds));
    }

    private static bool ParseBooleanResult(RedisResult result, string operation)
    {
        if (result.IsNull)
        {
            throw new InvalidOperationException($"Redis Actor 租约{operation}脚本返回了空结果。");
        }

        long value;
        try
        {
            value = (long)result;
        }
        catch (Exception ex) when (ex is InvalidCastException or InvalidOperationException)
        {
            throw new InvalidOperationException($"Redis Actor 租约{operation}脚本返回值不是整数。", ex);
        }

        return value switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidOperationException($"Redis Actor 租约{operation}脚本返回了无效状态 {value}。"),
        };
    }

    private static long GetLeaseMilliseconds(TimeSpan leaseDuration)
    {
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), leaseDuration, "Actor 租约时长必须大于零。");
        }

        var milliseconds = leaseDuration.Ticks / TimeSpan.TicksPerMillisecond;
        if (leaseDuration.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            milliseconds++;
        }

        return milliseconds;
    }

    private static long GetExpiryTicks(long ttlMilliseconds)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var maxRemainingTicks = DateTime.MaxValue.Ticks - nowTicks;
        if (ttlMilliseconds > maxRemainingTicks / TimeSpan.TicksPerMillisecond)
        {
            return DateTime.MaxValue.Ticks;
        }

        return nowTicks + ttlMilliseconds * TimeSpan.TicksPerMillisecond;
    }

    private static void ValidateIdentity(string hub, string key)
    {
        ValidateRequired(hub, nameof(hub));
        ValidateRequired(key, nameof(key));
    }

    private static void ValidateRequired(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("值不能为空或空白。", parameterName);
        }
    }
}
