using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Server.Extensions;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Server.Processing.Pipeline;
using Xunit;

namespace PulseRPC.Server.Tests.Processing;

public class MessageDispatcherTests
{
    [Fact]
    public async Task ServiceRoutingTableRegistry_Register_MustComposeAllAssemblies()
    {
        var first = new HubRoutingTable("FirstHub", 0x1001, "first");
        var second = new HubRoutingTable("SecondHub", 0x2002, "second");
        ServiceRoutingTableRegistry.Clear();

        try
        {
            ServiceRoutingTableRegistry.Register(first);
            ServiceRoutingTableRegistry.Register(second);

            var instance = ServiceRoutingTableRegistry.Instance;
            instance.Should().NotBeNull();
            var composite = instance!;
            composite.IsProtocolIdValid("FirstHub", 0x1001).Should().BeTrue();
            composite.IsProtocolIdValid("SecondHub", 0x2002).Should().BeTrue();
            composite.EnumerateProtocolIds().ToArray().Should().BeEquivalentTo(new ushort[] { 0x1001, 0x2002 });

            using var provider = new ServiceCollection().BuildServiceProvider();
            (await composite.RouteByProtocolIdAsync(
                provider,
                "SecondHub",
                0x2002,
                ReadOnlyMemory<byte>.Empty)).Should().Be("second");
        }
        finally
        {
            ServiceRoutingTableRegistry.Clear();
        }
    }

    [Fact]
    public void ResponseSerializerRegistry_Register_MustComposeAllAssemblies()
    {
        var first = new TestResponseSerializer(0x1001);
        var second = new TestResponseSerializer(0x2002);
        ResponseSerializerRegistry.Clear();

        try
        {
            ResponseSerializerRegistry.Register(new TestResponseSerializerRegistry(first));
            ResponseSerializerRegistry.Register(new TestResponseSerializerRegistry(second));

            var instance = ResponseSerializerRegistry.Instance;
            instance.Should().NotBeNull();
            var composite = instance!;
            composite.TryGetSerializer(0x1001, out var resolvedFirst).Should().BeTrue();
            composite.TryGetSerializer(0x2002, out var resolvedSecond).Should().BeTrue();
            resolvedFirst.Should().BeSameAs(first);
            resolvedSecond.Should().BeSameAs(second);
            composite.EnumerateSerializers().ToArray().Should().BeEquivalentTo(new IResponseSerializer[] { first, second });
        }
        finally
        {
            ResponseSerializerRegistry.Clear();
        }
    }

    [Fact]
    public void AddPulseServer_MustResolveRegistriesRegisteredAfterServiceConfiguration()
    {
        ServiceRoutingTableRegistry.Clear();
        ResponseSerializerRegistry.Clear();

        try
        {
            ServiceRoutingTableRegistry.Register(new HubRoutingTable("EarlyHub", 0x3002, "early"));
            ResponseSerializerRegistry.Register(
                new TestResponseSerializerRegistry(new TestResponseSerializer(0x3002)));

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddPulseServer(5000);
            using var provider = services.BuildServiceProvider();

            var resolvedRoutingTable = provider.GetRequiredService<IServiceRoutingTable>();
            var resolvedSerializerRegistry = provider.GetRequiredService<IResponseSerializerRegistry>();

            var routingTable = new HubRoutingTable("LateHub", 0x3003, "late");
            var serializerRegistry = new TestResponseSerializerRegistry(new TestResponseSerializer(0x3003));
            ServiceRoutingTableRegistry.Register(routingTable);
            ResponseSerializerRegistry.Register(serializerRegistry);

            resolvedRoutingTable.IsProtocolIdValid("EarlyHub", 0x3002).Should().BeTrue();
            resolvedRoutingTable.IsProtocolIdValid("LateHub", 0x3003).Should().BeTrue();
            resolvedSerializerRegistry.TryGetSerializer(0x3002, out _).Should().BeTrue();
            resolvedSerializerRegistry.TryGetSerializer(0x3003, out _).Should().BeTrue();
        }
        finally
        {
            ServiceRoutingTableRegistry.Clear();
            ResponseSerializerRegistry.Clear();
        }
    }

    [Fact]
    public async Task StartAsync_MustBeIdempotent()
    {
        var routingTable = new TestRoutingTable();
        ServiceRoutingTableRegistry.Clear();
        ServiceRoutingTableRegistry.Register(routingTable);

        try
        {
            using var dispatcher = new MessageDispatcher();

            await dispatcher.StartAsync();
            await dispatcher.StartAsync();
            await dispatcher.StartAsync();
        }
        finally
        {
            ServiceRoutingTableRegistry.Clear();
        }
    }

    [Fact]
    public async Task DispatchAsync_MustRouteDirectlyAndRaiseProcessedOnce()
    {
        var routingTable = new TestRoutingTable
        {
            OnRoute = (_, _, _, _, _) => new ValueTask<object?>("ok")
        };

        ServiceRoutingTableRegistry.Clear();
        ServiceRoutingTableRegistry.Register(routingTable);

        try
        {
            using var dispatcher = new MessageDispatcher();
            using var provider = new ServiceCollection().BuildServiceProvider();
            var processed = new List<MessageProcessedEventArgs>();
            dispatcher.MessageProcessed += (_, e) => processed.Add(e);

            await dispatcher.StartAsync();

            var result = await dispatcher.DispatchAsync(
                CreateEnvelope(protocolId: 0x1234, serviceKey: "room-1", payload: new byte[] { 1, 2, 3 }),
                provider);

            result.Should().Be("ok");
            routingTable.Calls.Should().ContainSingle(call =>
                call.ProtocolId == 0x1234 &&
                call.ServiceKey == "room-1" &&
                call.Payload.Length == 3);
            processed.Should().ContainSingle();
            processed[0].Success.Should().BeTrue();
            processed[0].CallContext.ProtocolId.Should().Be(0x1234);
        }
        finally
        {
            ServiceRoutingTableRegistry.Clear();
        }
    }

