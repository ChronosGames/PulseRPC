using GameServer.Contracts;
using Npgsql;

namespace GameServer.Host;

// The database issues and checks its own monotonically increasing fencing generation.
// Redis placement and cooperative cancellation do not authorize database writes.
internal sealed class AssetStore(NpgsqlDataSource source)
{
    internal sealed record Fence(string Player, Guid Owner, long Generation);
    internal const int LeaseSeconds = 6;

    internal async Task PrepareLoadAsync(string prefix, int players, long initialBalance, CancellationToken ct)
    {
        await using var query = source.CreateCommand("""
            INSERT INTO game_players(player,balance,owner,generation,owner_until)
            SELECT $1 || n::text,$3,$4,0,'epoch'::timestamptz FROM generate_series(0,$2-1) n
            """);
        query.Parameters.AddWithValue(prefix);
        query.Parameters.AddWithValue(players);
        query.Parameters.AddWithValue(initialBalance);
        query.Parameters.AddWithValue(Guid.NewGuid());
        await query.ExecuteNonQueryAsync(ct);
    }

    internal async Task VerifyLoadAsync(string prefix, int players, long initialBalance, CancellationToken ct)
    {
        await using var query = source.CreateCommand("""
            SELECT count(*),count(*) FILTER(WHERE p.balance=$2-COALESCE(r.units,0)*7
                AND p.inventory=COALESCE(r.units,0) AND COALESCE(r.receipts,0)=COALESCE(o.events,0))
            FROM game_players p
            LEFT JOIN (SELECT player,sum(quantity) units,count(*) receipts FROM game_receipts
                WHERE starts_with(player,$1) GROUP BY player) r USING(player)
            LEFT JOIN (SELECT player,count(*) events FROM game_outbox
                WHERE starts_with(player,$1) GROUP BY player) o USING(player)
            WHERE starts_with(p.player,$1)
            """);
        query.Parameters.AddWithValue(prefix);
        query.Parameters.AddWithValue(initialBalance);
        await using var reader = await query.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.GetInt64(0) != players || reader.GetInt64(1) != players)
            throw new InvalidOperationException("Asset balances, inventory, receipts and outbox differ after load.");
    }

    internal async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var command = source.CreateCommand("""
            CREATE TABLE IF NOT EXISTS game_players (
              player text PRIMARY KEY, balance bigint NOT NULL DEFAULT 1000 CHECK(balance >= 0),
              inventory integer NOT NULL DEFAULT 0, owner uuid NOT NULL,
              generation bigint NOT NULL, owner_until timestamptz NOT NULL);
            CREATE TABLE IF NOT EXISTS game_receipts (
              player text NOT NULL, operation uuid NOT NULL, sku text NOT NULL, quantity integer NOT NULL,
              balance bigint NOT NULL, inventory integer NOT NULL, PRIMARY KEY(player, operation));
            CREATE TABLE IF NOT EXISTS game_outbox (
              player text NOT NULL, operation uuid NOT NULL, published boolean NOT NULL DEFAULT false,
              PRIMARY KEY(player, operation));
            CREATE TABLE IF NOT EXISTS game_inbox (
              player text NOT NULL, operation uuid NOT NULL, PRIMARY KEY(player, operation));
            CREATE TABLE IF NOT EXISTS game_purchase_notifications (
              player text PRIMARY KEY, purchases integer NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS game_sessions (
              player text PRIMARY KEY, session uuid NOT NULL, valid_until timestamptz NOT NULL);
            ALTER TABLE game_receipts ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT clock_timestamp();
            ALTER TABLE game_outbox ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT clock_timestamp();
            ALTER TABLE game_outbox ADD COLUMN IF NOT EXISTS last_published_at timestamptz;
            ALTER TABLE game_outbox ADD COLUMN IF NOT EXISTS delivered_at timestamptz;
            ALTER TABLE game_outbox ADD COLUMN IF NOT EXISTS publish_attempts integer NOT NULL DEFAULT 0;
            CREATE INDEX IF NOT EXISTS game_outbox_pending ON game_outbox(created_at) WHERE delivered_at IS NULL;
            """);
        await command.ExecuteNonQueryAsync(ct);
    }

    internal async Task<Fence> AcquireAsync(string player, Guid owner, CancellationToken ct)
    {
        await using var command = source.CreateCommand("""
            INSERT INTO game_players(player, owner, generation, owner_until)
            VALUES ($1, $2, 1, clock_timestamp() + make_interval(secs => $3))
            ON CONFLICT(player) DO UPDATE SET owner = EXCLUDED.owner,
              generation = game_players.generation + 1, owner_until = EXCLUDED.owner_until
            WHERE game_players.owner_until <= clock_timestamp()
            RETURNING generation
            """);
        command.Parameters.AddWithValue(player);
        command.Parameters.AddWithValue(owner);
        command.Parameters.AddWithValue(LeaseSeconds);
        var result = await command.ExecuteScalarAsync(ct);
        if (result is not long generation) throw new InvalidOperationException("Database ownership is still held by another activation.");
        return new Fence(player, owner, generation);
    }

    internal async Task RenewAsync(Fence fence, CancellationToken ct)
    {
        await using var command = source.CreateCommand("""
            UPDATE game_players SET owner_until = clock_timestamp() + make_interval(secs => $4)
            WHERE player=$1 AND owner=$2 AND generation=$3 AND owner_until > clock_timestamp()
            """);
        AddFence(command, fence);
        command.Parameters.AddWithValue(LeaseSeconds);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Database fence has expired or changed.");
    }

    internal async Task ReleaseAsync(Fence fence, CancellationToken ct)
    {
        await using var command = source.CreateCommand("""
            UPDATE game_players SET owner_until=clock_timestamp()
            WHERE player=$1 AND owner=$2 AND generation=$3
            """);
        AddFence(command, fence);
        await command.ExecuteNonQueryAsync(ct);
    }

    internal async Task<PurchaseReceipt> PurchaseAsync(Fence fence, PurchaseCommand request, CancellationToken ct,
        PlayerSessions.Stamp? session = null)
    {
        if (request.OperationId == Guid.Empty || request.Sku != "potion" || request.Quantity < 1 || request.Quantity > 100)
            throw new ArgumentException("A purchase needs an operation ID, a supported SKU and quantity 1..100.");
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (session is not null)
            await PlayerSessions.LockForPurchaseAsync(connection, transaction, session, fence.Player, ct);
        // Serialize both mutation and deduplication on the authoritative player row.
        await using (var guard = new NpgsqlCommand("""
            SELECT generation FROM game_players WHERE player=$1 AND owner=$2 AND generation=$3
            AND owner_until > clock_timestamp() FOR UPDATE
            """, connection, transaction))
        {
            AddFence(guard, fence);
            if (await guard.ExecuteScalarAsync(ct) is null) throw new InvalidOperationException("Stale database writer rejected.");
        }

        await using (var existing = new NpgsqlCommand("""
            SELECT sku, quantity, balance, inventory FROM game_receipts WHERE player=$1 AND operation=$2
            """, connection, transaction))
        {
            existing.Parameters.AddWithValue(fence.Player);
            existing.Parameters.AddWithValue(request.OperationId);
            await using var reader = await existing.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                if (reader.GetString(0) != request.Sku || reader.GetInt32(1) != request.Quantity)
                    throw new ArgumentException("An operation ID cannot be reused for a different purchase.");
                var receipt = new PurchaseReceipt { OperationId = request.OperationId, Balance = reader.GetInt64(2), Inventory = reader.GetInt32(3) };
                await reader.DisposeAsync();
                await transaction.CommitAsync(ct);
                return receipt;
            }
        }

        PurchaseReceipt result;
        await using (var update = new NpgsqlCommand("""
            UPDATE game_players SET balance=balance-$4, inventory=inventory+$5
            WHERE player=$1 AND owner=$2 AND generation=$3 AND owner_until > clock_timestamp() AND balance >= $4
            RETURNING balance, inventory
            """, connection, transaction))
        {
            AddFence(update, fence);
            update.Parameters.AddWithValue(checked(request.Quantity * 7L)); // Server-owned price.
            update.Parameters.AddWithValue(request.Quantity);
            await using var reader = await update.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Insufficient balance or stale database fence.");
            result = new PurchaseReceipt { OperationId = request.OperationId, Balance = reader.GetInt64(0), Inventory = reader.GetInt32(1) };
        }
        await using (var insert = new NpgsqlCommand("""
            WITH receipt AS (
              INSERT INTO game_receipts(player,operation,sku,quantity,balance,inventory)
              VALUES ($1,$2,$3,$4,$5,$6) RETURNING player,operation)
            INSERT INTO game_outbox(player,operation) SELECT player,operation FROM receipt
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue(fence.Player);
            insert.Parameters.AddWithValue(request.OperationId);
            insert.Parameters.AddWithValue(request.Sku);
            insert.Parameters.AddWithValue(request.Quantity);
            insert.Parameters.AddWithValue(result.Balance);
            insert.Parameters.AddWithValue(result.Inventory);
            await insert.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return result;
    }

    internal async Task<PlayerSnapshot> ReadAsync(Fence fence, CancellationToken ct)
    {
        await using var command = source.CreateCommand("""
            SELECT balance,inventory FROM game_players WHERE player=$1 AND owner=$2 AND generation=$3
              AND owner_until > clock_timestamp()
            """);
        AddFence(command, fence);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Stale database reader rejected.");
        return new PlayerSnapshot { Balance = reader.GetInt64(0), Inventory = reader.GetInt32(1), Fence = fence.Generation };
    }

    // Two transactions deliberately model a crash between consumer commit and producer ACK.
    // In a deployed system the consumer transaction contains its real notification side effect.
    internal async Task DrainOutboxAsync(bool acknowledge, CancellationToken ct)
    {
        await using var read = source.CreateCommand("SELECT player,operation FROM game_outbox WHERE NOT published ORDER BY player,operation LIMIT 128");
        var batch = new List<(string Player, Guid Operation)>();
        await using (var reader = await read.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) batch.Add((reader.GetString(0), reader.GetGuid(1)));
        foreach (var item in batch)
        {
            await using var consume = source.CreateCommand("INSERT INTO game_inbox(player,operation) VALUES($1,$2) ON CONFLICT DO NOTHING");
            consume.Parameters.AddWithValue(item.Player);
            consume.Parameters.AddWithValue(item.Operation);
            await consume.ExecuteNonQueryAsync(ct);
            if (!acknowledge) continue;
            await using var ack = source.CreateCommand("UPDATE game_outbox SET published=true WHERE player=$1 AND operation=$2");
            ack.Parameters.AddWithValue(item.Player);
            ack.Parameters.AddWithValue(item.Operation);
            await ack.ExecuteNonQueryAsync(ct);
        }
    }

    private static void AddFence(NpgsqlCommand command, Fence fence)
    {
        command.Parameters.AddWithValue(fence.Player);
        command.Parameters.AddWithValue(fence.Owner);
        command.Parameters.AddWithValue(fence.Generation);
    }
}
