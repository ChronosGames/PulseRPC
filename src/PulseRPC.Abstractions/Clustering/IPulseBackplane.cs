using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Routing;

namespace PulseRPC.Clustering;

/// <summary>
/// 一个跨节点成员条目：某连接位于哪个节点。
/// </summary>
public readonly struct BackplaneMember : IEquatable<BackplaneMember>
{
    /// <summary>成员所在节点标识。</summary>
    public string NodeId { get; }

    /// <summary>成员连接标识。</summary>
    public string ConnectionId { get; }

    /// <summary>创建成员条目。</summary>
    public BackplaneMember(string nodeId, string connectionId)
    {
        NodeId = nodeId ?? string.Empty;
        ConnectionId = connectionId ?? string.Empty;
    }

    /// <inheritdoc/>
    public bool Equals(BackplaneMember other)
        => string.Equals(NodeId, other.NodeId, StringComparison.Ordinal)
           && string.Equals(ConnectionId, other.ConnectionId, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is BackplaneMember other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(
            NodeId is null ? 0 : StringComparer.Ordinal.GetHashCode(NodeId),
            ConnectionId is null ? 0 : StringComparer.Ordinal.GetHashCode(ConnectionId));
}

/// <summary>
/// 收到一次模型 X 广播时的回调签名。
/// </summary>
/// <param name="fanoutAddress">原始 Fan-out 寻址（<c>AllClients/Group/User/Except</c>）。</param>
/// <param name="protocolId">方法协议号。</param>
/// <param name="body">已序列化的消息体（不含头部，与 <see cref="IPulseBackplane.PublishAsync"/> 收到的一致）。</param>
/// <param name="originNodeId">发布该广播的节点标识；订阅方应据此判断"是否是自己发布的"以避免重复本地投递。</param>
/// <param name="cancellationToken">取消令牌。</param>
public delegate ValueTask BackplaneMessageHandler(
    PulseAddress fanoutAddress,
    ushort protocolId,
    ReadOnlyMemory<byte> body,
    string originNodeId,
    CancellationToken cancellationToken);

/// <summary>
/// 跨节点广播 / 分组 / 用户映射的扩散后端（对标 SignalR 的 Redis backplane）。
/// </summary>
/// <remarks>
/// <para>
/// 修复"进程内 GroupManager / UserConnectionMapping / ServerChannelManager 只覆盖本节点连接"导致的
/// 多节点/网关下 Fan-out 静默丢消息问题。抽象层<strong>同时暴露两种扩散能力</strong>：
/// </para>
/// <list type="bullet">
/// <item><description>模型 X（publish/subscribe）：<see cref="PublishAsync"/> + <see cref="Subscribe"/> —— 大范围广播（All / 大组 / Except），各节点订阅后对本地成员投递；</description></item>
/// <item><description>模型 Y（全局成员目录）：<see cref="AddMemberAsync"/> / <see cref="RemoveMemberAsync"/> / <see cref="ResolveAsync"/> —— 定向（User / Single / 小组）先解析节点再定投。</description></item>
/// </list>
/// <para>
/// <strong>成员归属键约定</strong>：<see cref="AddMemberAsync"/>/<see cref="RemoveMemberAsync"/>/<see cref="ResolveAsync"/>
/// 仅按 <c>(PulseAddress.Kind, PulseAddress.Key)</c> 匹配成员归属，<strong>忽略 <c>Hub</c> 字段</strong>——
/// 组/用户成员关系在本框架中是 Hub 无关的（<c>IGroupManager</c>/<c>IUserConnectionMapping</c> 本身不区分 Hub，
/// Hub 仅用于出站信封构造），实现方与调用方均不得依赖 Hub 参与成员匹配。
/// </para>
/// <para>
/// 默认实现 <see cref="InProcessBackplane"/> 为单节点无扩散（等价现状、零依赖）；集群实现（Redis/NATS 等）作为独立包提供。
/// </para>
/// </remarks>
public interface IPulseBackplane
{
    /// <summary>
    /// 模型 X：把一次 Fan-out 意图扩散到其它节点（各节点收到后经 <see cref="Subscribe"/> 注册的回调对本地成员投递）。
    /// </summary>
    /// <param name="fanoutAddress">Fan-out 寻址。</param>
    /// <param name="protocolId">方法协议号。</param>
    /// <param name="body">已序列化的消息体。</param>
    /// <param name="originNodeId">本节点标识；订阅方用它判断是否为自己发布（从而跳过重复本地投递）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask PublishAsync(
        PulseAddress fanoutAddress,
        ushort protocolId,
        ReadOnlyMemory<byte> body,
        string originNodeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 模型 X：订阅其它节点（含自己）经 <see cref="PublishAsync"/> 发布的广播。
    /// 同一后端实例可注册多个订阅者；处理器抛出的异常不应影响其它订阅者或发布方。
    /// </summary>
    /// <param name="handler">收到广播时调用的回调。</param>
    /// <returns>释放后取消订阅的句柄。</returns>
    IDisposable Subscribe(BackplaneMessageHandler handler);

    /// <summary>
    /// 模型 Y：登记某连接对某成员集合（组/用户）的归属，供跨节点定向解析。
    /// </summary>
    ValueTask AddMemberAsync(
        string connectionId,
        PulseAddress membership,
        string ownerNodeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 模型 Y：移除某连接对某成员集合的归属（连接断开 / 离开组时调用）。
    /// </summary>
    ValueTask RemoveMemberAsync(
        string connectionId,
        PulseAddress membership,
        string ownerNodeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 模型 Y：解析某寻址目标（组/用户/单连接）对应的全集群成员，供发送节点定向投递。
    /// 单节点实现返回空集合（表示"无其它节点成员"）。
    /// </summary>
    ValueTask<IReadOnlyList<BackplaneMember>> ResolveAsync(
        PulseAddress address,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 单节点默认 Backplane —— 无跨节点扩散（等价于现状），零依赖。
/// </summary>
/// <remarks>
/// 本实现下所有 Fan-out 只由本节点的 <c>GroupManager</c> / <c>ServerChannelManager</c> 处理本地成员；
/// <see cref="PublishAsync"/> 为 no-op，<see cref="ResolveAsync"/> 返回空集合。集群部署需替换为分布式实现。
/// </remarks>
public sealed class InProcessBackplane : IPulseBackplane
{
    private static readonly IReadOnlyList<BackplaneMember> EmptyMembers = Array.Empty<BackplaneMember>();

    private sealed class NoopSubscription : IDisposable
    {
        public static readonly NoopSubscription Instance = new();

        public void Dispose()
        {
            // 单节点无扩散：Subscribe 不持有任何资源，Dispose 无需操作。
        }
    }

    /// <inheritdoc/>
    public ValueTask PublishAsync(PulseAddress fanoutAddress, ushort protocolId, ReadOnlyMemory<byte> body, string originNodeId, CancellationToken cancellationToken = default)
        => default;

    /// <inheritdoc/>
    public IDisposable Subscribe(BackplaneMessageHandler handler)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        return NoopSubscription.Instance;
    }

    /// <inheritdoc/>
    public ValueTask AddMemberAsync(string connectionId, PulseAddress membership, string ownerNodeId, CancellationToken cancellationToken = default)
        => default;

    /// <inheritdoc/>
    public ValueTask RemoveMemberAsync(string connectionId, PulseAddress membership, string ownerNodeId, CancellationToken cancellationToken = default)
        => default;

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<BackplaneMember>> ResolveAsync(PulseAddress address, CancellationToken cancellationToken = default)
        => new(EmptyMembers);
}
