using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using PulseRPC.Backplane.Redis;
using PulseRPC.Clustering;
using StackExchange.Redis;
using Xunit;

namespace PulseRPC.Backplane.Redis.Tests;

public sealed class RedisActorLeaseStoreTests
{
    private static RedisResult PlacementResult(string nodeId, string leaseId, long ttlMilliseconds)
        => RedisResult.Create(new RedisValue[] { nodeId, leaseId, ttlMilliseconds });

    private static RedisResult EmptyResult()
        => RedisResult.Create(Array.Empty<RedisValue>());

    private static RedisResult IntegerResult(long value)
        => RedisResult.Create((RedisValue)value);

    private static (RedisActorLeaseStore Store, IConnectionMultiplexer Connection, IDatabase Database) Create(
        RedisResult result,
        string keyPrefix = "pulserpc",
        int databaseNumber = -1)
    {
        var connection = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        connection.GetDatabase(databaseNumber, Arg.Any<object?>()).Returns(database);
        database.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(result));

        var store = new RedisActorLeaseStore(
            connection,
            Options.Create(new RedisActorLeaseStoreOptions
            {
                KeyPrefix = keyPrefix,
                Database = databaseNumber,
            }));
        return (store, connection, database);
    }

    [Fact]
    public async Task ResolveAsync_ParsesOwnerLeaseAndRedisTtl()
    {
        var (store, _, _) = Create(PlacementResult("node-a", "lease-a", 2_000));
        var before = DateTime.UtcNow;

        var placement = await store.ResolveAsync("RoomHub", "room-1");

        placement.Should().NotBeNull();
        placement!.Value.NodeId.Should().Be("node-a");
        placement.Value.LeaseId.Should().Be("lease-a");
        placement.Value.ExpiresAtUtcTicks.Should().BeGreaterThan(before.AddMilliseconds(1_800).Ticks);
        placement.Value.ExpiresAtUtcTicks.Should().BeLessThanOrEqualTo(DateTime.UtcNow.AddMilliseconds(2_100).Ticks);
    }

    [Fact]
    public async Task ResolveAsync_EmptyScriptResult_ReturnsNull()
    {
        var (store, _, _) = Create(EmptyResult());

        var placement = await store.ResolveAsync("RoomHub", "room-1");

        placement.Should().BeNull();
    }

    [Fact]
    public async Task ActivateAsync_WhenRedisReportsExistingLease_ReturnsExistingOwner()
    {
        var (store, _, database) = Create(PlacementResult("node-a", "lease-winner", 5_000));
        RedisValue[]? arguments = null;
        database.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns(call =>
            {
                arguments = call.ArgAt<RedisValue[]>(2);
                return Task.FromResult(PlacementResult("node-a", "lease-winner", 5_000));
            });

        var placement = await store.ActivateAsync(
            "RoomHub",
            "room-1",
            "node-b",
            TimeSpan.FromSeconds(5));

        placement.NodeId.Should().Be("node-a");
        placement.LeaseId.Should().Be("lease-winner");
        arguments.Should().NotBeNull();
        ((string?)arguments![0]).Should().Be("node-b");
        ((string?)arguments[1]).Should().NotBeNullOrWhiteSpace("每次候选激活必须使用新的 opaque lease id");
        ((long)arguments[2]).Should().Be(5_000);
    }

    [Fact]
    public async Task ActivateAsync_ConcurrentScriptResults_KeepSingleRedisWinner()
    {
        var (store, _, _) = Create(PlacementResult("node-7", "lease-winner", 10_000));

        var attempts = await Task.WhenAll(
            Enumerable.Range(0, 64)
                .Select(index => store.ActivateAsync(
                    "RoomHub",
                    "room-race",
                    $"node-{index}",
                    TimeSpan.FromSeconds(10)).AsTask()));

        attempts.Select(item => item.NodeId).Should().OnlyContain(nodeId => nodeId == "node-7");
        attempts.Select(item => item.LeaseId).Should().OnlyContain(leaseId => leaseId == "lease-winner");
    }

    [Fact]
    public async Task RenewAsync_ParsesAtomicCompareAndExpireResult()
    {
        var (store, _, database) = Create(IntegerResult(1));
        database.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns(
                Task.FromResult(IntegerResult(1)),
                Task.FromResult(IntegerResult(0)));

        var renewed = await store.RenewAsync(
            "RoomHub", "room-1", "node-a", "lease-a", TimeSpan.FromMilliseconds(1_500));
        var rejected = await store.RenewAsync(
            "RoomHub", "room-1", "node-b", "lease-wrong", TimeSpan.FromMilliseconds(1_500));

        renewed.Should().BeTrue();
        rejected.Should().BeFalse();
    }

    [Fact]
    public async Task ReleaseAsync_UsesCompareAndDeleteScriptResult()
    {
        var (store, _, database) = Create(IntegerResult(0));
        string? script = null;
        RedisValue[]? arguments = null;
        database.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns(call =>
            {
                script = call.ArgAt<string>(0);
                arguments = call.ArgAt<RedisValue[]>(2);
                return Task.FromResult(IntegerResult(0));
            });

        await store.ReleaseAsync("RoomHub", "room-1", "node-a", "wrong-lease");

        script.Should().Contain("lease[1] == ARGV[1]")
            .And.Contain("lease[2] == ARGV[2]")
            .And.Contain("redis.call('DEL', KEYS[1])");
        arguments.Should().Equal((RedisValue)"node-a", (RedisValue)"wrong-lease");
    }

    [Fact]
    public async Task Scripts_UseSingleKeyAndAtomicTtlOperations()
    {
        var (store, _, database) = Create(PlacementResult("node-a", "lease-a", 1_000));
        string? script = null;
        RedisKey[]? keys = null;
        database.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns(call =>
            {
                script = call.ArgAt<string>(0);
                keys = call.ArgAt<RedisKey[]>(1);
                return Task.FromResult(PlacementResult("node-a", "lease-a", 1_000));
            });

        await store.ActivateAsync("RoomHub", "room-1", "node-a", TimeSpan.FromSeconds(1));

        keys.Should().ContainSingle();
        script.Should().Contain("redis.call('HMGET', KEYS[1]")
            .And.Contain("redis.call('PTTL', KEYS[1])")
            .And.Contain("redis.call('HSET', KEYS[1]")
            .And.Contain("redis.call('PEXPIRE', KEYS[1]");
    }

    [Fact]
    public async Task RedisKeys_EncodeHubAndActorKeyWithoutDelimiterCollisions()
    {
        var (store, _, database) = Create(EmptyResult(), keyPrefix: "cluster-a");
        var capturedKeys = new ConcurrentQueue<string>();
        database.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns(call =>
            {
                capturedKeys.Enqueue(call.ArgAt<RedisKey[]>(1)[0].ToString());
                return Task.FromResult(EmptyResult());
            });

        await store.ResolveAsync("a:b", "c");
        await store.ResolveAsync("a", "b:c");
        await store.ResolveAsync("房间:Hub", "玩家/一");

        capturedKeys.Should().HaveCount(3).And.OnlyHaveUniqueItems();
        capturedKeys.Should().OnlyContain(key => key.StartsWith("cluster-a:actor-leases:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PositiveSubMillisecondLease_RoundsUpToOneMillisecond()
    {
        var (store, _, database) = Create(PlacementResult("node-a", "lease-a", 1));
        RedisValue[]? arguments = null;
        database.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns(call =>
            {
                arguments = call.ArgAt<RedisValue[]>(2);
                return Task.FromResult(PlacementResult("node-a", "lease-a", 1));
            });

        await store.ActivateAsync("RoomHub", "room-1", "node-a", TimeSpan.FromTicks(1));

        ((long)arguments![2]).Should().Be(1);
    }

    [Fact]
    public async Task InvalidArguments_AreRejectedBeforeCallingRedis()
    {
        var (store, _, database) = Create(EmptyResult());

        var emptyHub = async () => await store.ResolveAsync(" ", "key");
        var emptyKey = async () => await store.ResolveAsync("Hub", string.Empty);
        var emptyNode = async () => await store.ActivateAsync("Hub", "key", " ", TimeSpan.FromSeconds(1));
        var invalidTtl = async () => await store.ActivateAsync("Hub", "key", "node-a", TimeSpan.Zero);
        var emptyLease = async () => await store.RenewAsync("Hub", "key", "node-a", "", TimeSpan.FromSeconds(1));

        await emptyHub.Should().ThrowAsync<ArgumentException>();
        await emptyKey.Should().ThrowAsync<ArgumentException>();
        await emptyNode.Should().ThrowAsync<ArgumentException>();
        await invalidTtl.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await emptyLease.Should().ThrowAsync<ArgumentException>();
        database.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task PreCanceledOperation_DoesNotCallRedis()
    {
        var (store, _, database) = Create(EmptyResult());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var operation = async () => await store.ResolveAsync("Hub", "key", cts.Token);

        await operation.Should().ThrowAsync<OperationCanceledException>();
        database.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Constructor_ValidatesOptions()
    {
        var connection = Substitute.For<IConnectionMultiplexer>();

        var blankPrefix = () => new RedisActorLeaseStore(
            connection,
            Options.Create(new RedisActorLeaseStoreOptions { KeyPrefix = " " }));
        var invalidDatabase = () => new RedisActorLeaseStore(
            connection,
            Options.Create(new RedisActorLeaseStoreOptions { Database = -2 }));

        blankPrefix.Should().Throw<ArgumentException>();
        invalidDatabase.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AddRedisActorLeases_ReplacesExistingStoreAndUsesSharedConnection()
    {
        var services = new ServiceCollection();
        var connection = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        connection.GetDatabase(3, Arg.Any<object?>()).Returns(database);
        services.AddSingleton(connection);
        services.AddSingleton(Substitute.For<IActorLeaseStore>());

        services.AddRedisActorLeases(options =>
        {
            options.KeyPrefix = "cluster-di";
            options.Database = 3;
        });

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IActorLeaseStore>().Should().ContainSingle()
            .Which.Should().BeOfType<RedisActorLeaseStore>();
        connection.Received(1).GetDatabase(3, Arg.Any<object?>());
    }
}
