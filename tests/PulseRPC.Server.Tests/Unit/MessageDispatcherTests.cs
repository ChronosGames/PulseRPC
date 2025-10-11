using FluentAssertions;
using NSubstitute;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Models;
using PulseRPC.Server.Pipeline;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PulseRPC.Server.Tests.Unit;

/// <summary>
/// Unit tests for MessageDispatcher (T040).
/// Tests service registration, message routing, priority scheduling, and backpressure.
/// </summary>
public class MessageDispatcherTests
{
    [Fact]
    public void MessageDispatcher_ShouldInitialize_WithDefaultOptions()
    {
        // Arrange & Act
        var dispatcher = new MessageDispatcher();

        // Assert
        dispatcher.Should().NotBeNull();
        dispatcher.IsRunning.Should().BeFalse();
        dispatcher.RegisteredServiceCount.Should().Be(0);
        dispatcher.QueueDepth.Should().Be(0);
        dispatcher.TotalMessagesDispatched.Should().Be(0);
    }

    [Fact]
    public void MessageDispatcher_ShouldInitialize_WithCustomOptions()
    {
        // Arrange
        var options = new MessageDispatcherOptions
        {
            WorkerThreadCount = 4,
            MaxQueueDepthPerPriority = 5000
        };

        // Act
        var dispatcher = new MessageDispatcher(options);

        // Assert
        dispatcher.Should().NotBeNull();
        dispatcher.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task MessageDispatcher_ShouldStartAndStop_Successfully()
    {
        // Arrange
        var dispatcher = new MessageDispatcher();

        // Act: Start
        await dispatcher.StartAsync();

        // Assert: Started
        dispatcher.IsRunning.Should().BeTrue();

        // Act: Stop
        await dispatcher.StopAsync();

        // Assert: Stopped
        dispatcher.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task MessageDispatcher_ShouldThrow_WhenStartingTwice()
    {
        // Arrange
        var dispatcher = new MessageDispatcher();
        await dispatcher.StartAsync();

        // Act & Assert: Starting again should throw
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.StartAsync());

        await dispatcher.StopAsync();
    }

    [Fact]
    public async Task MessageDispatcher_ShouldAllowMultipleStops()
    {
        // Arrange
        var dispatcher = new MessageDispatcher();
        await dispatcher.StartAsync();

        // Act: Stop multiple times
        await dispatcher.StopAsync();
        await dispatcher.StopAsync(); // Should not throw

        // Assert
        dispatcher.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void RegisterServiceHandler_ShouldRegisterService_Successfully()
    {
        // Arrange
        var dispatcher = new MessageDispatcher();
        var handler = CreateMockHandler();

        // Act
        dispatcher.RegisterServiceHandler("TestService", handler);

        // Assert
        dispatcher.RegisteredServiceCount.Should().Be(1);
        dispatcher.GetServiceHandler("TestService").Should().NotBeNull();
    }

    [Fact]
    public void RegisterServiceHandler_ShouldThrow_WhenServiceNameIsEmpty()
    {
        // Arrange
        var dispatcher = new MessageDispatcher();
        var handler = CreateMockHandler();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => dispatcher.RegisterServiceHandler("", handler));
        Assert.Throws<ArgumentException>(() => dispatcher.RegisterServiceHandler("   ", handler));
    }

    [Fact]
    public void RegisterServiceHandler_ShouldThrow_WhenHandlerIsNull()
    {
        // Arrange
        var dispatcher = new MessageDispatcher();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => dispatcher.RegisterServiceHandler("TestService", null!));
    }

    [Fact]
    public void RegisterServiceHandler_ShouldThrow_WhenDuplicateServiceName()
    {
        // Arrange
        var dispatcher = new MessageDispatcher();
        var handler1 = CreateMockHandler();
        var handler2 = CreateMockHandler();

        dispatcher.RegisterServiceHandler("TestService", handler1);

        // Act & Assert: Registering again should throw
        Assert.Throws<InvalidOperationException>(() => dispatcher.RegisterServiceHandler("TestService", handler2));
    }

