using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace PulseRPC.Server.Routing;

/// <summary>
/// 精确一次（<see cref="PulseRPC.DeliveryMode.ExactlyOnce"/>）投递的接收方去重集
/// （设计文档 §10.3："去重集有界（时间/条数）"、"落在 Actor 侧"）。
/// </summary>
/// <remarks>
/// <para>
/// 按 <c>scopeKey</c>（通常为 <c>"{Hub}:{Key}"</c>，即目标 Actor 身份）分桶维护一个有界的
/// <c>MessageId</c> 集合：同一 <c>(scopeKey, messageId)</c> 在窗口内第二次出现时 <see cref="TryReserve"/>
/// 返回 <c>false</c>（应跳过执行），达成"效果幂等"。窗口同时受<strong>时间</strong>（<c>window</c>）
/// 与<strong>容量</strong>（<c>maxEntriesPerScope</c>）双重约束，防止无界内存增长；
/// 极晚到达的重复（超出窗口）不再保证去重——这是"有效一次"语义的既定边界（§10.3），而非缺陷。
/// </para>
/// <para>
/// <strong>已知限制</strong>（对应设计文档 §15.1 风险 #9）：<c>scopeKey</c> 桶本身当前不随 Actor
/// 生命周期主动回收（惰性依赖窗口内没有新消息时桶内条目自然过期），长期持有大量互不相同的
/// scopeKey 会造成桶字典本身缓慢增长；跨节点属主变更（租约转移）后，新属主节点的去重状态从空
/// 开始，之前窗口内的去重记忆不会迁移——这与设计文档"跨激活状态不保留"的既定语义一致。
/// </para>
/// </remarks>
public sealed class MessageDeduplicationCache
{
    private sealed class ScopeState
    {
        public readonly object Lock = new();
        public readonly Dictionary<Guid, long> SeenAtMillis = new();
        public readonly Queue<Guid> InsertionOrder = new();

        /// <summary>
        /// 墓碑标记：空桶回收（<see cref="MessageDeduplicationCache.Sweep"/>）在锁内把它置 <c>true</c> 并从字典移除后，
        /// 任何在 <c>GetOrAdd</c> 与取锁之间拿到本（已被移除的）实例的线程会在取锁后检测到并重试 <c>GetOrAdd</c>，
        /// 避免把去重记录写进一个已不在字典中的孤立桶。
        /// </summary>
        public bool Removed;
    }

    private readonly ConcurrentDictionary<string, ScopeState> _scopes = new(StringComparer.Ordinal);
    private readonly long _windowMillis;
    private readonly int _maxEntriesPerScope;
    private readonly int _sweepInterval;
    private int _reserveCounter;

