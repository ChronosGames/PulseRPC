using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PulseRPC.Clustering;
using PulseRPC.Routing;
using PulseRPC.Server.Clustering;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Routing;
using PulseRPC.Server.Services;
using PulseRPC.Server.Services.Management;
using PulseRPC.Server.Transport;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

public sealed class ActorLeaseActivationLifecycleTests
{
    private const string LocalNodeId = "node-local";

    [Fact]
    public async Task FailedLocalActivation_ReleasesLeaseAndDoesNotStartHeartbeat()
    {
        var service = new TestActor("actor-1", failOnStart: true);
        await using var manager = CreateManager(service);
        var heartbeat = Substitute.For<IActorLeaseHeartbeat>();
        var directory = Substitute.For<IActorDirectory>();
        var placement = new ActorPlacement(
            LocalNodeId,
            "lease-1",
            DateTime.UtcNow.AddMinutes(1).Ticks);
        directory.ActivateAsync(
                nameof(TestActor),
                "actor-1",
                LocalNodeId,
                Arg.Any<CancellationToken>())
            .Returns(placement);
        using var router = CreateRouter(manager, directory, heartbeat);

        var action = async () => await router.SendAsync(
            PulseAddress.Actor(nameof(TestActor), "actor-1"),
            0x1234,
            ReadOnlyMemory<byte>.Empty);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("start failed");
        await directory.Received(1).ReleaseAsync(
            nameof(TestActor),
            "actor-1",
            LocalNodeId,
            "lease-1",
            Arg.Any<CancellationToken>());
        heartbeat.DidNotReceive().Track(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<ActorPlacement>());
        manager.GetService(nameof(TestActor), "actor-1").Should().BeNull();
    }

    [Fact]
    public async Task SuccessfulLocalActivation_StartsHeartbeatAndKeepsLease()
    {
        var service = new TestActor("actor-1", failOnStart: false);
        await using var manager = CreateManager(service);
        var heartbeat = Substitute.For<IActorLeaseHeartbeat>();
        var directory = Substitute.For<IActorDirectory>();
        var placement = new ActorPlacement(
            LocalNodeId,
            "lease-1",
            DateTime.UtcNow.AddMinutes(1).Ticks);
        directory.ActivateAsync(
                nameof(TestActor),
                "actor-1",
                LocalNodeId,
                Arg.Any<CancellationToken>())
            .Returns(placement);
        using var router = CreateRouter(manager, directory, heartbeat);

        await router.SendAsync(
            PulseAddress.Actor(nameof(TestActor), "actor-1"),
            0x1234,
            ReadOnlyMemory<byte>.Empty);

        heartbeat.Received(1).Track(nameof(TestActor), "actor-1", placement);
        await directory.DidNotReceive().ReleaseAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        manager.GetService(nameof(TestActor), "actor-1").Should().BeSameAs(service);
    }

    [Fact]
    public async Task SuccessfulLocalActivation_StartsHeartbeatBeforeFirstInvocationCompletes()
    {
        var service = new TestActor("actor-1", failOnStart: false);
        await using var manager = CreateManager(service);
        var heartbeat = Substitute.For<IActorLeaseHeartbeat>();
        var directory = Substitute.For<IActorDirectory>();
        var placement = new ActorPlacement(
            LocalNodeId,
            "lease-1",
            DateTime.UtcNow.AddMinutes(1).Ticks);
        directory.ActivateAsync(
                nameof(TestActor),
                "actor-1",
                LocalNodeId,
                Arg.Any<CancellationToken>())
            .Returns(placement);
        var activationCompleted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var finishInvocation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var routingTable = new ManagerRoutingTable(
            manager,
            activationCompleted,
            finishInvocation.Task);
        using var router = CreateRouter(manager, directory, heartbeat, routingTable);

        var send = router.SendAsync(
            PulseAddress.Actor(nameof(TestActor), "actor-1"),
            0x1234,
            ReadOnlyMemory<byte>.Empty).AsTask();

        await activationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        heartbeat.Received(1).Track(nameof(TestActor), "actor-1", placement);
        send.IsCompleted.Should().BeFalse("业务方法仍在执行时租约已经需要续租");

        finishInvocation.TrySetResult(true);
        await send.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellation_SharedActivationStillCompletesLeaseLifecycle(bool failOnStart)
    {
        var service = new TestActor("actor-1", failOnStart, blockStart: true);
        await using var manager = CreateManager(service);
        var heartbeat = Substitute.For<IActorLeaseHeartbeat>();
        var tracked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        heartbeat.When(value => value.Track(
                nameof(TestActor),
                "actor-1",
                Arg.Any<ActorPlacement>()))
            .Do(_ => tracked.TrySetResult(true));
        var directory = Substitute.For<IActorDirectory>();
        var released = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        directory.When(value => value.ReleaseAsync(
                nameof(TestActor),
                "actor-1",
                LocalNodeId,
                "lease-1",
                Arg.Any<CancellationToken>()))
            .Do(_ => released.TrySetResult(true));
        var placement = new ActorPlacement(
            LocalNodeId,
            "lease-1",
            DateTime.UtcNow.AddMinutes(1).Ticks);
        directory.ActivateAsync(
                nameof(TestActor),
                "actor-1",
                LocalNodeId,
                Arg.Any<CancellationToken>())
            .Returns(placement);
        using var router = CreateRouter(manager, directory, heartbeat);
        using var callerCancellation = new CancellationTokenSource();

        var send = router.SendAsync(
            PulseAddress.Actor(nameof(TestActor), "actor-1"),
            0x1234,
            ReadOnlyMemory<byte>.Empty,
            cancellationToken: callerCancellation.Token).AsTask();
        await service.StartEntered.WaitAsync(TimeSpan.FromSeconds(5));

        callerCancellation.Cancel();
        await FluentActions.Awaiting(() => send)
            .Should().ThrowAsync<OperationCanceledException>();

        service.ReleaseStart();
        if (failOnStart)
        {
            await released.Task.WaitAsync(TimeSpan.FromSeconds(5));
            heartbeat.DidNotReceive().Track(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ActorPlacement>());
        }
        else
        {
            await tracked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await directory.DidNotReceive().ReleaseAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());
        }
    }

