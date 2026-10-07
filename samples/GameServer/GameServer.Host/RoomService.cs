using GameServer.Contracts;
using Microsoft.Extensions.Logging;
using Npgsql;
using PulseRPC;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Gateway;
using PulseRPC.Server.Services;

namespace GameServer.Host;

internal sealed class RoomStore(NpgsqlDataSource source)
{
    internal async Task<long> ReadAuthorizedAsync(string room, string player, CancellationToken ct)
    {
        await using var query = source.CreateCommand("""
            SELECT count(*) FROM game_room_memberships WHERE room=$1
            HAVING EXISTS(SELECT 1 FROM game_room_memberships WHERE room=$1 AND player=$2)
            """);
        query.Parameters.AddWithValue(room);
        query.Parameters.AddWithValue(player);
        return await query.ExecuteScalarAsync(ct) is long members ? members
            : throw new UnauthorizedAccessException("Only current room members may access this Actor.");
    }

    internal async Task SetMemberAsync(string room, string player, bool member, CancellationToken ct)
    {
        await using var query = source.CreateCommand(member
            ? "INSERT INTO game_room_memberships(room,player) VALUES($1,$2) ON CONFLICT DO NOTHING"
            : "DELETE FROM game_room_memberships WHERE room=$1 AND player=$2");
        query.Parameters.AddWithValue(room);
        query.Parameters.AddWithValue(player);
        await query.ExecuteNonQueryAsync(ct);
    }
}

internal sealed class GameResourcePolicy(RoomStore rooms) : IGatewayActorInvocationPolicy
{
    private readonly UserOwnedActorInvocationPolicy _players = new(
        new Dictionary<string, IReadOnlyCollection<ushort>> { ["PlayerHub"] = new ushort[] { 0x7101, 0x7102, 0x7103 } });

    public async ValueTask EvaluateAsync(GatewayActorInvocationContext context, CancellationToken cancellationToken = default)
    {
        if (context.Hub != "RoomHub")
        {
            await _players.EvaluateAsync(context, cancellationToken);
            return;
        }
        var caller = context.CallerContext;
        if (caller.SourceType != CallSourceType.ExternalUser || caller.IsExpired || string.IsNullOrWhiteSpace(caller.UserId)
            || context.ProtocolId is not (0x7110 or 0x7111))
            throw new UnauthorizedAccessException("Room operation is not available to this caller.");
        await rooms.ReadAuthorizedAsync(context.Key, caller.UserId, cancellationToken);
    }
}

[PulseService(DisplayName = "RoomHub", InstanceScope = ServiceInstanceScope.MultiInstance)]
public sealed class RoomService : PulseServiceBase, IRoomHub
{
    private readonly RoomStore _rooms;
    private readonly PlayerSessions _sessions;
    private readonly PlayerSessionMode _mode;
    private readonly string _node;

    internal RoomService(string room, RoomStore rooms, PlayerSessions sessions, PlayerSessionMode mode,
        string node, ILogger<RoomService> logger)
        : base("RoomHub", room, logger, new ServiceExecutionOptions
        { QueueCapacity = 64, BackpressureMode = ServiceBackpressureMode.ThrowException })
    { _rooms = rooms; _sessions = sessions; _mode = mode; _node = node; }

    public async Task<RoomSnapshot> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var caller = PulseContext.Current;
        if (caller is null || caller.IsExpired || caller.SourceType != CallSourceType.ExternalUser || string.IsNullOrWhiteSpace(caller.UserId))
            throw new UnauthorizedAccessException("A player identity is required.");
        if (_mode.GetStamp(caller) is { } stamp) await _sessions.ValidateAsync(stamp, cancellationToken);
        ActorLeaseCancellationToken.ThrowIfCancellationRequested();
        var count = await _rooms.ReadAuthorizedAsync(ServiceId, caller.UserId, cancellationToken);
        return new RoomSnapshot { Room = ServiceId, Members = count, NodeId = _node };
    }

    public async Task<string> EchoAsync(string value, CancellationToken cancellationToken = default)
    {
        await GetStateAsync(cancellationToken);
        return value;
    }
}
