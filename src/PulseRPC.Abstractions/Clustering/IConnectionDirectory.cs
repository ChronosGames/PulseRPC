using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Routing;

namespace PulseRPC.Clustering;

/// <summary>
/// 客户端连接在集群中的放置信息。
/// </summary>
/// <remarks>
/// 对真实客户端连接，<see cref="NodeId"/> 是持有 socket/channel 的节点；对 Gateway 虚拟连接，
/// <see cref="NodeId"/> 是网关节点，<see cref="ConnectionId"/> 是网关上的真实连接标识。
/// </remarks>
public readonly struct ConnectionPlacement : IEquatable<ConnectionPlacement>
{
    /// <summary>持有该连接的节点标识。</summary>
    public string NodeId { get; }

    /// <summary>节点内连接标识。</summary>
    public string ConnectionId { get; }

    /// <summary>创建连接放置信息。</summary>
    public ConnectionPlacement(string nodeId, string connectionId)
    {
        NodeId = nodeId ?? string.Empty;
        ConnectionId = connectionId ?? string.Empty;
    }

    /// <inheritdoc/>
    public bool Equals(ConnectionPlacement other)
        => string.Equals(NodeId, other.NodeId, StringComparison.Ordinal)
           && string.Equals(ConnectionId, other.ConnectionId, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ConnectionPlacement other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(
            NodeId is null ? 0 : StringComparer.Ordinal.GetHashCode(NodeId),
            ConnectionId is null ? 0 : StringComparer.Ordinal.GetHashCode(ConnectionId));

    /// <inheritdoc/>
    public override string ToString() => $"ConnectionPlacement(Node='{NodeId}', Connection='{ConnectionId}')";
}

/// <summary>
/// 连接目录 —— 解析 connection/user/group fan-out 在集群中的节点归属。
/// </summary>
/// <remarks>
/// <para>
/// 该目录与 <see cref="IActorDirectory"/> 分离：Actor identity 通常长生命周期、低频迁移；客户端连接、用户多端登录与
/// 分组成员关系生命周期短、变更频繁，且 Gateway 虚拟连接需要保留真实连接所在节点。
/// </para>
/// <para>
/// 单节点实现可以只返回本地连接；集群实现可由 Gateway、Backplane 或外部存储维护成员关系。
/// </para>
/// </remarks>
public interface IConnectionDirectory
{
    /// <summary>
    /// 注册一个连接查找键到具体连接放置的映射。
    /// </summary>
    /// <remarks>
    /// <paramref name="connectionId"/> 是全局查找键；<paramref name="placement"/> 中的 <see cref="ConnectionPlacement.ConnectionId"/>
    /// 是 owner 节点内的真实连接 Id。Gateway 虚拟连接可用组合后的虚拟 Id 作为查找键，同时把真实客户端连接 Id 放入
    /// <see cref="ConnectionPlacement.ConnectionId"/>。
    /// </remarks>
    ValueTask RegisterConnectionAsync(
        string connectionId,
        ConnectionPlacement placement,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除一个连接查找键到具体连接放置的映射。
    /// </summary>
    ValueTask RemoveConnectionAsync(
        string connectionId,
        ConnectionPlacement placement,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找单个连接所在节点；未找到返回 <c>null</c>。
    /// </summary>
    ValueTask<ConnectionPlacement?> FindConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找某用户的所有连接放置。
    /// </summary>
    ValueTask<IReadOnlyList<ConnectionPlacement>> FindUserAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找某 fan-out 地址在集群中的成员连接。
    /// </summary>
    /// <remarks>
    /// <paramref name="membership"/> 通常是 <see cref="AddressKind.Group"/>、<see cref="AddressKind.User"/>、
    /// <see cref="AddressKind.AllClients"/> 或 <see cref="AddressKind.Except"/> 地址；实现可按需要忽略 Hub 或 Key。
    /// </remarks>
    ValueTask<IReadOnlyList<ConnectionPlacement>> FindMembersAsync(
        PulseAddress membership,
        CancellationToken cancellationToken = default);
}
