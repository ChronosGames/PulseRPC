using Npgsql;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Gateway;

namespace GameServer.Host;

internal sealed class PlayerSessionMode
{
    internal bool Required { get; } = Environment.GetEnvironmentVariable("GAME_SESSION_MODE") switch
    {
        null or "required" => true,
        "legacy-compatible" => false,
        _ => throw new ArgumentException("GAME_SESSION_MODE must be required or legacy-compatible.")
    };

    internal PlayerSessions.Stamp? GetStamp(IPulseContext? context)
        => Required || context?.Claims.ContainsKey(PlayerSessions.Claim) == true ? PlayerSessions.Require(context) : null;
}

internal sealed class PlayerSessions(NpgsqlDataSource source)
{
    internal const string Claim = "game_session";
    internal sealed record Stamp(string Player, Guid Session);

    internal async Task ReplaceAsync(Stamp stamp, DateTime expiry, CancellationToken ct)
    {
        await using var query = source.CreateCommand("""
            INSERT INTO game_sessions(player,session,valid_until) VALUES($1,$2,$3)
            ON CONFLICT(player) DO UPDATE SET session=EXCLUDED.session,valid_until=EXCLUDED.valid_until
            """);
        query.Parameters.AddWithValue(stamp.Player);
        query.Parameters.AddWithValue(stamp.Session);
        query.Parameters.AddWithValue(DateTime.SpecifyKind(expiry, DateTimeKind.Utc));
        await query.ExecuteNonQueryAsync(ct);
    }

    internal async Task RevokeAsync(Stamp stamp, CancellationToken ct)
    {
        await using var query = source.CreateCommand("DELETE FROM game_sessions WHERE player=$1 AND session=$2");
        query.Parameters.AddWithValue(stamp.Player);
        query.Parameters.AddWithValue(stamp.Session);
        await query.ExecuteNonQueryAsync(ct);
    }

    internal async Task ValidateAsync(Stamp stamp, CancellationToken ct)
    {
        await using var query = source.CreateCommand("""
            SELECT 1 FROM game_sessions WHERE player=$1 AND session=$2 AND valid_until > clock_timestamp()
            """);
        query.Parameters.AddWithValue(stamp.Player);
        query.Parameters.AddWithValue(stamp.Session);
        if (await query.ExecuteScalarAsync(ct) is null)
            throw new UnauthorizedAccessException("Player session was replaced, revoked or expired.");
    }

    internal static Stamp Require(IPulseContext? context)
    {
        if (context is null || context.IsExpired || string.IsNullOrWhiteSpace(context.UserId)
            || !context.Claims.TryGetValue(Claim, out var raw) || !Guid.TryParse(raw, out var session))
            throw new UnauthorizedAccessException("A current player session is required.");
        return new Stamp(context.UserId, session);
    }

    internal static async Task LockForPurchaseAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Stamp stamp, string player, CancellationToken ct)
    {
        if (!string.Equals(stamp.Player, player, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Session does not own these assets.");
        // This shared row lock lasts through the purchase commit. A replacement login
        // waits for an admitted transaction; after its ACK no old session can commit a
        // new purchase, even if the old request was already queued on a remote Actor.
        await using var query = new NpgsqlCommand("""
            SELECT 1 FROM game_sessions WHERE player=$1 AND session=$2 AND valid_until > clock_timestamp() FOR SHARE
            """, connection, transaction);
        query.Parameters.AddWithValue(stamp.Player);
        query.Parameters.AddWithValue(stamp.Session);
        if (await query.ExecuteScalarAsync(ct) is null)
            throw new UnauthorizedAccessException("Stale player session cannot mutate assets.");
    }
}

internal sealed class PlayerSessionPolicy(PlayerSessions sessions, PlayerSessionMode mode) : IGatewayActorInvocationPolicy
{
    public async ValueTask EvaluateAsync(GatewayActorInvocationContext context, CancellationToken cancellationToken = default)
    {
        if (mode.GetStamp(context.CallerContext) is { } stamp)
            await sessions.ValidateAsync(stamp, cancellationToken);
    }
}
