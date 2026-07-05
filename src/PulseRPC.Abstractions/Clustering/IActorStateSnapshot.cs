using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Clustering;

/// <summary>
/// keyed Actor 的可迁移状态契约（L3 opt-in）—— 实现本接口的 Actor 支持在节点间<strong>状态迁移</strong>：
/// 迁出节点捕获其状态快照，迁入节点恢复该快照，从而实现"跨激活状态保留"（超越 L2"跨激活状态丢失"的语义）。
/// </summary>
/// <remarks>
/// <para>
/// 捕获/恢复由 <c>ActorMigrationCoordinator</c> 在 Actor 已被<strong>静默（quiesce）</strong>——即其邮箱在途消息
/// 已排空、不再接受新消息——之后调用，因此实现无需自行处理并发：捕获时状态是稳定的。
/// </para>
/// <para>
/// 序列化格式由实现自行决定（MemoryPack/JSON/自定义均可）；框架只负责把不透明的 <c>byte[]</c> 快照从
/// 迁出节点搬运到迁入节点。未实现本接口的 Actor 仍可迁移，但迁入后状态为空（等价 L2 重新激活）。
/// </para>
/// </remarks>
public interface IActorStateSnapshot
{
    /// <summary>捕获当前 Actor 状态为不透明快照（在 Actor 已静默后调用）。</summary>
    ValueTask<byte[]> CaptureStateAsync(CancellationToken cancellationToken = default);

    /// <summary>从快照恢复 Actor 状态（在迁入节点激活后、开始处理消息前调用）。</summary>
    ValueTask RestoreStateAsync(byte[] state, CancellationToken cancellationToken = default);
}

/// <summary>
/// Actor 状态快照的跨节点搬运通道（L3）—— 把迁出节点捕获的快照发送到迁入节点并触发其恢复+激活。
/// </summary>
/// <remarks>
/// 生产实现基于 <see cref="INodeLink"/>（新增一个集群内部 Hub 方法用于接收快照）；本抽象使迁移协调器与
/// 具体传输解耦，便于单元测试与将来替换。
/// </remarks>
public interface IActorStateTransport
{
    /// <summary>
    /// 把 <paramref name="snapshot"/> 发送给 <paramref name="targetNodeId"/>，由其恢复 <c>(hub, key)</c> 实例并激活。
    /// </summary>
    ValueTask SendSnapshotAsync(
        string targetNodeId,
        string hub,
        string key,
        byte[] snapshot,
        CancellationToken cancellationToken = default);
}