    private static PulseServiceManager CreateManager(TestActor service)
    {
        var manager = new PulseServiceManager(
            EmptyServiceProvider.Instance,
            NullLogger<PulseServiceManager>.Instance);
        manager.Register<TestActor>((_, _) => service);
        return manager;
    }

    private static ClusterPulseRouter CreateRouter(
        PulseServiceManager manager,
        IActorDirectory directory,
        IActorLeaseHeartbeat heartbeat,
        IServiceRoutingTable? routingTable = null)
    {
        routingTable ??= new ManagerRoutingTable(manager);
        var local = new LocalPulseRouter(
            new ServerChannelManager(NullLogger<ServerChannelManager>.Instance),
            new GroupManager(),
            new UserConnectionMapping(),
            EmptyServiceProvider.Instance,
            NullLogger<LocalPulseRouter>.Instance,
            routingTable);
        var nodeLink = Substitute.For<INodeLink>();
        var backplane = Substitute.For<IPulseBackplane>();
        backplane.Subscribe(Arg.Any<BackplaneMessageHandler>())
            .Returns(Substitute.For<IDisposable>());

        return new ClusterPulseRouter(
            local,
            new NodeConsistentHashRing(new[] { LocalNodeId }),
            directory,
            nodeLink,
            backplane,
            Options.Create(new ClusterTopologyOptions { LocalNodeId = LocalNodeId }),
            NullLogger<ClusterPulseRouter>.Instance,
            leaseHeartbeat: heartbeat);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }

    private sealed class ManagerRoutingTable : IServiceRoutingTable
    {
        private readonly PulseServiceManager _manager;
        private readonly TaskCompletionSource<bool>? _activationCompleted;
        private readonly Task? _finishInvocation;

        public ManagerRoutingTable(
            PulseServiceManager manager,
            TaskCompletionSource<bool>? activationCompleted = null,
            Task? finishInvocation = null)
        {
            _manager = manager;
            _activationCompleted = activationCompleted;
            _finishInvocation = finishInvocation;
        }

        public bool IsProtocolIdValid(string hub, ushort protocolId) => true;

        public ReadOnlySpan<ushort> EnumerateProtocolIds() => new ushort[] { 0x1234 };

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            ushort protocolId,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            string hub,
            ushort protocolId,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            ushort protocolId,
            string serviceKey,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => RouteAsync(serviceKey, cancellationToken);

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            string hub,
            ushort protocolId,
            string serviceKey,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => RouteAsync(serviceKey, cancellationToken);

        private async ValueTask<object?> RouteAsync(
            string serviceKey,
            CancellationToken cancellationToken)
        {
            await _manager.GetOrCreateServiceAsync(
                nameof(TestActor),
                serviceKey,
                cancellationToken);
            _activationCompleted?.TrySetResult(true);
            if (_finishInvocation is not null)
            {
                await _finishInvocation.WaitAsync(cancellationToken);
            }
            return null;
        }
    }

    [PulseService(
        StartupType = ServiceStartupType.OnDemand,
        InstanceScope = ServiceInstanceScope.MultiInstance)]
    private sealed class TestActor : PulseServiceBase
    {
        private readonly bool _failOnStart;
        private readonly TaskCompletionSource<bool> _startEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _startGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TestActor(string serviceId, bool failOnStart, bool blockStart = false)
            : base(nameof(TestActor), serviceId, executionOptions: ServiceExecutionOptions.StatelessIO)
        {
            _failOnStart = failOnStart;
            if (!blockStart)
            {
                _startGate.TrySetResult(true);
            }
        }

        public Task StartEntered => _startEntered.Task;

        public void ReleaseStart() => _startGate.TrySetResult(true);

        public override async Task OnStartingAsync(CancellationToken cancellationToken = default)
        {
            _startEntered.TrySetResult(true);
            await _startGate.Task.WaitAsync(cancellationToken);
            if (_failOnStart)
            {
                throw new InvalidOperationException("start failed");
            }
        }
    }
}
