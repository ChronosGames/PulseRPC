using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Clustering;
using PulseRPC.Routing;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// 基于 <see cref="IPulseBackplane"/> 模型 Y 成员目录的连接目录实现。
/// </summary>
/// <remarks>
/// 该实现只负责把 <see cref="IConnectionDirectory"/> 的 connection/user/group 查询转换为 backplane 成员登记与解析；
/// 单节点默认的 <see cref="InProcessBackplane"/> 返回空集合，因此不会改变现有单节点投递语义。
/// </remarks>
public sealed class BackplaneConnectionDirectory : IConnectionDirectory
{
    private readonly IPulseBackplane _backplane;

    /// <summary>创建 backplane-backed 连接目录。</summary>
    public BackplaneConnectionDirectory(IPulseBackplane backplane)
    {
        _backplane = backplane ?? throw new ArgumentNullException(nameof(backplane));
    }

    /// <inheritdoc/>
    public ValueTask RegisterConnectionAsync(
        string connectionId,
        ConnectionPlacement placement,
        CancellationToken cancellationToken = default)
        => _backplane.AddMemberAsync(
            placement.ConnectionId,
            PulseAddress.Connection(string.Empty, connectionId),
            placement.NodeId,
            cancellationToken);

    /// <inheritdoc/>
    public ValueTask RemoveConnectionAsync(
        string connectionId,
        ConnectionPlacement placement,
        CancellationToken cancellationToken = default)
        => _backplane.RemoveMemberAsync(
            placement.ConnectionId,
            PulseAddress.Connection(string.Empty, connectionId),
            placement.NodeId,
            cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<ConnectionPlacement?> FindConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        var members = await _backplane.ResolveAsync(PulseAddress.Connection(string.Empty, connectionId), cancellationToken)
            .ConfigureAwait(false);
        var member = members.FirstOrDefault();
        return string.IsNullOrEmpty(member.NodeId)
            ? null
            : new ConnectionPlacement(member.NodeId, member.ConnectionId);
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<ConnectionPlacement>> FindUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var members = await _backplane.ResolveAsync(PulseAddress.User(string.Empty, userId), cancellationToken)
            .ConfigureAwait(false);
        return ToPlacements(members);
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<ConnectionPlacement>> FindMembersAsync(
        PulseAddress membership,
        CancellationToken cancellationToken = default)
    {
        var members = await _backplane.ResolveAsync(membership, cancellationToken).ConfigureAwait(false);
        return ToPlacements(members);
    }

    private static IReadOnlyList<ConnectionPlacement> ToPlacements(IReadOnlyList<BackplaneMember> members)
    {
        if (members.Count == 0)
        {
            return Array.Empty<ConnectionPlacement>();
        }

        var placements = new ConnectionPlacement[members.Count];
        for (var i = 0; i < members.Count; i++)
        {
            placements[i] = new ConnectionPlacement(members[i].NodeId, members[i].ConnectionId);
        }

        return placements;
    }
}
