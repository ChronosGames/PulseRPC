using System.Text.Json;
using GameServer.Contracts;
using Npgsql;

namespace GameServer.Host;

internal static class AssetStoreVerification
{
    internal static async Task VerifySessionsAsync(AssetStore store, NpgsqlDataSource source)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ct = timeout.Token;
        var sessions = new PlayerSessions(source);
        var player = "session-fence-" + Guid.NewGuid().ToString("N");
        var original = new PlayerSessions.Stamp(player, Guid.NewGuid());
        var replacement = new PlayerSessions.Stamp(player, Guid.NewGuid());
        await sessions.ReplaceAsync(original, DateTime.UtcNow.AddMinutes(5), ct);
        var fence = await store.AcquireAsync(player, Guid.NewGuid(), ct);
        var operation = new PurchaseCommand { OperationId = Guid.NewGuid() };
        await store.PurchaseAsync(fence, operation, ct, original);

        await using (var connection = await source.OpenConnectionAsync(ct))
        await using (var transaction = await connection.BeginTransactionAsync(ct))
        {
            await PlayerSessions.LockForPurchaseAsync(connection, transaction, original, player, ct);
            // PostgreSQL itself proves replacement is serialized with a transaction
            // already admitted by the old session; do not depend on timing a Task.
            await using var competing = await source.OpenConnectionAsync(ct);
            await using var competingTransaction = await competing.BeginTransactionAsync(ct);
            await using (var configure = new NpgsqlCommand("SET LOCAL lock_timeout='100ms'", competing, competingTransaction))
                await configure.ExecuteNonQueryAsync(ct);
            await using var update = new NpgsqlCommand("""
                UPDATE game_sessions SET session=$2 WHERE player=$1
                """, competing, competingTransaction);
            update.Parameters.AddWithValue(player);
            update.Parameters.AddWithValue(replacement.Session);
            try { await update.ExecuteNonQueryAsync(ct); throw new InvalidOperationException("Replacement bypassed the session transaction lock."); }
            catch (PostgresException error) when (error.SqlState == "55P03") { }
            await competingTransaction.RollbackAsync(ct);
            await transaction.CommitAsync(ct);
        }
        await sessions.ReplaceAsync(replacement, DateTime.UtcNow.AddMinutes(5), ct);
        await MustFailAsync<UnauthorizedAccessException>(() => store.PurchaseAsync(fence,
            new PurchaseCommand { OperationId = Guid.NewGuid() }, CancellationToken.None, original));
        await sessions.RevokeAsync(original, ct);
        await sessions.ValidateAsync(replacement, ct);
        var replay = await store.PurchaseAsync(fence, operation, ct, replacement);
        if (replay.Balance != 993 || replay.Inventory != 1) throw new InvalidOperationException("Session replacement duplicated a purchase.");
        await sessions.RevokeAsync(replacement, ct);
        await MustFailAsync<UnauthorizedAccessException>(() => store.PurchaseAsync(fence,
            new PurchaseCommand { OperationId = Guid.NewGuid() }, CancellationToken.None, replacement));
        await store.ReleaseAsync(fence, ct);
        Console.WriteLine("RESULT " + JsonSerializer.Serialize(new
        {
            Passed = true, DatabaseSerializesLoginWithPurchase = true,
            StaleQueuedWriterRejectedWithoutCancellation = true, StaleLogoutSafe = true, DurableReplay = true
        }));
    }

    internal static async Task HoldPlayerRowAsync(NpgsqlDataSource source, string player)
    {
        await using var connection = await source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var query = new NpgsqlCommand("SELECT player FROM game_players WHERE player=$1 FOR UPDATE", connection, transaction);
        query.Parameters.AddWithValue(player);
        if (await query.ExecuteScalarAsync() is null) throw new InvalidOperationException("Missing overload test player.");
        Console.WriteLine("LOCKED");
        await Task.Delay(TimeSpan.FromSeconds(3));
        await transaction.RollbackAsync();
    }

    internal static async Task VerifyInactiveOwnerAsync(NpgsqlDataSource source, string player)
    {
        await using var query = source.CreateCommand("SELECT owner_until <= clock_timestamp() FROM game_players WHERE player=$1");
        query.Parameters.AddWithValue(player);
        if (await query.ExecuteScalarAsync() is not true)
            throw new InvalidOperationException("The Actor continued renewing database ownership after losing its Redis lease.");
        Console.WriteLine("RESULT {\"InactiveOwner\":true}");
    }

    internal static async Task RunAsync(AssetStore store, NpgsqlDataSource source)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var ct = timeout.Token;
        var player = "store-" + Guid.NewGuid().ToString("N");
        var original = await store.AcquireAsync(player, Guid.NewGuid(), ct);
        var operation = new PurchaseCommand { OperationId = Guid.NewGuid() };
        // Competing retries are separate SQL transactions, not an in-memory lock or cache.
        var attempts = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => store.PurchaseAsync(original, operation, ct)));
        if (attempts.Any(receipt => receipt.Balance != 993 || receipt.Inventory != 1))
            throw new InvalidOperationException("Concurrent replay changed asset state.");
        await MustFailAsync<ArgumentException>(() => store.PurchaseAsync(original,
            new PurchaseCommand { OperationId = operation.OperationId, Quantity = 2 }, ct));
        await store.DrainOutboxAsync(acknowledge: false, ct);
        await store.DrainOutboxAsync(acknowledge: false, ct); // Consumer committed, producer ACK was lost.
        await store.DrainOutboxAsync(acknowledge: true, ct);

        await store.ReleaseAsync(original, ct);
        var replacement = await store.AcquireAsync(player, Guid.NewGuid(), ct);
        if (replacement.Generation <= original.Generation) throw new InvalidOperationException("Fence did not advance.");
        // Deliberately pass no cancellation token: SQL must fence an uncooperative old writer.
        await MustFailAsync<InvalidOperationException>(() => store.PurchaseAsync(original,
            new PurchaseCommand { OperationId = Guid.NewGuid() }, CancellationToken.None));
        await MustFailAsync<InvalidOperationException>(() => store.RenewAsync(original, ct));
        await store.ReleaseAsync(original, ct); // A stale release must not revoke the replacement.
        var replay = await new AssetStore(source).PurchaseAsync(replacement, operation, ct);
        if (replay.Balance != 993 || replay.Inventory != 1) throw new InvalidOperationException("Receipt did not survive owner replacement.");
        var snapshot = await store.ReadAsync(replacement, ct);
        if (snapshot.Balance != 993) throw new InvalidOperationException("Stale write changed balance.");

        await Task.Delay(TimeSpan.FromSeconds(AssetStore.LeaseSeconds) + TimeSpan.FromMilliseconds(100), ct);
        await MustFailAsync<InvalidOperationException>(() => store.RenewAsync(replacement, ct));
        await MustFailAsync<InvalidOperationException>(() => store.PurchaseAsync(replacement,
            new PurchaseCommand { OperationId = Guid.NewGuid() }, CancellationToken.None));
        var next = await store.AcquireAsync(player, Guid.NewGuid(), ct);
        if (next.Generation <= replacement.Generation) throw new InvalidOperationException("Expired generation was reused.");

        foreach (var table in new[] { "game_receipts", "game_outbox", "game_inbox" })
        {
            // Table names are fixed literals, never request data.
            await using var query = source.CreateCommand($"SELECT count(*) FROM {table} WHERE player=$1");
            query.Parameters.AddWithValue(player);
            if (await query.ExecuteScalarAsync(ct) is not long count || count != 1)
                throw new InvalidOperationException($"Expected exactly one durable record in {table}.");
        }
        await store.ReleaseAsync(next, ct);
        Console.WriteLine("RESULT " + JsonSerializer.Serialize(new
        {
            Passed = true, ConcurrentDuplicateTransactions = 20, AssetMutations = 1,
            StaleWriterRejectedWithoutCancellation = true, StaleRenewAndReleaseSafe = true,
            ExpiredFenceCannotRevive = true, DurableReceiptSurvivesReplacement = true,
            OutboxRetryDeduplicatedByInbox = true
        }));
    }

    private static async Task MustFailAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
