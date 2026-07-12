using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Extensions;
using PulseRPC.Server.Services;
using PulseRPC.Server.Services.Management;
using Xunit;

namespace PulseRPC.Server.Tests.Services;

public sealed class PulseServiceRegistrationTests
{
    [Fact]
    public async Task AddPulseService_RegistersCatalogBeforeHostStart()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPulseService<RegistrationTestService>(
            (_, id) => new RegistrationTestService(id));

        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<PulseServiceManager>();
        var accessor = provider.GetRequiredService<IServiceAccessor<RegistrationTestService>>();

        manager.GetStatistics().RegisteredTypes.Should().Be(1);
        provider.GetService<RegistrationTestService>().Should().BeNull(
            "managed services must only be resolved through IServiceAccessor");

        var instance = await accessor.GetAsync("actor-1");
        instance.ServiceId.Should().Be("actor-1");
        instance.State.Should().Be(ServiceLifecycleState.Running);
        instance.StartCount.Should().Be(1);
    }

    [Fact]
    public async Task AddPulseService_DoesNotResetExplicitManagerOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPulseServiceManagement(options =>
        {
            options.ContinueOnAutoStartFailure = true;
            options.EnableInstanceEviction = false;
            options.CleanupInterval = TimeSpan.FromSeconds(17);
            options.MaxCachedInstances = 123;
        });
        services.AddPulseService<RegistrationTestService>(
            (_, id) => new RegistrationTestService(id));

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PulseServiceManagerOptions>>().Value;

        options.ContinueOnAutoStartFailure.Should().BeTrue();
        options.EnableInstanceEviction.Should().BeFalse();
        options.CleanupInterval.Should().Be(TimeSpan.FromSeconds(17));
        options.MaxCachedInstances.Should().Be(123);
        provider.GetService<PulseServiceManagerOptions>().Should().BeNull(
            "IOptions<PulseServiceManagerOptions> is the only registered options source");
    }

    [Fact]
    public async Task HostStart_DoesNotRegisterTheCatalogTwice()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPulseService<RegistrationTestService>(
            (_, id) => new RegistrationTestService(id));

        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<PulseServiceManager>();
        manager.GetStatistics().RegisteredTypes.Should().Be(1);

        var hosted = provider.GetServices<IHostedService>()
            .OfType<PulseServiceManagerHostedService>()
            .Single();

        var start = () => hosted.StartAsync(CancellationToken.None);
        await start.Should().NotThrowAsync();
        manager.GetStatistics().RegisteredTypes.Should().Be(1);
    }

    [Fact]
    public async Task DisabledEviction_DoesNotStartCleanupLoop()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPulseServiceManagement(options => options.EnableInstanceEviction = false);

        await using var provider = services.BuildServiceProvider();
        var evictor = provider.GetServices<IHostedService>()
            .OfType<ServiceInstanceEvictor>()
            .Single();

        await evictor.StartAsync(CancellationToken.None);

        var statistics = evictor.GetStatistics();
        statistics.IsRunning.Should().BeFalse();
        statistics.TotalCleanupRuns.Should().Be(0);
    }

    [Fact]
    public async Task ProviderDisposal_DisposesInstancesCreatedBeforeHostStartOnce()
    {
        RegistrationTestService? created = null;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPulseService<RegistrationTestService>((_, id) =>
        {
            created = new RegistrationTestService(id);
            return created;
        });

        var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IServiceAccessor<RegistrationTestService>>();
        await accessor.GetAsync("actor-2");

        await provider.DisposeAsync();

        created.Should().NotBeNull();
        created!.StopCount.Should().Be(1);
        created.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task AutoStartFailure_MustRollbackPreviouslyStartedServices()
    {
        AutoStartSuccessService? first = null;
        AutoStartFailureService? second = null;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPulseServiceManagement(options =>
            options.ContinueOnAutoStartFailure = false);
        services.AddPulseService<AutoStartSuccessService>((_, id) =>
            first = new AutoStartSuccessService(id));
        services.AddPulseService<AutoStartFailureService>((_, id) =>
            second = new AutoStartFailureService(id));

        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<PulseServiceManager>();

        var start = () => manager.StartAutoStartServicesAsync();
        await start.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("auto-start failed");

        first.Should().NotBeNull();
        first!.StartCount.Should().Be(1);
        first.StopCount.Should().Be(1);
        first.DisposeCount.Should().Be(1);
        second.Should().NotBeNull();
        second!.DisposeCount.Should().Be(1);
        manager.GetStatistics().ActiveInstances.Should().Be(0);
    }

    [Fact]
    public async Task AutoStartRollback_WhenDisposeRetryFails_RetainsOwnerAndReportsAggregateFailure()
    {
        AutoStartSuccessService? first = null;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPulseServiceManagement(options =>
            options.ContinueOnAutoStartFailure = false);
        services.AddPulseService<AutoStartSuccessService>((_, id) =>
            first = new AutoStartSuccessService(id, disposeFailuresBeforeSuccess: 2));
        services.AddPulseService<AutoStartFailureService>((_, id) =>
            new AutoStartFailureService(id));

        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<PulseServiceManager>();

        var start = () => manager.StartAutoStartServicesAsync();
        await start.Should().ThrowAsync<AggregateException>()
            .WithMessage("*rollback cleanup was incomplete*");

        first.Should().NotBeNull();
        first!.StopCount.Should().Be(1);
        first.DisposeCount.Should().Be(2, "rollback must retry a transient Dispose failure once");
        manager.GetStatistics().ActiveInstances.Should().Be(0);

        var replacement = async () => await manager.GetOrCreateServiceAsync(
            "AutoStartSuccess",
            "default");
        await replacement.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*pending cleanup*");

        (await manager.RemoveServiceAsync("AutoStartSuccess", "default"))
            .Should().BeTrue("cleanup ownership remains available for an explicit retry");
        first.DisposeCount.Should().Be(3);
    }

    [Fact]
    public async Task HostedStop_DefersManagerDisposalUntilProviderShutdown()
    {
        RegistrationTestService? created = null;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPulseService<RegistrationTestService>((_, id) =>
            created = new RegistrationTestService(id));

        var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IServiceAccessor<RegistrationTestService>>();
        var hosted = provider.GetServices<IHostedService>()
            .OfType<PulseServiceManagerHostedService>()
            .Single();
        await accessor.GetAsync("shutdown-window");

        await hosted.StopAsync(CancellationToken.None);

        created.Should().NotBeNull();
        created!.DisposeCount.Should().Be(0,
            "a server may still resolve managed services until all hosted services have stopped");
        (await accessor.GetAsync("shutdown-window")).Should().BeSameAs(created);

        await provider.DisposeAsync();
        created.DisposeCount.Should().Be(1);
    }

    [PulseService(
        DisplayName = "RegistrationTest",
        StartupType = ServiceStartupType.OnDemand,
        InstanceScope = ServiceInstanceScope.MultiInstance,
        IdleTimeoutSeconds = 0,
        EnableHealthCheck = false)]
    private sealed class RegistrationTestService(string serviceId) : IPulseService
    {
        private int _state = (int)ServiceLifecycleState.Created;
        private int _startCount;
        private int _stopCount;
        private int _disposeCount;

        public string ServiceType => "RegistrationTest";
        public string ServiceId { get; } = serviceId;
        public ServiceLifecycleState State => (ServiceLifecycleState)Volatile.Read(ref _state);
        public int StartCount => Volatile.Read(ref _startCount);
        public int StopCount => Volatile.Read(ref _stopCount);
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _startCount);
            Volatile.Write(ref _state, (int)ServiceLifecycleState.Running);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _stopCount);
            Volatile.Write(ref _state, (int)ServiceLifecycleState.Stopped);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }

    [PulseService(
        DisplayName = "AutoStartSuccess",
        StartupType = ServiceStartupType.AutoStart,
        InstanceScope = ServiceInstanceScope.Singleton,
        EnableHealthCheck = false)]
    private sealed class AutoStartSuccessService : IPulseService
    {
        private readonly int _disposeFailuresBeforeSuccess;
        private int _state = (int)ServiceLifecycleState.Created;

        public AutoStartSuccessService(
            string serviceId,
            int disposeFailuresBeforeSuccess = 0)
        {
            ServiceId = serviceId;
            _disposeFailuresBeforeSuccess = disposeFailuresBeforeSuccess;
        }

        public string ServiceType => "AutoStartSuccess";
        public string ServiceId { get; }
        public ServiceLifecycleState State => (ServiceLifecycleState)Volatile.Read(ref _state);
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            Volatile.Write(ref _state, (int)ServiceLifecycleState.Running);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            Volatile.Write(ref _state, (int)ServiceLifecycleState.Stopped);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            if (DisposeCount <= _disposeFailuresBeforeSuccess)
            {
                throw new InvalidOperationException("auto-start rollback dispose failed");
            }

            return ValueTask.CompletedTask;
        }
    }

    [PulseService(
        DisplayName = "AutoStartFailure",
        StartupType = ServiceStartupType.AutoStart,
        InstanceScope = ServiceInstanceScope.Singleton,
        EnableHealthCheck = false)]
    private sealed class AutoStartFailureService(string serviceId) : IPulseService
    {
        public string ServiceType => "AutoStartFailure";
        public string ServiceId { get; } = serviceId;
        public ServiceLifecycleState State => ServiceLifecycleState.Created;
        public int DisposeCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("auto-start failed");

        public Task StopAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
