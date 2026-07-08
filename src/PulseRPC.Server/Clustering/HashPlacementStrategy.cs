using System;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// 基于一致性哈希的 Actor 放置策略。
/// </summary>
/// <remarks>
/// 将 <c>(Hub, Key)</c> 组合成稳定 identity 后交给 <see cref="NodeConsistentHashRing"/>，确保所有节点在相同静态成员
/// 列表下为同一 Actor identity 算出相同 owner，同时避免不同 Hub 的相同业务 key 被强制视为同一个哈希输入。
/// </remarks>
public sealed class HashPlacementStrategy : IActorPlacementStrategy
{
    private readonly NodeConsistentHashRing _hashRing;

    /// <summary>创建 hash placement 策略。</summary>
    public HashPlacementStrategy(NodeConsistentHashRing hashRing)
    {
        _hashRing = hashRing ?? throw new ArgumentNullException(nameof(hashRing));
    }

    /// <inheritdoc/>
    public string SelectOwner(string hub, string key)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(key);
        return _hashRing.GetOwner(BuildIdentity(hub, key));
    }

    /// <summary>构建用于哈希放置的稳定 Actor identity。</summary>
    public static string BuildIdentity(string hub, string key) => $"{hub}:{key}";
}