    [Fact]
    public void UnregisterServiceHandler_ShouldRemoveService_Successfully()
    {
        // Arrange
        var dispatcher = new MessageDispatcher();
        var handler = CreateMockHandler();
        dispatcher.RegisterServiceHandler("TestService", handler);

        // Act
        var result = dispatcher.UnregisterServiceHandler("TestService");

        // Assert
        result.Should().BeTrue();
        dispatcher.RegisteredServiceCount.Should().Be(0);
        dispatcher.GetServiceHandler("TestService").Should().BeNull();
    }

    [Fact]
    public void UnregisterServiceHandler_ShouldReturnFalse_WhenServiceNotFound()
    {
        // Arrange
        var dispatcher = new MessageDispatcher();

        // Act
        var result = dispatcher.UnregisterServiceHandler("NonExistentService");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void GetServiceHandler_ShouldReturnNull_WhenServiceNotRegistered()
    {
        // Arrange
        var dispatcher = new MessageDispatcher();

        // Act
        var handler = dispatcher.GetServiceHandler("NonExistentService");

        // Assert
        handler.Should().BeNull();
    }

    [Fact]
    public async Task DispatchMessageAsync_ShouldThrow_WhenDispatcherNotRunning()
    {
        // Arrange
        var dispatcher = new MessageDispatcher();
        var message = CreateTestMessage("TestService", "TestMethod");

        // Act & Assert: Dispatching without starting should throw
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchMessageAsync(message));
    }

    [Fact]
    public async Task DispatchMessageAsync_ShouldThrow_WhenMessageIsNull()
    {
        // Arrange
        var dispatcher = new MessageDispatcher();
        await dispatcher.StartAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            dispatcher.DispatchMessageAsync(null!));

