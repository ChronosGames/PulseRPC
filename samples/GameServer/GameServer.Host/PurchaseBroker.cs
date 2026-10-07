using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using StackExchange.Redis;

namespace GameServer.Host;

// PostgreSQL remains the durable source until the consumer transaction commits.
// Redis acceptance alone never marks delivery complete: a lost stream is replayable.
internal sealed class PurchaseBroker(NpgsqlDataSource source, IConnectionMultiplexer redis, string stream, string? playerFilter = null)
{
    private const string Group = "purchase-notifications-v1";
    private readonly string _consumer = Guid.NewGuid().ToString("N");
    private RedisValue _claimCursor = "0-0";
    private IDatabase Database => redis.GetDatabase();

    internal async Task EnsureGroupAsync(CancellationToken ct)
    {
        try { await Database.StreamCreateConsumerGroupAsync(stream, Group, "0-0", createStream: true).WaitAsync(ct); }
        catch (RedisServerException error) when (error.Message.StartsWith("BUSYGROUP", StringComparison.Ordinal)) { }
    }

    internal async Task<int> PublishAsync(CancellationToken ct, Func<Task>? afterPublish = null)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var batch = new List<(string Player, Guid Operation, string Sku, int Quantity)>();
        await using (var query = new NpgsqlCommand("""
            SELECT o.player,o.operation,r.sku,r.quantity FROM game_outbox o
            JOIN game_receipts r USING(player,operation)
            WHERE o.delivered_at IS NULL
              AND ($1::text IS NULL OR o.player=$1)
              AND (o.last_published_at IS NULL OR o.last_published_at < clock_timestamp()-interval '2 seconds')
            ORDER BY o.created_at,o.player,o.operation LIMIT 64 FOR UPDATE OF o SKIP LOCKED
            """, connection, transaction))
        {
            query.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = (object?)playerFilter ?? DBNull.Value });
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                batch.Add((reader.GetString(0), reader.GetGuid(1), reader.GetString(2), reader.GetInt32(3)));
        }
        var published = 0;
        foreach (var item in batch)
        {
            ct.ThrowIfCancellationRequested();
            var id = await Database.ScriptEvaluateAsync("""
                if redis.call('XLEN',KEYS[1]) >= 4096 then return false end
                return redis.call('XADD',KEYS[1],'*','version','1','player',ARGV[1],
                  'operation',ARGV[2],'sku',ARGV[3],'quantity',ARGV[4])
                """, [stream], [item.Player, item.Operation.ToString("D"), item.Sku, item.Quantity]).WaitAsync(ct);
            if (id.IsNull) break; // Backpressure stays in the durable SQL outbox.
            published++;
            if (afterPublish is not null) await afterPublish();
            await using var update = new NpgsqlCommand("""
                UPDATE game_outbox SET published=true,last_published_at=clock_timestamp(),publish_attempts=publish_attempts+1
                WHERE player=$1 AND operation=$2
                """, connection, transaction);
            update.Parameters.AddWithValue(item.Player);
            update.Parameters.AddWithValue(item.Operation);
            await update.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return published;
    }

    internal async Task<int> ConsumeAsync(CancellationToken ct, Func<Task>? afterCommit = null)
    {
        await EnsureGroupAsync(ct);
        var pending = await Database.StreamAutoClaimAsync(stream, Group, _consumer, 1000, _claimCursor, 64).WaitAsync(ct);
        _claimCursor = pending.NextStartId;
        var fresh = await Database.StreamReadGroupAsync(stream, Group, _consumer, ">", 64).WaitAsync(ct);
        var entries = pending.ClaimedEntries.Concat(fresh).ToArray();
        foreach (var entry in entries)
        {
            var fields = entry.Values.ToDictionary(pair => pair.Name.ToString(), pair => pair.Value.ToString(), StringComparer.Ordinal);
            if (fields.Count != 5 || fields.GetValueOrDefault("version") != "1"
                || !fields.TryGetValue("player", out var player) || string.IsNullOrWhiteSpace(player)
                || !Guid.TryParse(fields.GetValueOrDefault("operation"), out var operation)
                || !int.TryParse(fields.GetValueOrDefault("quantity"), out var quantity)
                || !fields.TryGetValue("sku", out var sku))
                throw new InvalidOperationException("Invalid purchase event; left pending for investigation.");
            await using var connection = await source.OpenConnectionAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using (var validate = new NpgsqlCommand("""
                SELECT 1 FROM game_receipts WHERE player=$1 AND operation=$2 AND sku=$3 AND quantity=$4
                """, connection, transaction))
            {
                validate.Parameters.AddWithValue(player);
                validate.Parameters.AddWithValue(operation);
                validate.Parameters.AddWithValue(sku);
                validate.Parameters.AddWithValue(quantity);
                if (await validate.ExecuteScalarAsync(ct) is null)
                    throw new InvalidOperationException("Purchase event differs from the durable receipt.");
            }
            await using (var consume = new NpgsqlCommand("""
                WITH accepted AS (
                  INSERT INTO game_inbox(player,operation) VALUES($1,$2) ON CONFLICT DO NOTHING RETURNING player),
                notified AS (
                  INSERT INTO game_purchase_notifications(player,purchases) SELECT player,1 FROM accepted
                  ON CONFLICT(player) DO UPDATE SET purchases=game_purchase_notifications.purchases+1 RETURNING player)
                UPDATE game_outbox SET delivered_at=COALESCE(delivered_at,clock_timestamp()) WHERE player=$1 AND operation=$2
                """, connection, transaction))
            {
                consume.Parameters.AddWithValue(player);
                consume.Parameters.AddWithValue(operation);
                await consume.ExecuteNonQueryAsync(ct);
            }
            await transaction.CommitAsync(ct);
            if (afterCommit is not null) await afterCommit();
            // This dedicated stream has one group. ACK and deletion are atomic, so
            // a crash between them cannot leak acknowledged messages indefinitely.
            await Database.ScriptEvaluateAsync("""
                local n=redis.call('XACK',KEYS[1],ARGV[1],ARGV[2])
                if n>0 then redis.call('XDEL',KEYS[1],ARGV[2]) end
                return n
                """, [stream], [Group, entry.Id]).WaitAsync(ct);
        }
        return entries.Length;
    }

    internal async Task VerifyAsync(string player, CancellationToken ct)
    {
        await using var query = source.CreateCommand("""
            SELECT p.balance,p.inventory,
              (SELECT count(*) FROM game_receipts WHERE player=p.player),
              (SELECT count(*) FROM game_inbox WHERE player=p.player),
              (SELECT purchases FROM game_purchase_notifications WHERE player=p.player),
              (SELECT count(*) FROM game_outbox WHERE player=p.player AND delivered_at IS NOT NULL)
            FROM game_players p WHERE p.player=$1
            """);
        query.Parameters.AddWithValue(player);
        await using var reader = await query.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.GetInt64(0) != 993 || reader.GetInt32(1) != 1
            || reader.GetInt64(2) != 1 || reader.GetInt64(3) != 1 || reader.IsDBNull(4)
            || reader.GetInt32(4) != 1 || reader.GetInt64(5) != 1)
            throw new InvalidOperationException("Broker replay changed assets or duplicated the consumer side effect.");
        var pending = await Database.StreamPendingAsync(stream, Group).WaitAsync(ct);
        if (pending.PendingMessageCount != 0) throw new InvalidOperationException("Unacknowledged broker messages remain.");
        Console.WriteLine("RESULT " + JsonSerializer.Serialize(new
        {
            Passed = true, AssetMutations = 1, ConsumerSideEffects = 1, PendingMessages = pending.PendingMessageCount,
            DurableOutbox = true, Broker = "Redis Streams", ConsumerTransaction = "inbox + notification + delivered marker"
        }));
    }
}

internal sealed class PurchaseDeliveryWorker(PurchaseBroker broker, ILogger<PurchaseDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                attempt.CancelAfter(TimeSpan.FromSeconds(10));
                await broker.PublishAsync(attempt.Token);
                await broker.ConsumeAsync(attempt.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning(error, "Purchase delivery will retry from durable state"); }
            try { await Task.Delay(250, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
