using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PulseRPC.Messaging;
using PulseRPC.Server.Processing.Engine;
using Xunit;

namespace PulseRPC.Server.Tests.Processing;

public class MessageDispatcherTests
{
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

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            ushort protocolId,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => RouteByProtocolIdAsync(serviceProvider, protocolId, string.Empty, data, cancellationToken);

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
    }
}
