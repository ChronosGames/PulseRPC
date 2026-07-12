using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Server.Services;
using PulseRPC.Server.Services.Management;
using Xunit;

#pragma warning disable CS0618 // Intentional compatibility coverage for the deprecated parallel factory.

namespace PulseRPC.Server.Tests.Services;

public sealed class PulseServiceFactoryLifecycleTests
{
    [Fact]
    public async Task ConcurrentGetOrCreate_MustNotExposeServiceBeforeStartCompletes()
    {
        var service = new ControlledFactoryService("room-1");
        using var factory = CreateFactory(_ => service);

        var first = factory.GetOrCreateAsync("room-1").AsTask();
        await service.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = factory.GetOrCreateAsync("room-1").AsTask();

        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        Assert.False(factory.TryGet("room-1", out _));

        service.ReleaseStart();
        var instances = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(instances[0], instances[1]);
        Assert.Equal(ServiceLifecycleState.Running, service.State);
    }

    [Fact]
    public async Task StartFailure_MustNotPublishAndMustDisposeService()
    {
        var service = new ControlledFactoryService("room-1", failOnStart: true);
        service.ReleaseStart();
        using var factory = CreateFactory(_ => service);

        await Assert.ThrowsAsync<ServiceActivationException>(async () =>
            await factory.GetOrCreateAsync("room-1"));

        Assert.False(factory.TryGet("room-1", out _));
        Assert.Equal(1, service.DisposeCount);
    }

    private static PulseServiceFactory<ControlledFactoryService> CreateFactory(
        Func<string, ControlledFactoryService> serviceFactory) => new(
            serviceFactory,
            new PulseServiceFactoryOptions
            {
                CleanupInterval = TimeSpan.FromHours(1),
                HealthCheckInterval = TimeSpan.FromHours(1),
                EnableHealthCheck = false
            },
            NullLogger<PulseServiceFactory<ControlledFactoryService>>.Instance);

    private sealed class ControlledFactoryService : PulseServiceBase
    {
        private readonly bool _failOnStart;
        private readonly TaskCompletionSource<bool> _startGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeCount;

        public ControlledFactoryService(string id, bool failOnStart = false)
            : base("FactoryService", id)
        {
            _failOnStart = failOnStart;
        }

        public TaskCompletionSource<bool> StartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void ReleaseStart() => _startGate.TrySetResult(true);

        public override async Task OnStartingAsync(CancellationToken cancellationToken = default)
        {
            StartEntered.TrySetResult(true);
            await _startGate.Task.WaitAsync(cancellationToken);
            if (_failOnStart)
            {
                throw new InvalidOperationException("start failed");
            }
        }

        public override async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            await base.DisposeAsync();
        }
    }
}
