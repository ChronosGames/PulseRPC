using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PulseRPC.Serialization;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Extensions;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Server.Security;
using Xunit;

namespace PulseRPC.Server.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ClientFacingGateTestCollection
{
    public const string Name = "ClientFacingGate";
}

[Collection(ClientFacingGateTestCollection.Name)]
public sealed class NamedServerClientFacingGateIsolationTests
{
    [Theory]
    [InlineData("enabled", "disabled")]
    [InlineData("disabled", "enabled")]
    public async Task ResolvingNamedServers_MustKeepIndependentHostGatePolicies(
        string firstServer,
        string secondServer)
    {
        var previous = ClientFacingGate.EnforcementEnabled;
        ClientFacingGate.EnforcementEnabled = false;

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IServiceRoutingTable>(new StubRoutingTable());
            services.AddSingleton<IResponseSerializerRegistry>(new EmptyResponseSerializerRegistry());
            services.AddNamedPulseServer("enabled", options =>
            {
                options.EnableClientFacingGate = true;
                options.AddTcp("enabled", 5101);
            });
            services.AddNamedPulseServer("disabled", options =>
            {
                options.EnableClientFacingGate = false;
                options.AddTcp("disabled", 5102);
            });

            await using var provider = services.BuildServiceProvider();

            _ = provider.GetRequiredKeyedService<INamedPulseServer>(firstServer);
            ClientFacingGate.EnforcementEnabled.Should().BeFalse();

            _ = provider.GetRequiredKeyedService<INamedPulseServer>(secondServer);
            ClientFacingGate.EnforcementEnabled.Should().BeFalse();

            var enabledProvider = provider
                .GetRequiredKeyedService<ClientFacingGateServiceProvider>("enabled");
            var disabledProvider = provider
                .GetRequiredKeyedService<ClientFacingGateServiceProvider>("disabled");

            GetEngineServiceProvider(provider, "enabled").Should().BeSameAs(enabledProvider);
            GetEngineServiceProvider(provider, "disabled").Should().BeSameAs(disabledProvider);

            using (PulseContext.SetContext(PulseContextData.CreateUserContext("user-1")))
            {
                var enabledCall = () => ClientFacingGate.Enforce(
                    enabledProvider,
                    isClientFacing: false,
                    protocolId: 0x1234,
                    methodDisplayName: "ITestHub.InvokeAsync");
                var disabledCall = () => ClientFacingGate.Enforce(
                    disabledProvider,
                    isClientFacing: false,
                    protocolId: 0x1234,
                    methodDisplayName: "ITestHub.InvokeAsync");

                enabledCall.Should().Throw<ClientFacingAccessDeniedException>();
                disabledCall.Should().NotThrow();

                ClientFacingGate.EnforcementEnabled = true;
                enabledCall.Should().Throw<ClientFacingAccessDeniedException>();
                disabledCall.Should().NotThrow(
                    "宿主显式关闭策略必须覆盖兼容用的进程静态回退值");
            }
        }
        finally
        {
            ClientFacingGate.EnforcementEnabled = previous;
        }
    }

    private static IServiceProvider GetEngineServiceProvider(
        IServiceProvider provider,
        string serverName)
    {
        var engine = provider.GetRequiredKeyedService<ITieredMessageEngine>(serverName);
        var field = typeof(MessageEngine).GetField(
            "_serviceProvider",
            BindingFlags.Instance | BindingFlags.NonPublic);

        field.Should().NotBeNull();
        return field!.GetValue(engine).Should().BeAssignableTo<IServiceProvider>().Which;
    }

    private sealed class StubRoutingTable : IServiceRoutingTable
    {
        public bool IsProtocolIdValid(string hub, ushort protocolId) => false;
        public ReadOnlySpan<ushort> EnumerateProtocolIds() => [];

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            ushort protocolId,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<object?>(null);

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            string hub,
            ushort protocolId,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<object?>(null);

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            ushort protocolId,
            string serviceKey,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<object?>(null);

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            string hub,
            ushort protocolId,
            string serviceKey,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<object?>(null);
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
}
