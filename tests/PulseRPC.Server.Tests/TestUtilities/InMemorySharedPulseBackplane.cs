using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Clustering;
using PulseRPC.Routing;

namespace PulseRPC.Server.Tests.TestUtilities;

/// <summary>
/// 进程内共享总线，供多个 <see cref="InMemorySharedPulseBackplane"/>「节点」实例模拟一个真实集群的
/// <see cref="IPulseBackplane"/> 扩散行为（同一 <see cref="InMemoryBackplaneBus"/> 实例 = 同一集群）。
/// </summary>
/// <remarks>
/// 这是仅用于测试的 <strong>参考实现/测试替身</strong>，用于在单进程单元测试内验证设计文档 §9.3
/// 广播语义矩阵（跨节点成员不漏、不重复），并不是生产可用的分布式后端（真实 Redis/NATS 后端属于
/// P6 后续独立包）。
/// </remarks>
public sealed class InMemoryBackplaneBus
{
    private readonly ConcurrentDictionary<Guid, BackplaneMessageHandler> _subscribers = new();

    // (Kind, Key) -> connectionId -> ownerNodeId：模型 Y 的全局成员目录，Hub 字段按接口约定被忽略。
    internal readonly ConcurrentDictionary<(AddressKind Kind, string Key), ConcurrentDictionary<string, string>> Members = new();

    internal IDisposable Subscribe(BackplaneMessageHandler handler)
    {
        var id = Guid.NewGuid();
        _subscribers[id] = handler ?? throw new ArgumentNullException(nameof(handler));
        return new Subscription(this, id);
    }

    internal async ValueTask PublishAsync(
        PulseAddress fanoutAddress, ushort protocolId, ReadOnlyMemory<byte> body, string originNodeId, CancellationToken cancellationToken)
    {
        foreach (var handler in _subscribers.Values.ToArray())
        {
            try
            {
                await handler(fanoutAddress, protocolId, body, originNodeId, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // 单个订阅者（节点）处理异常不应影响其它节点或发布方，与真实 pub/sub 后端的容错语义一致。
            }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly InMemoryBackplaneBus _bus;
        private readonly Guid _id;

        public Subscription(InMemoryBackplaneBus bus, Guid id)
        {
            _bus = bus;
            _id = id;
        }

        public void Dispose() => _bus._subscribers.TryRemove(_id, out _);
    }
}

/// <summary>
/// <see cref="IPulseBackplane"/> 的进程内共享总线实现 —— 每个实例代表"集群中的一个节点"，
/// 多个实例共享同一个 <see cref="InMemoryBackplaneBus"/> 即可在单进程内模拟多节点集群。
/// </summary>
public sealed class InMemorySharedPulseBackplane : IPulseBackplane
{
    private readonly InMemoryBackplaneBus _bus;

    public InMemorySharedPulseBackplane(InMemoryBackplaneBus bus)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    }

    /// <inheritdoc/>
    public ValueTask PublishAsync(PulseAddress fanoutAddress, ushort protocolId, ReadOnlyMemory<byte> body, string originNodeId, CancellationToken cancellationToken = default)
        => _bus.PublishAsync(fanoutAddress, protocolId, body, originNodeId, cancellationToken);

    /// <inheritdoc/>
    public IDisposable Subscribe(BackplaneMessageHandler handler) => _bus.Subscribe(handler);

    /// <inheritdoc/>
    public ValueTask AddMemberAsync(string connectionId, PulseAddress membership, string ownerNodeId, CancellationToken cancellationToken = default)
    {
        var bucket = _bus.Members.GetOrAdd((membership.Kind, membership.Key), _ => new ConcurrentDictionary<string, string>());
        bucket[connectionId] = ownerNodeId;
        return default;
    }

    /// <inheritdoc/>
    public ValueTask RemoveMemberAsync(string connectionId, PulseAddress membership, string ownerNodeId, CancellationToken cancellationToken = default)
    {
        if (_bus.Members.TryGetValue((membership.Kind, membership.Key), out var bucket))
        {
            bucket.TryRemove(connectionId, out _);
        }

        return default;
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<BackplaneMember>> ResolveAsync(PulseAddress address, CancellationToken cancellationToken = default)
    {
        if (!_bus.Members.TryGetValue((address.Kind, address.Key), out var bucket) || bucket.IsEmpty)
        {
            return new ValueTask<IReadOnlyList<BackplaneMember>>(Array.Empty<BackplaneMember>());
        }

        IReadOnlyList<BackplaneMember> result = bucket
            .Select(kvp => new BackplaneMember(kvp.Value, kvp.Key))
            .ToArray();
        return new ValueTask<IReadOnlyList<BackplaneMember>>(result);
    }
}
