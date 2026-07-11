using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PulseRPC.Clustering;
using PulseRPC.Routing;
using PulseRPC.Server.Clustering;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Routing;
using PulseRPC.Server.Services;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

/// <summary>
/// P7 故障接管：<see cref="ClusterPulseRouter"/> 在跨节点 Actor 调用失败时上报节点健康、把故障节点
/// 移出存活集并把 <c>(Hub, Key)</c> 重新映射到存活节点后重试（属主接管）。
/// </summary>
public class ClusterPulseRouterFailoverTests
{
    private const string Local = "node-local";
    private const string A = "node-a";
    private const string B = "node-b";

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }

    private static LocalPulseRouter CreateLocal()
        => new(
            new ServerChannelManager(NullLogger<ServerChannelManager>.Instance),
            new GroupManager(),
            new UserConnectionMapping(),
            EmptyServiceProvider.Instance,
            NullLogger<LocalPulseRouter>.Instance);

    private static StaticClusterMembership CreateMembership(int failureThreshold)
    {
        var topology = new ClusterTopologyOptions
        {
            LocalNodeId = Local,
            Members =
            {
                new ClusterNodeEndpoint { NodeId = Local, Host = "127.0.0.1", Port = 5000 },
                new ClusterNodeEndpoint { NodeId = A, Host = "127.0.0.1", Port = 5001 },
                new ClusterNodeEndpoint { NodeId = B, Host = "127.0.0.1", Port = 5002 },
            },
        };
        return new StaticClusterMembership(
            Options.Create(topology),
            Options.Create(new StaticClusterMembershipOptions
            {
                FailureThreshold = failureThreshold,
                QuarantineDuration = TimeSpan.FromMinutes(10), // 测试期间不触发自动半开恢复
            }));
    }

    /// <summary>找到一个在全成员环中属主为 <paramref name="fullOwner"/>、在去掉 A 后的环中属主为 <paramref name="reducedOwner"/> 的 Key。</summary>
    private static string FindKeyForFailover(string fullOwner, string reducedOwner)
    {
        var full = new NodeConsistentHashRing(new[] { Local, A, B });
        var reduced = new NodeConsistentHashRing(new[] { Local, B });
        for (var i = 0; i < 100_000; i++)
        {
            var key = $"actor-{i}";
            if (full.GetOwner(HashPlacementStrategy.BuildIdentity("RoomHub", key)) == fullOwner
                && reduced.GetOwner(HashPlacementStrategy.BuildIdentity("RoomHub", key)) == reducedOwner)
            {
                return key;
            }
        }

        throw new InvalidOperationException("未能构造出满足故障接管路径的 Key，测试环境哈希分布异常。");
    }

    private static ClusterPulseRouter CreateRouter(StaticClusterMembership membership, INodeLink nodeLink)
    {
        var ring = new NodeConsistentHashRing(new[] { Local, A, B });
        var actorDirectory = new LeaseActorDirectory(
            Options.Create(new LeaseActorDirectoryOptions
            {
                LeaseDuration = TimeSpan.FromMilliseconds(40),
            }));
        var backplane = Substitute.For<IPulseBackplane>();
        backplane.Subscribe(Arg.Any<BackplaneMessageHandler>()).Returns(Substitute.For<IDisposable>());
        var topology = Options.Create(new ClusterTopologyOptions { LocalNodeId = Local });

        return new ClusterPulseRouter(
            CreateLocal(), ring, actorDirectory, nodeLink, backplane, topology,
            NullLogger<ClusterPulseRouter>.Instance,
            new DeliveryRetryOptions { MaxAttempts = 1 },
            membership);
    }

    [Fact]
    public async Task SendAsync_WhenOwnerNodeFails_MustFailoverToSurvivingRemoteOwner()
    {
        using var membership = CreateMembership(failureThreshold: 1);
        var nodeLink = Substitute.For<INodeLink>();

        // A 不可达：抛异常；B 正常。
        nodeLink
            .SendActorAsync(A, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ushort>(), Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<Guid>(), Arg.Any<string>())
            .Returns(_ => throw new TimeoutException("node-a unreachable"));

        var router = CreateRouter(membership, nodeLink);
        var key = FindKeyForFailover(fullOwner: A, reducedOwner: B);
        var body = MemoryPackSerializer.Serialize("failover-payload");

        await router.SendAsync(PulseAddress.Actor("RoomHub", key), 0x1234, body, DeliveryMode.AtLeastOnce);

        // 首次尝试打到故障节点 A（失败），接管后重试到存活节点 B（成功）。
        await nodeLink.Received().SendActorAsync(A, "RoomHub", key, 0x1234, Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<Guid>(), Arg.Any<string>());
        await nodeLink.Received(1).SendActorAsync(B, "RoomHub", key, 0x1234, Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<Guid>(), Arg.Any<string>());

        membership.LiveNodeIds.Should().NotContain(A);
        membership.LiveNodeIds.Should().Contain(B);
    }

    [Fact]
    public async Task SendAsync_FailoverRetries_MustCarrySameMessageIdForExactlyOnceDedup()
    {
        using var membership = CreateMembership(failureThreshold: 1);
        var nodeLink = Substitute.For<INodeLink>();

        var capturedMessageIds = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        nodeLink
            .SendActorAsync(A, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ushort>(), Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<Guid>(), Arg.Any<string>())
            .Returns(ci => { capturedMessageIds.Add(ci.ArgAt<Guid>(8)); throw new TimeoutException("node-a unreachable"); });
        nodeLink
            .SendActorAsync(B, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ushort>(), Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<Guid>(), Arg.Any<string>())
            .Returns(ci => { capturedMessageIds.Add(ci.ArgAt<Guid>(8)); return ValueTask.CompletedTask; });

        var router = CreateRouter(membership, nodeLink);
        var key = FindKeyForFailover(fullOwner: A, reducedOwner: B);

        await router.SendAsync(PulseAddress.Actor("RoomHub", key), 0x1234, MemoryPackSerializer.Serialize("x"), DeliveryMode.ExactlyOnce);

        // 故障接管的两次尝试（A 失败 + B 成功）必须携带同一个非空 messageId，使远端 Actor 侧去重生效。
        capturedMessageIds.Should().HaveCountGreaterThanOrEqualTo(2);
        capturedMessageIds.Distinct().Should().HaveCount(1);
        capturedMessageIds.Should().NotContain(Guid.Empty);
    }

    [Fact]
    public async Task SendAsync_WithExplicitNodeId_MustNotFailover()
    {
        using var membership = CreateMembership(failureThreshold: 1);
        var nodeLink = Substitute.For<INodeLink>();
        nodeLink
            .SendActorAsync(A, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ushort>(), Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<Guid>(), Arg.Any<string>())
            .Returns(_ => throw new TimeoutException("node-a unreachable"));

        var router = CreateRouter(membership, nodeLink);
        var body = MemoryPackSerializer.Serialize("explicit");

        // 显式指定目标节点 A：即使 A 失败也不做属主接管，原样抛出。
        var act = async () => await router.SendAsync(PulseAddress.Actor("RoomHub", "any-key", A), 0x1234, body, DeliveryMode.AtLeastOnce);

        await act.Should().ThrowAsync<TimeoutException>();
        await nodeLink.DidNotReceive().SendActorAsync(B, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ushort>(), Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task AskAsync_WhenRemoteBusinessCallFails_MustNotQuarantineOrFailover()
    {
        using var membership = CreateMembership(failureThreshold: 1);
        var nodeLink = Substitute.For<INodeLink>();
        nodeLink
            .AskActorAsync(A, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ushort>(), Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<string>())
            .Returns(ValueTask.FromException<ReadOnlyMemory<byte>>(
                new InvalidOperationException("remote business rejection")));

        var router = CreateRouter(membership, nodeLink);
        var key = FindKeyForFailover(fullOwner: A, reducedOwner: B);

        var act = async () => await router.AskAsync(
            PulseAddress.Actor("RoomHub", key),
            0x1234,
            MemoryPackSerializer.Serialize("business-error"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("remote business rejection");
        membership.LiveNodeIds.Should().Contain(A);
        await nodeLink.DidNotReceive().AskActorAsync(
            B,
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<ushort>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<string>());
    }
}
