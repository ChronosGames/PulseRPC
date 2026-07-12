using System.Diagnostics.CodeAnalysis;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PulseRPC.Serialization;
using PulseRPC.Routing;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Extensions;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Server.Security;
using PulseRPC.Server.Transport;
using PulseRPC.Shared;
using Xunit;

namespace PulseRPC.Server.Tests;

public sealed class ServerRuntimeCompositionTests
{
    [Fact]
    public void UnwiredServerCompatibilityOptions_MustBeMarkedObsolete()
    {
#pragma warning disable CS0618 // Intentional compatibility-contract coverage.
        var compatibilityTypes = new[]
        {
            typeof(BackpressureStrategy),
            typeof(ServiceLifecycleOptions)
        };
#pragma warning restore CS0618

        compatibilityTypes.Should().OnlyContain(type =>
            Attribute.GetCustomAttribute(type, typeof(ObsoleteAttribute)) != null);
    }

    [Fact]
    public void PulseServerOptions_MustRejectInvalidEffectiveMessageSettings()
    {
        var invalidShardCount = new PulseServerOptions
        {
            MessageWorkerShardCount = 0,
            MessageQueueCapacityPerShard = 1
        }.AddTcp("main", 5100);
        var invalidQueueCapacity = new PulseServerOptions
        {
            MessageWorkerShardCount = 1,
            MessageQueueCapacityPerShard = 0
        }.AddTcp("main", 5100);

        invalidShardCount.Invoking(options => options.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*MessageWorkerShardCount*");
        invalidQueueCapacity.Invoking(options => options.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*MessageQueueCapacityPerShard*");
    }

    [Fact]
    public async Task StandardFacade_MustUseTheRegisteredRuntimeAndChannelRegistry()
    {
        var services = CreateServices();
        services.AddPulseServer(options => options.AddTcp("standard", 5100));

        await using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<PulseServer>();

        server.Runtime.Should().BeSameAs(provider.GetRequiredService<ServerRuntime>());
        server.Runtime.ChannelRegistry.Should().BeSameAs(
            provider.GetRequiredService<IServerChannelManager>());
        server.Runtime.MessageEngine.Should().BeSameAs(
            provider.GetRequiredService<ITieredMessageEngine>());
    }

    [Fact]
    public async Task NamedFacades_MustShareOneRuntimePerNameAndIsolateRegistriesAcrossNames()
    {
        var services = CreateServices();
        services.AddNamedPulseServer("alpha", options => options.AddTcp("alpha", 5101));
        services.AddNamedPulseServer("beta", options => options.AddTcp("beta", 5102));

        await using var provider = services.BuildServiceProvider();
        var alpha = provider.GetRequiredKeyedService<INamedPulseServer>("alpha")
            .Should().BeOfType<NamedPulseServer>().Subject;
        var beta = provider.GetRequiredKeyedService<INamedPulseServer>("beta")
            .Should().BeOfType<NamedPulseServer>().Subject;

        alpha.Runtime.Should().BeSameAs(
            provider.GetRequiredKeyedService<ServerRuntime>("alpha"));
        beta.Runtime.Should().BeSameAs(
            provider.GetRequiredKeyedService<ServerRuntime>("beta"));
        alpha.Runtime.ChannelRegistry.Should().BeSameAs(
            provider.GetRequiredKeyedService<IServerChannelManager>("alpha"));
        beta.Runtime.ChannelRegistry.Should().BeSameAs(
            provider.GetRequiredKeyedService<IServerChannelManager>("beta"));

        alpha.Runtime.Should().NotBeSameAs(beta.Runtime);
        alpha.Runtime.ChannelRegistry.Should().NotBeSameAs(beta.Runtime.ChannelRegistry);
        provider.GetServices<ITransportProvider>().OfType<TcpTransportProvider>()
            .Should().ContainSingle();
        provider.GetServices<ITransportProvider>().OfType<KcpTransportProvider>()
            .Should().ContainSingle();

        var alphaHost = provider.GetRequiredKeyedService<ClientFacingGateServiceProvider>("alpha");
        var betaHost = provider.GetRequiredKeyedService<ClientFacingGateServiceProvider>("beta");
        alphaHost.GetService(typeof(IServerChannelManager))
            .Should().BeSameAs(alpha.Runtime.ChannelRegistry);
        betaHost.GetService(typeof(IServerChannelManager))
            .Should().BeSameAs(beta.Runtime.ChannelRegistry);
        alphaHost.GetService(typeof(IPulseRouter))
            .Should().BeSameAs(provider.GetRequiredKeyedService<IPulseRouter>("alpha"));
        betaHost.GetService(typeof(IPulseRouter))
            .Should().BeSameAs(provider.GetRequiredKeyedService<IPulseRouter>("beta"));
        alphaHost.GetService(typeof(IPulseRouter))
            .Should().NotBeSameAs(betaHost.GetService(typeof(IPulseRouter)));
    }

    [Fact]
    public async Task NamedHostedService_MustStartCatalogAndStopInReverseLifecycle()
    {
        var services = CreateServices();
        services.AddSingleton<ITransportIntegrationManager>(new TestTransportIntegrationManager());
        services.AddNamedPulseServer("alpha", options => options.AddTcp("alpha", 5101));
        services.AddNamedPulseServer("beta", options => options.AddTcp("beta", 5102));

        await using var provider = services.BuildServiceProvider();
        provider.GetServices<NamedServerRegistration>()
            .Select(registration => registration.ServerName)
            .Should().Equal("alpha", "beta");
        var hosted = provider.GetServices<IHostedService>()
            .OfType<NamedPulseServersHostedService>()
            .Should().ContainSingle().Subject;

        await hosted.StartAsync(CancellationToken.None);
        var alpha = provider.GetRequiredKeyedService<INamedPulseServer>("alpha");
        var beta = provider.GetRequiredKeyedService<INamedPulseServer>("beta");
        alpha.IsRunning.Should().BeTrue();
        beta.IsRunning.Should().BeTrue();

        await hosted.StopAsync(CancellationToken.None);
        alpha.IsRunning.Should().BeFalse();
        beta.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task NamedHostedService_StopFailure_MustStillStopRemainingServers()
    {
        var alpha = Substitute.For<INamedPulseServer>();
        var beta = Substitute.For<INamedPulseServer>();
        alpha.ServerName.Returns("alpha");
        beta.ServerName.Returns("beta");
        alpha.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        beta.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        alpha.StopAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        beta.StopAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("beta stop failed")));

        var services = new ServiceCollection();
        services.AddKeyedSingleton("alpha", alpha);
        services.AddKeyedSingleton("beta", beta);
        await using var provider = services.BuildServiceProvider();
        var hosted = new NamedPulseServersHostedService(
            provider,
            [new NamedServerRegistration("alpha"), new NamedServerRegistration("beta")],
            NullLogger<NamedPulseServersHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);
        var stop = () => hosted.StopAsync(CancellationToken.None);
        await stop.Should().ThrowAsync<AggregateException>()
            .WithMessage("*failed to stop*");

        await beta.Received(1).StopAsync(Arg.Any<CancellationToken>());
        await alpha.Received(1).StopAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompatibilityFactory_MustReuseStandardCompositionAndOwnItsDependencies()
    {
#pragma warning disable CS0618
        var server = PulseServerFactory.Create(options =>
        {
            options.MessageWorkerShardCount = 2;
            options.MessageQueueCapacityPerShard = 8;
            options.AddTcp("factory", 5103);
        });
#pragma warning restore CS0618

        var owned = server.Should().BeOfType<OwnedPulseServer>().Subject;
        var runtime = owned.Runtime;
        runtime.MessageEngine.Should().BeOfType<MessageEngine>()
            .Which.WorkerShardIds.Should().HaveCount(2);

        var channel = runtime.ChannelRegistry.AddChannel(new MockServerTransport("factory-conn"));
#pragma warning disable CS0618 // Intentional coverage of the read-only compatibility view.
        server.ChannelPool.GetChannel("factory-conn").Should().BeSameAs(channel);
#pragma warning restore CS0618

        await server.DisposeAsync();

        var addAfterDispose = () =>
            runtime.ChannelRegistry.AddChannel(new MockServerTransport("after-dispose"));
        addAfterDispose.Should().Throw<ObjectDisposedException>();

        // Both the owned facade and the DI provider may observe disposal. It is idempotent.
        await server.DisposeAsync();
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IServiceRoutingTable>(new StubRoutingTable());
        services.AddSingleton<IResponseSerializerRegistry>(new EmptyResponseSerializerRegistry());
        return services;
    }

    private sealed class StubRoutingTable : IServiceRoutingTable
    {
        public bool IsProtocolIdValid(string hub, ushort protocolId) => false;
        public ReadOnlySpan<ushort> EnumerateProtocolIds() => [];

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            ushort protocolId,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<object?>(null);

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            string hub,
            ushort protocolId,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<object?>(null);

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            ushort protocolId,
            string serviceKey,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<object?>(null);

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            string hub,
            ushort protocolId,
            string serviceKey,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<object?>(null);
    }

    private sealed class EmptyResponseSerializerRegistry : IResponseSerializerRegistry
    {
        public bool TryGetSerializer(
            ushort protocolId,
            [NotNullWhen(true)] out IResponseSerializer? serializer)
        {
            serializer = null;
            return false;
        }

        public ReadOnlySpan<IResponseSerializer> EnumerateSerializers() => [];
    }

    private sealed class TestTransportIntegrationManager : ITransportIntegrationManager
    {
        public void RegisterProvider(ITransportProvider provider)
        {
        }

        public IServerListener CreateListener(
            TransportChannelConfiguration config,
            ILoggerFactory loggerFactory)
            => new TestServerListener(config.Name, config.Type, config.Port);

        public IReadOnlyList<string> GetSupportedTransportTypes()
            => [TransportType.TCP.ToString(), TransportType.KCP.ToString()];

        public bool IsSupported(string transportType) => true;
    }

    private sealed class TestServerListener(
        string name,
        TransportType type,
        int port) : IServerListener
    {
        public string Name { get; } = name;
        public TransportType Type { get; } = type;
        public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, port);
        public bool IsListening { get; private set; }
        public event EventHandler<ServerConnectionEventArgs>? ConnectionAccepted;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            IsListening = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsListening = false;
            return Task.CompletedTask;
        }

        public void Dispose() => IsListening = false;
    }
}
