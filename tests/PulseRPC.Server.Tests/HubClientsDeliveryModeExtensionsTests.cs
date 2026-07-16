using FluentAssertions;
using PulseRPC.Server;
using Xunit;

namespace PulseRPC.Server.Tests;

public sealed class HubClientsDeliveryModeExtensionsTests
{
    [Theory]
    [InlineData(ReceiverDeliveryMode.BestEffort)]
    [InlineData(ReceiverDeliveryMode.Strict)]
    public void WithDeliveryMode_MustUseRuntimeCapability(ReceiverDeliveryMode mode)
    {
        IHubClients<ITestReceiver> clients = new SelectableHubClients();

        var selected = clients.WithDeliveryMode(mode);

        selected.Should().BeOfType<SelectableHubClients>()
            .Which.Mode.Should().Be(mode);
    }

    [Fact]
    public void WithDeliveryMode_MustRejectAnImplementationWithoutTheCapability()
    {
        IHubClients<ITestReceiver> clients = new UnsupportedHubClients();

        var act = () => clients.WithDeliveryMode(ReceiverDeliveryMode.Strict);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void WithDeliveryMode_MustRejectAnUnknownModeBeforeDispatch()
    {
        IHubClients<ITestReceiver> clients = new SelectableHubClients();

        var act = () => clients.WithDeliveryMode((ReceiverDeliveryMode)byte.MaxValue);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private interface ITestReceiver : IPulseHub
    {
    }

    private sealed class SelectableHubClients : HubClientsStub,
        IReceiverDeliveryModeSelector<ITestReceiver>
    {
        public SelectableHubClients(ReceiverDeliveryMode mode = ReceiverDeliveryMode.BestEffort)
        {
            Mode = mode;
        }

        public ReceiverDeliveryMode Mode { get; }

        public IHubClients<ITestReceiver> WithDeliveryMode(ReceiverDeliveryMode deliveryMode) =>
            new SelectableHubClients(deliveryMode);
    }

    private sealed class UnsupportedHubClients : HubClientsStub
    {
    }

    private abstract class HubClientsStub : IHubClients<ITestReceiver>
    {
        public ITestReceiver All => throw new NotSupportedException();
        public ITestReceiver Single(string connectionId) => throw new NotSupportedException();
        public ITestReceiver Only(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public ITestReceiver Except(string connectionId) => throw new NotSupportedException();
        public ITestReceiver Except(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public ITestReceiver User(string userId) => throw new NotSupportedException();
        public ITestReceiver Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
        public ITestReceiver Group(string groupName) => throw new NotSupportedException();
        public ITestReceiver Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public ITestReceiver GroupExcept(string groupName, string excludedConnectionId) =>
            throw new NotSupportedException();
        public ITestReceiver GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) =>
            throw new NotSupportedException();
    }
}
