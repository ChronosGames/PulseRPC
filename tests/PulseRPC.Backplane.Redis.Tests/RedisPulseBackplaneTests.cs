using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PulseRPC.Backplane.Redis;
using PulseRPC.Clustering;
using PulseRPC.Routing;
using StackExchange.Redis;
using Xunit;

namespace PulseRPC.Backplane.Redis.Tests;

/// <summary>
/// 回归测试：<see cref="RedisPulseBackplane"/>（§P6）—— 用 NSubstitute 模拟 StackExchange.Redis 的
/// <see cref="IConnectionMultiplexer"/>/<see cref="ISubscriber"/>/<see cref="IDatabase"/>（均为接口，
/// 无需真实 Redis 实例即可验证发布/订阅编解码、成员目录键设计与生命周期语义是否正确）。
/// </summary>
public class RedisPulseBackplaneTests
{
    private static (RedisPulseBackplane Backplane, IConnectionMultiplexer Connection, ISubscriber Subscriber, IDatabase Database) Create(
        string keyPrefix = "pulserpc")
    {
        var connection = Substitute.For<IConnectionMultiplexer>();
        var subscriber = Substitute.For<ISubscriber>();
        var database = Substitute.For<IDatabase>();
        connection.GetSubscriber(Arg.Any<object?>()).Returns(subscriber);
        connection.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(database);

        var options = Options.Create(new RedisBackplaneOptions { KeyPrefix = keyPrefix });
        var backplane = new RedisPulseBackplane(connection, options, NullLogger<RedisPulseBackplane>.Instance);

        return (backplane, connection, subscriber, database);
    }

