using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Server.Services;
using PulseRPC.Server.Services.Management;
using Xunit;

#pragma warning disable CS0618 // Intentional compatibility coverage for the deprecated parallel factory.

namespace PulseRPC.Server.Tests.Services;

public sealed class PulseHubFactoryLifecycleTests
{
    [Fact]
    public async Task ConcurrentGetOrCreate_MustCreateAndPublishOnlyOneHub()
    {
        var service = new FactoryService("room-1");
        using var serviceFactory = CreateServiceFactory(service);
        await serviceFactory.GetOrCreateAsync("room-1");
        using var releaseHubCreation = new ManualResetEventSlim();
        var hubCreationEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var hubCreationCount = 0;
        using var hubFactory = new PulseHubFactory<FactoryHub, FactoryService>(
            serviceFactory,
            currentService =>
            {
                Interlocked.Increment(ref hubCreationCount);
                hubCreationEntered.TrySetResult(true);
                releaseHubCreation.Wait();
                return new FactoryHub(currentService);
            },
            NullLogger<PulseHubFactory<FactoryHub, FactoryService>>.Instance);

        var first = Task.Run(async () => await hubFactory.GetOrCreateAsync("room-1"));
        await hubCreationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = Task.Run(async () => await hubFactory.GetOrCreateAsync("room-1"));
        await Task.Delay(100);

        try
        {
            Assert.Equal(1, Volatile.Read(ref hubCreationCount));
            Assert.False(second.IsCompleted);
        }
        finally
        {
            releaseHubCreation.Set();
        }

        var hubs = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(hubs[0], hubs[1]);
    }

    [Fact]
    public async Task RemoveAsync_MustWaitForPendingHubCreation()
    {
        var service = new FactoryService("room-1");
        using var serviceFactory = CreateServiceFactory(service);
        await serviceFactory.GetOrCreateAsync("room-1");
        using var releaseHubCreation = new ManualResetEventSlim();
        var hubCreationEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var hubFactory = new PulseHubFactory<FactoryHub, FactoryService>(
            serviceFactory,
            currentService =>
            {
                hubCreationEntered.TrySetResult(true);
                releaseHubCreation.Wait();
                return new FactoryHub(currentService);
            },
            NullLogger<PulseHubFactory<FactoryHub, FactoryService>>.Instance);

        var creation = Task.Run(async () => await hubFactory.GetOrCreateAsync("room-1"));
        await hubCreationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var removal = hubFactory.RemoveAsync("room-1").AsTask();
        await Task.Delay(50);

        try
        {
            Assert.False(removal.IsCompleted);
        }
        finally
        {
            releaseHubCreation.Set();
        }

        var hub = await creation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(await removal.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, hub.DisposeCount);
    }

    private static PulseServiceFactory<FactoryService> CreateServiceFactory(FactoryService service) => new(
        _ => service,
        new PulseServiceFactoryOptions
        {
            CleanupInterval = TimeSpan.FromHours(1),
            EnableHealthCheck = false
        },
        NullLogger<PulseServiceFactory<FactoryService>>.Instance);

    private sealed class FactoryService : PulseServiceBase
    {
        public FactoryService(string id)
            : base("FactoryService", id)
        {
        }
    }

    private sealed class FactoryHub : IDisposable
    {
        private int _disposeCount;

        public FactoryHub(FactoryService service)
        {
            Service = service;
        }

        public FactoryService Service { get; }
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }
}
