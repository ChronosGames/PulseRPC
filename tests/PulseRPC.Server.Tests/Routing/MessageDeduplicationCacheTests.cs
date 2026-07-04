using System;
using System.Threading;
using FluentAssertions;
using PulseRPC.Server.Routing;
using Xunit;

namespace PulseRPC.Server.Tests.Routing;

/// <summary>
/// 回归测试：<see cref="MessageDeduplicationCache"/>（§P6/§10.3）—— 精确一次投递的接收方去重集：
/// 同一 <c>(scopeKey, messageId)</c> 首次出现应放行，窗口内重复出现应拦截；不同 scope 相互隔离；
/// 容量与 TTL 双重有界；<see cref="MessageDeduplicationCache.Release"/> 支持失败回滚。
/// </summary>
public class MessageDeduplicationCacheTests
{
    [Fact]
    public void TryReserve_FirstOccurrence_ReturnsTrue()
    {
        var cache = new MessageDeduplicationCache();

        cache.TryReserve("Hub:actor-1", Guid.NewGuid()).Should().BeTrue();
    }

    [Fact]
    public void TryReserve_DuplicateWithinWindow_ReturnsFalse()
    {
        var cache = new MessageDeduplicationCache();
        var messageId = Guid.NewGuid();

        cache.TryReserve("Hub:actor-1", messageId).Should().BeTrue();
        cache.TryReserve("Hub:actor-1", messageId).Should().BeFalse("同一 scope 内重复的 MessageId 应被判定为重复消息");
    }

    [Fact]
    public void TryReserve_SameMessageIdInDifferentScopes_AreIndependent()
    {
        var cache = new MessageDeduplicationCache();
        var messageId = Guid.NewGuid();

        cache.TryReserve("Hub:actor-1", messageId).Should().BeTrue();
        cache.TryReserve("Hub:actor-2", messageId).Should().BeTrue("不同 scopeKey（不同 Actor 身份）的去重状态必须相互隔离");
    }

    [Fact]
    public void TryReserve_DifferentMessageIdsInSameScope_AreBothAccepted()
    {
        var cache = new MessageDeduplicationCache();

        cache.TryReserve("Hub:actor-1", Guid.NewGuid()).Should().BeTrue();
        cache.TryReserve("Hub:actor-1", Guid.NewGuid()).Should().BeTrue();
    }

    [Fact]
    public void Release_AllowsSameMessageIdToBeReservedAgain()
    {
        var cache = new MessageDeduplicationCache();
        var messageId = Guid.NewGuid();

        cache.TryReserve("Hub:actor-1", messageId).Should().BeTrue();
        cache.Release("Hub:actor-1", messageId);

        cache.TryReserve("Hub:actor-1", messageId).Should().BeTrue("释放预占后，同一 MessageId 的合法重试应能再次通过");
    }

    [Fact]
    public void Release_OnUnknownScopeOrMessageId_DoesNotThrow()
    {
        var cache = new MessageDeduplicationCache();

        var act = () => cache.Release("never-seen-scope", Guid.NewGuid());

        act.Should().NotThrow();
    }

    [Fact]
    public void TryReserve_ExpiredEntry_IsTreatedAsNewAndAccepted()
    {
        var cache = new MessageDeduplicationCache(window: TimeSpan.FromMilliseconds(20));
        var messageId = Guid.NewGuid();

        cache.TryReserve("Hub:actor-1", messageId).Should().BeTrue();
        Thread.Sleep(60);

        cache.TryReserve("Hub:actor-1", messageId).Should().BeTrue("超出去重窗口的记录应视为已过期，允许再次通过（§10.3 有效一次的既定边界）");
    }

    [Fact]
    public void TryReserve_BoundedCapacity_EvictsOldestEntryWhenExceeded()
    {
        var cache = new MessageDeduplicationCache(window: TimeSpan.FromMinutes(5), maxEntriesPerScope: 2);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();

        cache.TryReserve("Hub:actor-1", first).Should().BeTrue();
        cache.TryReserve("Hub:actor-1", second).Should().BeTrue();
        cache.TryReserve("Hub:actor-1", third).Should().BeTrue("容量已满时应淘汰最早写入的条目而不是拒绝新条目");

        // 淘汰后 first 的去重记忆已经丢失：同一 MessageId 会被当成"新"消息重新放行。
        cache.TryReserve("Hub:actor-1", first).Should().BeTrue("容量淘汰边界：最早写入的条目应已被移除去重记忆");
    }

    [Fact]
    public void Constructor_WithNonPositiveMaxEntries_Throws()
    {
        var act = () => new MessageDeduplicationCache(maxEntriesPerScope: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_WithNonPositiveSweepInterval_Throws()
    {
        var act = () => new MessageDeduplicationCache(sweepInterval: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Sweep_ReclaimsEmptyExpiredScopeBuckets()
    {
        // P7 硬化：去重窗口过期后，空的 scope 桶应被回收，使桶字典规模随活跃 Actor 收敛（§15.1 风险 #9）。
        var cache = new MessageDeduplicationCache(window: TimeSpan.FromMilliseconds(20));

        cache.TryReserve("Hub:actor-1", Guid.NewGuid()).Should().BeTrue();
        cache.TryReserve("Hub:actor-2", Guid.NewGuid()).Should().BeTrue();
        cache.ScopeCount.Should().Be(2);

        Thread.Sleep(60); // 让两个桶内条目全部过期
        cache.Sweep();

        cache.ScopeCount.Should().Be(0, "过期后为空的 scope 桶应被 Sweep 回收");
    }

    [Fact]
    public void Sweep_KeepsNonEmptyScopeBuckets()
    {
        var cache = new MessageDeduplicationCache(window: TimeSpan.FromMinutes(5));
        cache.TryReserve("Hub:actor-live", Guid.NewGuid()).Should().BeTrue();

        cache.Sweep();

        cache.ScopeCount.Should().Be(1, "窗口内仍有有效记录的 scope 桶不应被回收");
    }

    [Fact]
    public void TryReserve_AutomaticSweep_ReclaimsExpiredBuckets()
    {
        // 摊销式回收：sweepInterval=1 时每次 TryReserve 都会触发一次 Sweep。
        var cache = new MessageDeduplicationCache(window: TimeSpan.FromMilliseconds(20), sweepInterval: 1);
        cache.TryReserve("Hub:stale", Guid.NewGuid()).Should().BeTrue();

        Thread.Sleep(60); // 让 stale 桶过期

        // 下一次 TryReserve 会先写入 live 桶，再触发 Sweep 回收已过期为空的 stale 桶。
        cache.TryReserve("Hub:live", Guid.NewGuid()).Should().BeTrue();

        cache.ScopeCount.Should().Be(1, "自动摊销回收应移除过期空桶，仅保留活跃桶");
    }
}