    [Fact]
    public async Task PublishAsync_PublishesToFanoutChannel_WithKeyPrefix()
    {
        var (backplane, _, subscriber, _) = Create(keyPrefix: "my-cluster");

        await backplane.PublishAsync(PulseAddress.AllClients("RoomHub"), 0x1234, new byte[] { 1, 2, 3 }, "node-a");

        await subscriber.Received(1).PublishAsync(
            Arg.Is<RedisChannel>(c => c.ToString() == "my-cluster:fanout"),
            Arg.Any<RedisValue>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public void Subscribe_OnlySubscribesToRedisOnce_RegardlessOfLocalSubscriberCount()
    {
        var (backplane, _, subscriber, _) = Create();

        using var sub1 = backplane.Subscribe((_, _, _, _, _) => default);
        using var sub2 = backplane.Subscribe((_, _, _, _, _) => default);

        subscriber.Received(1).Subscribe(Arg.Any<RedisChannel>(), Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task PublishThenLocalRedisCallback_MustDispatchDecodedMessageToAllLocalSubscribers()
    {
        var (backplane, _, subscriber, _) = Create();
        Action<RedisChannel, RedisValue>? redisHandler = null;
        subscriber.When(s => s.Subscribe(Arg.Any<RedisChannel>(), Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>()))
            .Do(ci => redisHandler = ci.Arg<Action<RedisChannel, RedisValue>>());
        RedisChannel? publishedChannel = null;
        RedisValue publishedPayload = default;
        subscriber.PublishAsync(Arg.Any<RedisChannel>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                publishedChannel = ci.Arg<RedisChannel>();
                publishedPayload = ci.Arg<RedisValue>();
                return Task.FromResult(1L);
            });

        (PulseAddress Address, ushort ProtocolId, byte[] Body, string OriginNodeId)? received1 = null;
        (PulseAddress Address, ushort ProtocolId, byte[] Body, string OriginNodeId)? received2 = null;
        using var sub1 = backplane.Subscribe((addr, protocolId, body, origin, _) =>
        {
            received1 = (addr, protocolId, body.ToArray(), origin);
            return default;
        });
        using var sub2 = backplane.Subscribe((addr, protocolId, body, origin, _) =>
        {
            received2 = (addr, protocolId, body.ToArray(), origin);
            return default;
        });

        await backplane.PublishAsync(PulseAddress.Group("RoomHub", "room-1"), 0x9999, new byte[] { 9, 8, 7 }, "node-origin");

        redisHandler.Should().NotBeNull();
        redisHandler!(publishedChannel!.Value, publishedPayload);

        // 由于本地回调是 fire-and-forget（DispatchSafeAsync），给一次让步窗口让其完成。
        await Task.Delay(50);

        received1.Should().NotBeNull();
        received1!.Value.Address.Kind.Should().Be(AddressKind.Group);
        received1!.Value.Address.Hub.Should().Be("RoomHub");
        received1!.Value.Address.Key.Should().Be("room-1");
        received1!.Value.ProtocolId.Should().Be((ushort)0x9999);
        received1!.Value.Body.Should().BeEquivalentTo(new byte[] { 9, 8, 7 });
        received1!.Value.OriginNodeId.Should().Be("node-origin");

        received2.Should().NotBeNull("同一条广播应派发给所有本地订阅者");
    }

    [Fact]
    public void Dispose_UnsubscribedLocalHandler_MustNotBeInvoked_OnSubsequentRedisMessage()
    {
        var (backplane, _, subscriber, _) = Create();
        Action<RedisChannel, RedisValue>? redisHandler = null;
        subscriber.When(s => s.Subscribe(Arg.Any<RedisChannel>(), Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>()))
            .Do(ci => redisHandler = ci.Arg<Action<RedisChannel, RedisValue>>());

        var invoked = false;
        var subscription = backplane.Subscribe((_, _, _, _, _) =>
        {
            invoked = true;
            return default;
        });
        subscription.Dispose();

        redisHandler.Should().NotBeNull();
        var act = () => redisHandler!(default, default);

        act.Should().NotThrow();
        invoked.Should().BeFalse();
    }

    [Fact]
    public async Task AddMemberAsync_WritesHashEntry_KeyedByKindAndKeyOnly_IgnoringHub()
    {
        var (backplane, _, _, database) = Create(keyPrefix: "cluster1");
        database.HashSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<RedisValue>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(true));
        database.KeyExpireAsync(Arg.Any<RedisKey>(), Arg.Any<TimeSpan?>(), Arg.Any<CommandFlags>()).Returns(Task.FromResult(true));

        await backplane.AddMemberAsync("conn-1", PulseAddress.Group("HubA", "room-1"), "node-a");

        await database.Received(1).HashSetAsync(
            Arg.Is<RedisKey>(k => k.ToString() == $"cluster1:members:{(byte)AddressKind.Group}:room-1"),
            "conn-1", "node-a", Arg.Any<When>(), Arg.Any<CommandFlags>());
        await database.ReceivedWithAnyArgs(1).KeyExpireAsync(default(RedisKey), default(TimeSpan?));
    }

    [Fact]
    public async Task AddMemberAsync_WithDifferentHubs_SameKindAndKey_MustUseSameRedisKey()
    {
        var (backplane, _, _, database) = Create();

        await backplane.AddMemberAsync("conn-1", PulseAddress.Group("HubA", "room-1"), "node-a");
        await backplane.AddMemberAsync("conn-2", PulseAddress.Group("HubB", "room-1"), "node-b");

        await database.Received(2).HashSetAsync(
            Arg.Is<RedisKey>(k => k.ToString() == $"pulserpc:members:{(byte)AddressKind.Group}:room-1"),
            Arg.Any<RedisValue>(), Arg.Any<RedisValue>(), Arg.Any<When>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RemoveMemberAsync_DeletesHashField()
    {
        var (backplane, _, _, database) = Create(keyPrefix: "cluster1");

        await backplane.RemoveMemberAsync("conn-1", PulseAddress.User("HubA", "alice"), "node-a");

        await database.Received(1).HashDeleteAsync(
            Arg.Is<RedisKey>(k => k.ToString() == $"cluster1:members:{(byte)AddressKind.User}:alice"),
            "conn-1", Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task ResolveAsync_WhenNoEntries_ReturnsEmpty()
    {
        var (backplane, _, _, database) = Create();
        database.HashGetAllAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(Task.FromResult(Array.Empty<HashEntry>()));

        var result = await backplane.ResolveAsync(PulseAddress.User("HubA", "alice"));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_MapsHashEntriesToBackplaneMembers_ConnectionIdAsNameNodeIdAsValue()
    {
        var (backplane, _, _, database) = Create();
        database.HashGetAllAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(Task.FromResult(new[]
        {
            new HashEntry("conn-1", "node-a"),
            new HashEntry("conn-2", "node-b"),
        }));

        var result = await backplane.ResolveAsync(PulseAddress.Group("HubA", "room-1"));

        result.Should().BeEquivalentTo(new[]
        {
            new BackplaneMember("node-a", "conn-1"),
            new BackplaneMember("node-b", "conn-2"),
        });
    }

    [Fact]
    public async Task DisposeAsync_WhenNeverSubscribed_DoesNotCallRedisUnsubscribe()
    {
        var (backplane, _, subscriber, _) = Create();

        await backplane.DisposeAsync();

        subscriber.DidNotReceiveWithAnyArgs().Unsubscribe(default, default, default);
    }

    [Fact]
    public async Task DisposeAsync_WhenSubscribed_CallsRedisUnsubscribe_AndIsIdempotent()
    {
        var (backplane, _, subscriber, _) = Create();
        using var subscription = backplane.Subscribe((_, _, _, _, _) => default);

        await backplane.DisposeAsync();
        await backplane.DisposeAsync();

        subscriber.Received(1).Unsubscribe(Arg.Any<RedisChannel>(), Arg.Any<Action<RedisChannel, RedisValue>?>(), Arg.Any<CommandFlags>());
    }
}