    [Fact]
    public async Task StopAsync_MustWaitForInFlightDispatchAndRejectNewDispatch()
    {
        var entered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var routingTable = new TestRoutingTable
        {
            OnRoute = async (_, _, _, _, _) =>
            {
                entered.TrySetResult(null);
                await release.Task;
                return "done";
            }
        };

        ServiceRoutingTableRegistry.Clear();
        ServiceRoutingTableRegistry.Register(routingTable);

        try
        {
            using var dispatcher = new MessageDispatcher();
            using var provider = new ServiceCollection().BuildServiceProvider();

            await dispatcher.StartAsync();
            var dispatchTask = dispatcher.DispatchAsync(CreateEnvelope(0x2222), provider).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            var stopTask = dispatcher.StopAsync();
            await Task.Delay(50);
            stopTask.IsCompleted.Should().BeFalse("StopAsync must wait for the in-flight route call");

            var newDispatch = async () => await dispatcher.DispatchAsync(CreateEnvelope(0x3333), provider);
            await newDispatch.Should().ThrowAsync<InvalidOperationException>();

            release.SetResult(null);
            var result = await dispatchTask;
            await stopTask;

            result.Should().Be("done");
        }
        finally
        {
            ServiceRoutingTableRegistry.Clear();
        }
    }

    private static MessageEnvelope CreateEnvelope(
        ushort protocolId,
        string serviceKey = "",
        ReadOnlyMemory<byte> payload = default)
    {
        var messageId = Guid.NewGuid();
        return new MessageEnvelope
        {
            MessageId = messageId,
            ConnectionId = "conn-1",
            Header = new MessageHeader
            {
                Type = MessageType.Request,
                MessageId = messageId,
                ServiceName = "RoomHub",
                MethodName = "Send",
                ProtocolId = protocolId,
                ServiceKey = serviceKey
            },
            Payload = payload,
            ReceivedTime = DateTime.UtcNow,
            ProcessorId = 7
        };
    }

    private sealed class TestRoutingTable : IServiceRoutingTable
    {
        public List<(ushort ProtocolId, string ServiceKey, byte[] Payload)> Calls { get; } = new();

        public Func<IServiceProvider, ushort, string, ReadOnlyMemory<byte>, CancellationToken, ValueTask<object?>> OnRoute { get; set; }
            = (_, _, _, _, _) => new ValueTask<object?>((object?)null);

        public ReadOnlySpan<ushort> EnumerateProtocolIds() => Array.Empty<ushort>();

        public bool IsProtocolIdValid(string hub, ushort protocolId) => true;

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            ushort protocolId,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => RouteByProtocolIdAsync(serviceProvider, protocolId, string.Empty, data, cancellationToken);

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            string hub,
            ushort protocolId,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => RouteByProtocolIdAsync(serviceProvider, protocolId, data, cancellationToken);

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            ushort protocolId,
            string serviceKey,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((protocolId, serviceKey, data.ToArray()));
            return OnRoute(serviceProvider, protocolId, serviceKey, data, cancellationToken);
        }

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            string hub,
            ushort protocolId,
            string serviceKey,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => RouteByProtocolIdAsync(serviceProvider, protocolId, serviceKey, data, cancellationToken);
    }

    private sealed class HubRoutingTable(string hub, ushort protocolId, object? result) : IServiceRoutingTable
    {
        private readonly ushort[] _protocolIds = [protocolId];

        public bool IsProtocolIdValid(string candidateHub, ushort candidateProtocolId)
            => candidateHub == hub && candidateProtocolId == protocolId;

        public ReadOnlySpan<ushort> EnumerateProtocolIds() => _protocolIds;

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            ushort candidateProtocolId,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => candidateProtocolId == protocolId
                ? new ValueTask<object?>(result)
                : throw new InvalidOperationException();

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            string candidateHub,
            ushort candidateProtocolId,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => IsProtocolIdValid(candidateHub, candidateProtocolId)
                ? new ValueTask<object?>(result)
                : throw new InvalidOperationException();

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            ushort candidateProtocolId,
            string serviceKey,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => RouteByProtocolIdAsync(serviceProvider, candidateProtocolId, data, cancellationToken);

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            string candidateHub,
            ushort candidateProtocolId,
            string serviceKey,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => RouteByProtocolIdAsync(serviceProvider, candidateHub, candidateProtocolId, data, cancellationToken);
    }

    private sealed class TestResponseSerializerRegistry(params IResponseSerializer[] serializers) : IResponseSerializerRegistry
    {
        public bool TryGetSerializer(ushort protocolId, [NotNullWhen(true)] out IResponseSerializer? serializer)
        {
            serializer = serializers.FirstOrDefault(candidate => candidate.ProtocolId == protocolId);
            return serializer is not null;
        }

        public ReadOnlySpan<IResponseSerializer> EnumerateSerializers() => serializers;
    }

    private sealed class TestResponseSerializer(ushort protocolId) : IResponseSerializer
    {
        public ushort ProtocolId { get; } = protocolId;

        public void Serialize(object response, IBufferWriter<byte> writer)
        {
        }

        public ValueTask SerializeAsync(
            object response,
            IBufferWriter<byte> writer,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public bool TryGetTypedSerializer<T>(out Action<T, IBufferWriter<byte>> serializer)
        {
            serializer = null!;
            return false;
        }
    }
}
