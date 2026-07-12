using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using PulseRPC.Server.Services;
using PulseRPC.Server.Services.Management;
using Xunit;

namespace PulseRPC.Server.Tests.Services;

public sealed class PulseServiceManagerLifecycleTests
{
    [Fact]
    public async Task OnDemandStartFailure_DoesNotPublishFaultedInstance()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        await using var manager = new PulseServiceManager(
            provider,
            NullLogger<PulseServiceManager>.Instance);
        var service = new ControlledService("actor-1", failOnStart: true);
        manager.Register<ControlledService>((_, _) => service);

        var action = async () => await manager.GetOrCreateServiceAsync(
            nameof(ControlledService),
            "actor-1");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("start failed");
        manager.GetService(nameof(ControlledService), "actor-1").Should().BeNull();
        manager.GetStatistics().ActiveInstances.Should().Be(0);
        service.DisposeCount.Should().Be(1);
        service.MessageProcessingTask.Should().NotBeNull();
        service.MessageProcessingTask!.IsCompleted.Should().BeTrue(
            "disposing a faulted Actor must stop the mailbox worker started before OnStartingAsync failed");
    }

    [Fact]
    public async Task CallerCancellation_DoesNotCancelSharedActivation()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        await using var manager = new PulseServiceManager(
            provider,
            NullLogger<PulseServiceManager>.Instance);
        var service = new ControlledService("actor-1");
        manager.Register<ControlledService>((_, _) => service);
        using var firstCaller = new CancellationTokenSource();

        var first = manager.GetOrCreateServiceAsync(
            nameof(ControlledService),
            "actor-1",
            firstCaller.Token).AsTask();
        await service.StartEntered.WaitAsync(TimeSpan.FromSeconds(5));

        manager.GetService(nameof(ControlledService), "actor-1").Should().BeNull(
            "an Actor must not be visible before OnStartingAsync succeeds");

        var second = manager.GetOrCreateServiceAsync(
            nameof(ControlledService),
            "actor-1").AsTask();
        firstCaller.Cancel();

        await FluentActions.Awaiting(() => first)
            .Should().ThrowAsync<OperationCanceledException>();

        service.ReleaseStart();
        var activated = await second.WaitAsync(TimeSpan.FromSeconds(5));

        activated.Should().BeSameAs(service);
        service.State.Should().Be(ServiceLifecycleState.Running);
        manager.GetService(nameof(ControlledService), "actor-1").Should().BeSameAs(service);
    }

    [Fact]
    public async Task DisposeManager_MustObservePendingActivationBeforeItCanPublish()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var manager = new PulseServiceManager(
            provider,
            NullLogger<PulseServiceManager>.Instance);
        var factoryEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseFactory = new ManualResetEventSlim();
        var service = new PendingAutoStartService();
        manager.Register<PendingAutoStartService>((_, _) =>
        {
            factoryEntered.TrySetResult(true);
            releaseFactory.Wait();
            return service;
        });

        var activation = Task.Run(async () => await manager.GetOrCreateServiceAsync(
            nameof(PendingAutoStartService),
            "default"));
        await factoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disposal = manager.DisposeAsync().AsTask();
        disposal.IsCompleted.Should().BeFalse(
            "Dispose must join a pending Lazy even before its factory has produced the activation task");

        releaseFactory.Set();
        await FluentActions.Awaiting(() => activation)
            .Should().ThrowAsync<ObjectDisposedException>();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));

        manager.GetService(nameof(PendingAutoStartService), "default").Should().BeNull();
        manager.GetStatistics().ActiveInstances.Should().Be(0);
        service.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task RemoveService_WaitsForPendingActivationAndRemovesPublishedInstance()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        await using var manager = new PulseServiceManager(
            provider,
            NullLogger<PulseServiceManager>.Instance);
        var service = new ControlledService("actor-1");
        manager.Register<ControlledService>((_, _) => service);

        var activation = manager.GetOrCreateServiceAsync(
            nameof(ControlledService),
            "actor-1").AsTask();
        await service.StartEntered.WaitAsync(TimeSpan.FromSeconds(5));

        var removal = manager.RemoveServiceAsync(
            nameof(ControlledService),
            "actor-1").AsTask();
        removal.IsCompleted.Should().BeFalse();

        service.ReleaseStart();
        await activation.WaitAsync(TimeSpan.FromSeconds(5));
        (await removal.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();

        manager.GetService(nameof(ControlledService), "actor-1").Should().BeNull();
        service.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateDuringRemoval_WaitsUntilOldInstanceIsDisposed()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        await using var manager = new PulseServiceManager(
            provider,
            NullLogger<PulseServiceManager>.Instance);
        var first = new ControlledService("actor-1", blockOnStop: true);
        first.ReleaseStart();
        var second = new ControlledService("actor-1");
        second.ReleaseStart();
        var factoryCalls = 0;
        manager.Register<ControlledService>((_, _) =>
            Interlocked.Increment(ref factoryCalls) == 1 ? first : second);
        await manager.GetOrCreateServiceAsync(nameof(ControlledService), "actor-1");

        var removal = manager.RemoveServiceAsync(
            nameof(ControlledService),
            "actor-1").AsTask();
        await first.StopEntered.WaitAsync(TimeSpan.FromSeconds(5));

        var replacement = manager.GetOrCreateServiceAsync(
            nameof(ControlledService),
            "actor-1").AsTask();
        replacement.IsCompleted.Should().BeFalse();
        Volatile.Read(ref factoryCalls).Should().Be(1);

        first.ReleaseStop();
        (await removal.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        (await replacement.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeSameAs(second);
        first.DisposeCount.Should().Be(1);
        Volatile.Read(ref factoryCalls).Should().Be(2);
    }

    [Fact]
    public async Task RemoveService_ReleasesTrackedActorLease()
    {
        var leaseLifetime = new RecordingActorLeaseLifetime();
        var services = new ServiceCollection();
        services.AddSingleton<IServiceInstanceLeaseLifetime>(leaseLifetime);
        await using var provider = services.BuildServiceProvider();
        await using var manager = new PulseServiceManager(
            provider,
            NullLogger<PulseServiceManager>.Instance);
        var service = new ControlledService("actor-1");
        service.ReleaseStart();
        manager.Register<ControlledService>((_, _) => service);
        await manager.GetOrCreateServiceAsync(nameof(ControlledService), "actor-1");

        var removed = await manager.RemoveServiceAsync(nameof(ControlledService), "actor-1");

        removed.Should().BeTrue();
        leaseLifetime.ReleaseCount.Should().Be(1);
        leaseLifetime.LastHub.Should().Be(nameof(ControlledService));
        leaseLifetime.LastKey.Should().Be("actor-1");
    }

    [Fact]
    public async Task DisposeManager_ReleasesLeasesForRemainingInstances()
    {
        var leaseLifetime = new RecordingActorLeaseLifetime();
        var services = new ServiceCollection();
        services.AddSingleton<IServiceInstanceLeaseLifetime>(leaseLifetime);
        await using var provider = services.BuildServiceProvider();
        var manager = new PulseServiceManager(
            provider,
            NullLogger<PulseServiceManager>.Instance);
        var service = new ControlledService("actor:with-colon");
        service.ReleaseStart();
        manager.Register<ControlledService>((_, _) => service);
        await manager.GetOrCreateServiceAsync(nameof(ControlledService), "actor:with-colon");

        await manager.DisposeAsync();

        leaseLifetime.ReleaseCount.Should().Be(1);
        leaseLifetime.LastHub.Should().Be(nameof(ControlledService));
        leaseLifetime.LastKey.Should().Be("actor:with-colon");
    }

    [Fact]
    public async Task RemoveService_WhenStopFails_StillDisposesBeforeReleasingLease()
    {
        var leaseLifetime = new RecordingActorLeaseLifetime();
        var services = new ServiceCollection();
        services.AddSingleton<IServiceInstanceLeaseLifetime>(leaseLifetime);
        await using var provider = services.BuildServiceProvider();
        await using var manager = new PulseServiceManager(
            provider,
            NullLogger<PulseServiceManager>.Instance);
        var service = new ControlledService("actor-1", failOnStop: true);
        service.ReleaseStart();
        manager.Register<ControlledService>((_, _) => service);
        await manager.GetOrCreateServiceAsync(nameof(ControlledService), "actor-1");

        var removed = await manager.RemoveServiceAsync(nameof(ControlledService), "actor-1");

        removed.Should().BeFalse("停止钩子失败仍应向调用方报告清理异常");
        service.DisposeCount.Should().Be(1, "Stop 失败不能跳过最终 Dispose");
        leaseLifetime.ReleaseCount.Should().Be(1, "实例完成 Dispose 后才可释放 fencing lease");
    }

    [Fact]
    public async Task RemoveService_WhenDisposeFails_RetainsOwnershipUntilRetrySucceeds()
    {
        var leaseLifetime = new RecordingActorLeaseLifetime();
        var services = new ServiceCollection();
        services.AddSingleton<IServiceInstanceLeaseLifetime>(leaseLifetime);
        await using var provider = services.BuildServiceProvider();
        await using var manager = new PulseServiceManager(
            provider,
            NullLogger<PulseServiceManager>.Instance);
        var service = new ControlledService("actor-1", disposeFailuresBeforeSuccess: 1);
        service.ReleaseStart();
        manager.Register<ControlledService>((_, _) => service);
        await manager.GetOrCreateServiceAsync(nameof(ControlledService), "actor-1");

        var removed = await manager.RemoveServiceAsync(nameof(ControlledService), "actor-1");

        removed.Should().BeFalse();
        service.DisposeCount.Should().Be(1);
        leaseLifetime.ReleaseCount.Should().Be(0,
            "未确认实例释放时必须让旧 lease 自然过期，避免产生双 owner");

        var replacement = async () => await manager.GetOrCreateServiceAsync(
            nameof(ControlledService),
            "actor-1");
        await replacement.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*pending cleanup*");

        (await manager.RemoveServiceAsync(nameof(ControlledService), "actor-1"))
            .Should().BeTrue("a later removal retries the retained cleanup owner");
        service.DisposeCount.Should().Be(2);
        leaseLifetime.ReleaseCount.Should().Be(1);
    }

    [PulseService(
        StartupType = ServiceStartupType.OnDemand,
        InstanceScope = ServiceInstanceScope.MultiInstance)]
    private sealed class ControlledService : PulseServiceBase
    {
        private readonly bool _failOnStart;
        private readonly bool _failOnStop;
        private readonly int _disposeFailuresBeforeSuccess;
        private readonly bool _blockOnStop;
        private readonly TaskCompletionSource<bool> _startEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _startGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _stopEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _stopGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeCount;

        public ControlledService(
            string serviceId,
            bool failOnStart = false,
            bool failOnStop = false,
            int disposeFailuresBeforeSuccess = 0,
            bool blockOnStop = false)
            : base(
                nameof(ControlledService),
                serviceId,
                executionOptions: ServiceExecutionOptions.Actor)
        {
            _failOnStart = failOnStart;
            _failOnStop = failOnStop;
            _disposeFailuresBeforeSuccess = disposeFailuresBeforeSuccess;
            _blockOnStop = blockOnStop;
            if (failOnStart)
            {
                _startGate.TrySetResult(true);
            }
        }

        public Task StartEntered => _startEntered.Task;

        public Task StopEntered => _stopEntered.Task;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public Task? MessageProcessingTask
            => (Task?)typeof(PulseServiceBase)
                .GetField("_messageProcessingTask", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(this);

        public void ReleaseStart() => _startGate.TrySetResult(true);

        public void ReleaseStop() => _stopGate.TrySetResult(true);

        public override async Task OnStartingAsync(CancellationToken cancellationToken = default)
        {
            _startEntered.TrySetResult(true);
            await _startGate.Task.WaitAsync(cancellationToken);
            if (_failOnStart)
            {
                throw new InvalidOperationException("start failed");
            }
        }

        public override async ValueTask DisposeAsync()
        {
            var attempt = Interlocked.Increment(ref _disposeCount);
            if (attempt <= _disposeFailuresBeforeSuccess)
            {
                throw new InvalidOperationException("dispose failed");
            }

            await base.DisposeAsync();
        }

        public override async Task OnStoppingAsync(CancellationToken cancellationToken = default)
        {
            _stopEntered.TrySetResult(true);
            if (_blockOnStop)
            {
                await _stopGate.Task.WaitAsync(cancellationToken);
            }

            if (_failOnStop)
            {
                throw new InvalidOperationException("stop failed");
            }
        }
    }

    [PulseService(
        StartupType = ServiceStartupType.AutoStart,
        InstanceScope = ServiceInstanceScope.Singleton)]
    private sealed class PendingAutoStartService : IPulseService
    {
        private int _disposeCount;

        public string ServiceType => nameof(PendingAutoStartService);

        public string ServiceId => "default";

        public string ServiceAddress => $"{ServiceType}:{ServiceId}";

        public ServiceLifecycleState State => ServiceLifecycleState.Created;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public Task StartAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingActorLeaseLifetime : IServiceInstanceLeaseLifetime
    {
        private int _releaseCount;

        public int ReleaseCount => Volatile.Read(ref _releaseCount);

        public string? LastHub { get; private set; }

        public string? LastKey { get; private set; }

        public ValueTask ReleaseAsync(
            string hub,
            string key,
            CancellationToken cancellationToken = default)
        {
            LastHub = hub;
            LastKey = key;
            Interlocked.Increment(ref _releaseCount);
            return default;
        }
    }
}