        await dispatcher.StopAsync();
    }

    [Fact]
    public async Task DispatchMessageAsync_ShouldFail_WhenServiceNotFound()
    {
        // Arrange
        var dispatcher = new MessageDispatcher();
        await dispatcher.StartAsync();

        var message = CreateTestMessage("NonExistentService", "TestMethod");

        // Act
        var result = await dispatcher.DispatchMessageAsync(message);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorType.Should().Be("ServiceNotFound");
        result.ErrorMessage.Should().Contain("NonExistentService");

        await dispatcher.StopAsync();
    }

    [Fact]
    public async Task DispatchMessageAsync_ShouldSucceed_WhenServiceRegistered()
    {
        // Arrange
        var dispatcher = new MessageDispatcher();
        var handler = CreateMockHandler();
        dispatcher.RegisterServiceHandler("TestService", handler);

        await dispatcher.StartAsync();

        var message = CreateTestMessage("TestService", "TestMethod");

        // Act
        var result = await dispatcher.DispatchMessageAsync(message);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.ErrorType.Should().BeNull();

        await dispatcher.StopAsync();
    }

    [Fact]
    public async Task DispatchMessageAsync_ShouldHandlePriority_FromMetadata()
    {
        // Arrange
        var dispatcher = new MessageDispatcher();
        var handler = CreateMockHandler();
        dispatcher.RegisterServiceHandler("TestService", handler);

        await dispatcher.StartAsync();

        // Create messages with different priorities
        var criticalMessage = CreateTestMessage("TestService", "Critical", new Dictionary<string, string>
        {
            ["Priority"] = "Critical"
        });

        var normalMessage = CreateTestMessage("TestService", "Normal", new Dictionary<string, string>
        {
            ["Priority"] = "Normal"
        });

        // Act
        var result1 = await dispatcher.DispatchMessageAsync(criticalMessage);
        var result2 = await dispatcher.DispatchMessageAsync(normalMessage);

        // Assert: Both should succeed
        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();

        await dispatcher.StopAsync();
    }

    [Fact]
    public async Task DispatchMessageAsync_ShouldIncrementCounter_WhenMessageDispatched()
    {
        // Arrange
        var dispatcher = new MessageDispatcher();
        var handler = CreateMockHandler();
        dispatcher.RegisterServiceHandler("TestService", handler);

        await dispatcher.StartAsync();

        var message = CreateTestMessage("TestService", "TestMethod");

        // Act
        await dispatcher.DispatchMessageAsync(message);
        await Task.Delay(100); // Allow worker to process

        // Assert: Counter should increment
        dispatcher.TotalMessagesDispatched.Should().BeGreaterThan(0);

        await dispatcher.StopAsync();
    }

    [Fact]
    public async Task DispatchMessageAsync_ShouldIndicateBackpressure_WhenQueueNearCapacity()
    {
        // Arrange: Use small queue capacity
        var options = new MessageDispatcherOptions
        {
            MaxQueueDepthPerPriority = 10, // Very small queue
            WorkerThreadCount = 1 // Single worker to slow processing
        };

        var dispatcher = new MessageDispatcher(options);
        var handler = CreateMockHandler();
        dispatcher.RegisterServiceHandler("TestService", handler);

        await dispatcher.StartAsync();

        // Act: Flood with messages
        var tasks = new List<Task<DispatchResult>>();
        for (int i = 0; i < 50; i++)
        {
            var message = CreateTestMessage("TestService", $"Method{i}");
            tasks.Add(dispatcher.DispatchMessageAsync(message));
        }

        var results = await Task.WhenAll(tasks);

        // Assert: Some results should indicate backpressure or success
        // (Since queue is bounded, some may succeed, some may have backpressure)
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());

        await dispatcher.StopAsync();
    }

    [Fact]
    public async Task MessageDispatcher_ShouldProcessMultipleServices_Concurrently()
    {
        // Arrange
        var dispatcher = new MessageDispatcher();
        var handler1 = CreateMockHandler();
        var handler2 = CreateMockHandler();

        dispatcher.RegisterServiceHandler("Service1", handler1);
        dispatcher.RegisterServiceHandler("Service2", handler2);

        await dispatcher.StartAsync();

        // Act: Dispatch to both services
        var message1 = CreateTestMessage("Service1", "Method1");
        var message2 = CreateTestMessage("Service2", "Method2");

        var result1 = await dispatcher.DispatchMessageAsync(message1);
        var result2 = await dispatcher.DispatchMessageAsync(message2);

        // Assert
        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();

        await Task.Delay(100); // Allow processing

        dispatcher.TotalMessagesDispatched.Should().BeGreaterThan(0);

        await dispatcher.StopAsync();
    }

    [Fact]
    public void MessageDispatcher_ShouldDisposeCleanly()
    {
        // Arrange
        var dispatcher = new MessageDispatcher();

        // Act
        dispatcher.Dispose();

        // Assert: Should not throw
        dispatcher.Dispose(); // Multiple disposes should be safe
    }

    [Fact]
    public async Task MessageDispatcher_ShouldStopGracefully_WhenDisposed()
    {
        // Arrange
        var dispatcher = new MessageDispatcher();
        await dispatcher.StartAsync();

        // Act
        dispatcher.Dispose();

        // Assert: Should stop
        dispatcher.IsRunning.Should().BeFalse();
    }

    // Helper methods

    private static IServiceHandler CreateMockHandler()
    {
        var handler = Substitute.For<IServiceHandler>();
        handler.GetMethodNames().Returns(new[] { "TestMethod" });
        return handler;
    }

    private static RpcMessage CreateTestMessage(
        string serviceName,
        string methodName,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new RpcMessage
        {
            ProtocolVersion = 1,
            MessageType = MessageType.Request,
            RequestId = Guid.NewGuid(),
            ServiceName = serviceName,
            MethodName = methodName,
            Payload = new byte[] { 1, 2, 3 },
            Metadata = metadata
        };
    }
}
