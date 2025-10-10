using FluentAssertions;
using NSubstitute;
using Xunit;

namespace PulseRPC.Server.Tests.Contract;

/// <summary>
/// Contract tests for IMessageDispatcher interface.
/// Based on contracts/dispatcher-api.yaml
/// </summary>
public class DispatcherContractTests
{
    [Fact]
    public async Task StartAsync_ShouldBeIdempotent()
    {
        // Arrange: Create dispatcher
        // TODO: Implement after IMessageDispatcher is available

        // Act: Call StartAsync twice
        // await dispatcher.StartAsync();
        // await dispatcher.StartAsync();

        // Assert: No errors thrown
        Assert.True(false, "Not implemented - awaiting IMessageDispatcher");
    }

    [Fact]
    public async Task StopAsync_ShouldWaitForInFlightRequests()
    {
        // Arrange: Start processing with in-flight request

        // Act: Call StopAsync

        // Assert: Waits for in-flight completion before stopping
        Assert.True(false, "Not implemented - awaiting IMessageDispatcher");
    }

    [Fact]
    public async Task DispatchMessageAsync_ShouldRouteToCorrectService()
    {
        // Arrange: Register mock service handler

        // Act: Dispatch message to specific service

        // Assert: Service lookup called with correct name
        Assert.True(false, "Not implemented - awaiting IMessageDispatcher");
    }

    [Fact]
    public async Task DispatchMessageAsync_ShouldPreserveFIFOPerConnection()
    {
        // Arrange: Dispatch 10 messages from same connection

        // Act: Process messages

        // Assert: Order preserved (RequestId sequence maintained)
        Assert.True(false, "Not implemented - awaiting IMessageDispatcher");
    }

    [Fact]
    public void RegisterServiceHandler_ShouldRejectDuplicate()
    {
        // Arrange: Register service once

        // Act: Attempt to register same service again

        // Assert: ArgumentException thrown
        Assert.True(false, "Not implemented - awaiting IMessageDispatcher");
    }

    [Fact]
    public async Task MessageProcessed_ShouldFireExactlyOnce()
    {
        // Arrange: Setup message processed event listener

        // Act: Dispatch message

        // Assert: Event fired exactly once
        Assert.True(false, "Not implemented - awaiting IMessageDispatcher");
    }
}
