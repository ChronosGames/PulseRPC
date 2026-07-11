using FluentAssertions;
using Microsoft.Extensions.Options;
using PulseRPC.Backplane.Redis;
using StackExchange.Redis;
using Xunit;

namespace PulseRPC.Backplane.Redis.Tests;

internal sealed class RedisIntegrationFactAttribute : FactAttribute
{
    internal const string ConnectionStringEnvironmentVariable = "PULSERPC_TEST_REDIS";

    public RedisIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)))
        {
            Skip = $"设置 {ConnectionStringEnvironmentVariable} 后才运行真实 Redis 集成测试。";
        }
    }
}

public sealed class RedisActorLeaseStoreIntegrationTests
{
    [RedisIntegrationFact]
    public async Task RealRedis_EnforcesCasRenewReleaseAndTtlTakeover()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            RedisIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!;
        using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var prefix = $"pulserpc-tests:{Guid.NewGuid():N}";
        var options = Options.Create(new RedisActorLeaseStoreOptions { KeyPrefix = prefix });
        var storeA = new RedisActorLeaseStore(connection, options);
        var storeB = new RedisActorLeaseStore(connection, options);

        var contenders = await Task.WhenAll(
            Enumerable.Range(0, 64)
                .Select(index => (index % 2 == 0 ? storeA : storeB)
                    .ActivateAsync(
                        "RoomHub",
                        "room-race",
                        $"node-{index}",
                        TimeSpan.FromSeconds(5))
                    .AsTask()));

        contenders.Select(item => item.NodeId).Distinct(StringComparer.Ordinal).Should().ContainSingle();
        contenders.Select(item => item.LeaseId).Distinct(StringComparer.Ordinal).Should().ContainSingle();
        var winner = contenders[0];

        (await storeB.RenewAsync(
            "RoomHub", "room-race", "wrong-node", winner.LeaseId, TimeSpan.FromSeconds(5))).Should().BeFalse();
        await storeB.ReleaseAsync("RoomHub", "room-race", winner.NodeId, "wrong-lease");
        var stillOwned = (await storeA.ResolveAsync("RoomHub", "room-race"))!.Value;
        stillOwned.NodeId.Should().Be(winner.NodeId);
        stillOwned.LeaseId.Should().Be(winner.LeaseId);

        (await storeB.RenewAsync(
            "RoomHub", "room-race", winner.NodeId, winner.LeaseId, TimeSpan.FromSeconds(5))).Should().BeTrue();
        await storeA.ReleaseAsync("RoomHub", "room-race", winner.NodeId, winner.LeaseId);
        (await storeB.ResolveAsync("RoomHub", "room-race")).Should().BeNull();

        var expiring = await storeA.ActivateAsync(
            "RoomHub", "room-expiring", "node-old", TimeSpan.FromMilliseconds(150));
        await Task.Delay(350);
        var replacement = await storeB.ActivateAsync(
            "RoomHub", "room-expiring", "node-new", TimeSpan.FromSeconds(2));

        replacement.NodeId.Should().Be("node-new");
        replacement.LeaseId.Should().NotBe(expiring.LeaseId);
        await storeB.ReleaseAsync("RoomHub", "room-expiring", replacement.NodeId, replacement.LeaseId);
    }
}