    /// <summary>创建去重缓存。</summary>
    /// <param name="window">去重窗口（默认 5 分钟）：超出该时长的记录视为已过期，不再参与去重判定。</param>
    /// <param name="maxEntriesPerScope">单个 scope 内最多保留的 MessageId 数量（默认 4096），超出后淘汰最早写入的条目。</param>
    /// <param name="sweepInterval">每处理多少次 <see cref="TryReserve"/> 触发一次空桶回收 <see cref="Sweep"/>（默认 1024）。</param>
    public MessageDeduplicationCache(TimeSpan? window = null, int maxEntriesPerScope = 4096, int sweepInterval = 1024)
    {
        if (maxEntriesPerScope <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntriesPerScope), maxEntriesPerScope, "必须大于 0");
        }

        if (sweepInterval <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sweepInterval), sweepInterval, "必须大于 0");
        }

        _windowMillis = (long)(window ?? TimeSpan.FromMinutes(5)).TotalMilliseconds;
        _maxEntriesPerScope = maxEntriesPerScope;
        _sweepInterval = sweepInterval;
    }

    /// <summary>
    /// 当前持有去重记录的 scope 桶数量（用于诊断/测试断言空桶回收是否生效）。
    /// </summary>
    public int ScopeCount => _scopes.Count;

    /// <summary>
    /// 尝试为 <c>(scopeKey, messageId)</c> 预占一个去重名额：首次出现（或已过期）返回 <c>true</c>
    /// （调用方应继续执行），已存在且未过期返回 <c>false</c>（调用方应跳过执行）。
    /// </summary>
    public bool TryReserve(string scopeKey, Guid messageId)
    {
        ArgumentNullException.ThrowIfNull(scopeKey);

        var now = Environment.TickCount64;

        bool result;
        while (true)
        {
            var state = _scopes.GetOrAdd(scopeKey, static _ => new ScopeState());
            lock (state.Lock)
            {
                if (state.Removed)
                {
                    // 该桶已被并发的空桶回收从字典移除：重试 GetOrAdd 取得（或新建）在册的桶。
                    continue;
                }

                PruneExpired(state, now);

                if (state.SeenAtMillis.ContainsKey(messageId))
                {
                    return false;
                }

                while (state.InsertionOrder.Count >= _maxEntriesPerScope)
                {
                    var evicted = state.InsertionOrder.Dequeue();
                    state.SeenAtMillis.Remove(evicted);
                }

                state.SeenAtMillis[messageId] = now;
                state.InsertionOrder.Enqueue(messageId);
                result = true;
            }

            break;
        }

        // 摊销式空桶回收：每 _sweepInterval 次预占触发一次，避免长期持有大量互不相同 scopeKey 时
        // 桶字典本身缓慢增长（对应设计文档 §15.1 风险 #9）。
        if (Interlocked.Increment(ref _reserveCounter) % _sweepInterval == 0)
        {
            Sweep();
        }

        return result;
    }

    /// <summary>
    /// 回收所有已过期且为空的 scope 桶，使桶字典规模随活跃 Actor 数量收敛（而非只增不减）。
    /// 通常由 <see cref="TryReserve"/> 摊销式触发；也可由外部（如后台定时器）显式调用。
    /// </summary>
    public void Sweep()
    {
        var now = Environment.TickCount64;

        foreach (var kvp in _scopes)
        {
            var state = kvp.Value;
            lock (state.Lock)
            {
                if (state.Removed)
                {
                    continue;
                }

                PruneExpired(state, now);

                if (state.SeenAtMillis.Count == 0)
                {
                    // 墓碑 + 原子移除（仅当字典中该键仍映射到本实例时才移除），与 TryReserve 的 Removed 重试配合，
                    // 确保不会丢失并发写入：先置墓碑再移除，之后取到本实例的写入方会因 Removed 而重试。
                    state.Removed = true;
                    ((System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<string, ScopeState>>)_scopes)
                        .Remove(new System.Collections.Generic.KeyValuePair<string, ScopeState>(kvp.Key, state));
                }
            }
        }
    }

    /// <summary>
    /// 释放一次预占（例如实际执行在重试耗尽后仍然失败）：使后续携带同一 <c>(scopeKey, messageId)</c>
    /// 的合法重试可以再次通过 <see cref="TryReserve"/>，而不会被永久性地误判为重复。
    /// </summary>
    public void Release(string scopeKey, Guid messageId)
    {
        if (_scopes.TryGetValue(scopeKey, out var state))
        {
            lock (state.Lock)
            {
                // 不从 InsertionOrder 队列中移除（避免 O(n) 扫描）：PruneExpired/淘汰逻辑在遇到
                // 已不在字典中的条目时会自然跳过，不影响正确性，仅浪费极小的队列槽位。
                state.SeenAtMillis.Remove(messageId);
            }
        }
    }

    private void PruneExpired(ScopeState state, long nowMillis)
    {
        while (state.InsertionOrder.Count > 0)
        {
            var oldest = state.InsertionOrder.Peek();
            if (state.SeenAtMillis.TryGetValue(oldest, out var seenAt))
            {
                if (nowMillis - seenAt <= _windowMillis)
                {
                    // 队首未过期：队列按插入顺序排列，其余条目必然更晚插入、同样未过期。
                    break;
                }

                state.SeenAtMillis.Remove(oldest);
            }

            state.InsertionOrder.Dequeue();
        }
    }
}
