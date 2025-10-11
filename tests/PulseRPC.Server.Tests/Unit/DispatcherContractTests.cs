using FluentAssertions;
using Xunit;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Models;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace PulseRPC.Server.Tests.Unit;

/// <summary>
/// Contract tests for IMessageDispatcher interface (dispatcher-api.yaml)
/// Tests lifecycle management, message dispatching, service registration, and FIFO ordering.
/// These tests MUST FAIL initially (TDD approach) until IMessageDispatcher implementation exists.
/// </summary>
public class DispatcherContractTests
{
    #region IMessageDispatcher Lifecycle Contract Tests (T013)

    [Fact]
    public async Task StartAsync_ShouldInitializeDispatcher_Successfully()
    {
        // Arrange: New dispatcher instance
        var dispatcher = CreateMessageDispatcher();

        // Act: Start dispatcher
        await dispatcher.StartAsync(CancellationToken.None);

        // Assert: Dispatcher is running
        dispatcher.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_ShouldThrowException_IfAlreadyStarted()
    {
        // Arrange: Already-started dispatcher
        var dispatcher = CreateMessageDispatcher();
        await dispatcher.StartAsync(CancellationToken.None);

        // Act & Assert: Second start throws
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.StartAsync(CancellationToken.None)
        );
    }

    [Fact]
    public async Task StopAsync_ShouldShutdownGracefully()
    {
        // Arrange: Running dispatcher with pending messages
        var dispatcher = CreateMessageDispatcher();
        var handler = new TestServiceHandler();
        dispatcher.RegisterServiceHandler("TestService", handler);
        await dispatcher.StartAsync(CancellationToken.None);

        // Dispatch some messages
        var messages = Enumerable.Range(0, 10)
            .Select(_ => CreateTestMessage("TestService", "Echo"))
            .ToList();

        foreach (var msg in messages)
        {
            await dispatcher.DispatchMessageAsync(msg);
        }

        // Act: Stop dispatcher
        await dispatcher.StopAsync(CancellationToken.None);

        // Assert: All pending messages processed before shutdown
        dispatcher.IsRunning.Should().BeFalse();
        handler.ProcessedCount.Should().Be(10);
    }

    [Fact]
    public async Task StopAsync_ShouldRespectCancellationToken()
    {
        // Arrange: Dispatcher with long-running tasks
        var dispatcher = CreateMessageDispatcher();
        var slowHandler = new SlowServiceHandler(delayMs: 5000);
        dispatcher.RegisterServiceHandler("SlowService", slowHandler);
        await dispatcher.StartAsync(CancellationToken.None);

        // Dispatch slow messages
        for (int i = 0; i < 10; i++)
        {
            await dispatcher.DispatchMessageAsync(CreateTestMessage("SlowService", "SlowMethod"));
        }

        // Act: Stop with short timeout
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await dispatcher.StopAsync(cts.Token);

        // Assert: Stopped before all messages processed
        dispatcher.IsRunning.Should().BeFalse();
        slowHandler.ProcessedCount.Should().BeLessThan(10);
    }

    [Fact]
    public async Task StartAsync_And_StopAsync_ShouldBeIdempotent()
    {
        // Arrange: Dispatcher
        var dispatcher = CreateMessageDispatcher();

        // Act: Start-Stop-Start-Stop cycle
        await dispatcher.StartAsync(CancellationToken.None);
        await dispatcher.StopAsync(CancellationToken.None);
        await dispatcher.StartAsync(CancellationToken.None);
        await dispatcher.StopAsync(CancellationToken.None);

        // Assert: No errors, final state is stopped
        dispatcher.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task DispatchMessageAsync_ShouldRejectMessages_WhenNotStarted()
    {
        // Arrange: Stopped dispatcher
        var dispatcher = CreateMessageDispatcher();
        var message = CreateTestMessage("TestService", "Echo");

        // Act & Assert: Dispatch before start throws
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchMessageAsync(message)
        );
    }

    #endregion

    #region IMessageDispatcher.DispatchMessageAsync Contract Tests (T014)

    [Fact]
    public async Task DispatchMessageAsync_ShouldRouteToCorrectHandler()
    {
        // Arrange: Dispatcher with multiple services
        var dispatcher = CreateMessageDispatcher();
        var handler1 = new TestServiceHandler();
        var handler2 = new TestServiceHandler();
        dispatcher.RegisterServiceHandler("Service1", handler1);
        dispatcher.RegisterServiceHandler("Service2", handler2);
        await dispatcher.StartAsync(CancellationToken.None);

        // Act: Dispatch messages to different services
        await dispatcher.DispatchMessageAsync(CreateTestMessage("Service1", "Method1"));
        await dispatcher.DispatchMessageAsync(CreateTestMessage("Service2", "Method2"));

        await Task.Delay(100); // Allow processing

        // Assert: Routed correctly
        handler1.ProcessedCount.Should().Be(1);
        handler2.ProcessedCount.Should().Be(1);
    }

    [Fact]
    public async Task DispatchMessageAsync_ShouldMaintainFIFOOrder_PerConnection()
    {
        // Arrange: Dispatcher with order-tracking handler
        var dispatcher = CreateMessageDispatcher();
        var handler = new OrderTrackingHandler();
        dispatcher.RegisterServiceHandler("TestService", handler);
        await dispatcher.StartAsync(CancellationToken.None);

        var connectionId = "conn1";
        var messages = new[]
        {
            CreateTestMessage("TestService", "First", connectionId),
            CreateTestMessage("TestService", "Second", connectionId),
            CreateTestMessage("TestService", "Third", connectionId)
        };

        // Act: Dispatch in order
        foreach (var msg in messages)
        {
            await dispatcher.DispatchMessageAsync(msg);
        }

        await Task.Delay(100); // Allow processing

        // Assert: Processed in FIFO order
        handler.ExecutionOrder.Should().Equal(new[] { "First", "Second", "Third" });
    }

    [Fact]
    public async Task DispatchMessageAsync_ShouldAllowParallelism_AcrossConnections()
    {
        // Arrange: Dispatcher with slow handler
        var dispatcher = CreateMessageDispatcher();
        var handler = new ConcurrentTrackingHandler();
        dispatcher.RegisterServiceHandler("TestService", handler);
        await dispatcher.StartAsync(CancellationToken.None);

        // Act: Dispatch from 10 different connections simultaneously
        var tasks = Enumerable.Range(0, 10)
            .Select(i => dispatcher.DispatchMessageAsync(
                CreateTestMessage("TestService", "Method", $"conn{i}")
            ))
            .ToArray();

        await Task.WhenAll(tasks);
        await Task.Delay(100);

        // Assert: Multiple messages processed concurrently
        handler.MaxConcurrency.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task DispatchMessageAsync_ShouldApplyBackpressure_WhenQueueFull()
    {
        // Arrange: Dispatcher with limited capacity
        var options = new DispatcherOptions { MaxQueueDepth = 10 };
        var dispatcher = CreateMessageDispatcher(options);
        var slowHandler = new SlowServiceHandler(delayMs: 1000);
        dispatcher.RegisterServiceHandler("SlowService", slowHandler);
        await dispatcher.StartAsync(CancellationToken.None);

        // Act: Flood with 100 messages
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => dispatcher.DispatchMessageAsync(
                CreateTestMessage("SlowService", "SlowMethod")
            ))
            .ToArray();

        // Assert: Some tasks should wait (backpressure applied)
        var startTime = DateTime.UtcNow;
        await Task.WhenAll(tasks);
        var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;

        duration.Should().BeGreaterThan(5000); // Backpressure caused delay
    }

    [Fact]
    public async Task DispatchMessageAsync_ShouldReturnError_ForUnregisteredService()
    {
        // Arrange: Dispatcher without registered services
        var dispatcher = CreateMessageDispatcher();
        await dispatcher.StartAsync(CancellationToken.None);

        var message = CreateTestMessage("UnknownService", "Method");

        // Act: Dispatch to unknown service
        var result = await dispatcher.DispatchMessageAsync(message);

        // Assert: Service not found error
        result.IsSuccess.Should().BeFalse();
        result.ErrorType.Should().Contain("ServiceNotFound");
    }

    [Fact]
    public async Task DispatchMessageAsync_ShouldPreserveMessageMetadata()
    {
        // Arrange: Dispatcher with metadata-aware handler
        var dispatcher = CreateMessageDispatcher();
        var handler = new MetadataCapturingHandler();
        dispatcher.RegisterServiceHandler("TestService", handler);
        await dispatcher.StartAsync(CancellationToken.None);

        var message = CreateTestMessage("TestService", "Method");
        message.Metadata["TraceId"] = "trace-123";
        message.Metadata["UserId"] = "user-456";

        // Act: Dispatch message
        await dispatcher.DispatchMessageAsync(message);
        await Task.Delay(100);

        // Assert: Metadata preserved in handler
        handler.CapturedMetadata.Should().ContainKey("TraceId");
        handler.CapturedMetadata["TraceId"].Should().Be("trace-123");
    }

    [Fact]
    public async Task DispatchMessageAsync_ShouldRespectPriority_Ordering()
    {
        // Arrange: Dispatcher with priority queue
        var dispatcher = CreateMessageDispatcher();
        var handler = new PriorityTrackingHandler();
        dispatcher.RegisterServiceHandler("TestService", handler);
        await dispatcher.StartAsync(CancellationToken.None);

        // Act: Submit in reverse priority order
        await dispatcher.DispatchMessageAsync(CreateTestMessage("TestService", "Low", priority: MessagePriority.Low));
        await dispatcher.DispatchMessageAsync(CreateTestMessage("TestService", "Normal", priority: MessagePriority.Normal));
        await dispatcher.DispatchMessageAsync(CreateTestMessage("TestService", "High", priority: MessagePriority.High));
        await dispatcher.DispatchMessageAsync(CreateTestMessage("TestService", "Critical", priority: MessagePriority.Critical));

        await Task.Delay(200);

        // Assert: Critical processed first
        handler.ExecutionOrder[0].Should().Be("Critical");
        handler.ExecutionOrder[1].Should().Be("High");
    }

    #endregion

    #region IMessageDispatcher.RegisterServiceHandler Contract Tests (T015)

    [Fact]
    public void RegisterServiceHandler_ShouldRegisterService_Successfully()
    {
        // Arrange: Dispatcher and handler
        var dispatcher = CreateMessageDispatcher();
        var handler = new TestServiceHandler();

        // Act: Register service
        dispatcher.RegisterServiceHandler("TestService", handler);

        // Assert: Service registered (can retrieve handler)
        var registeredHandler = dispatcher.GetServiceHandler("TestService");
        registeredHandler.Should().BeSameAs(handler);
    }

    [Fact]
    public void RegisterServiceHandler_ShouldThrowException_ForDuplicateServiceName()
    {
        // Arrange: Dispatcher with registered service
        var dispatcher = CreateMessageDispatcher();
        var handler1 = new TestServiceHandler();
        var handler2 = new TestServiceHandler();

        dispatcher.RegisterServiceHandler("TestService", handler1);

        // Act & Assert: Duplicate registration throws
        Assert.Throws<InvalidOperationException>(
            () => dispatcher.RegisterServiceHandler("TestService", handler2)
        );
    }

    [Fact]
    public void RegisterServiceHandler_ShouldBeThreadSafe()
    {
        // Arrange: Dispatcher
        var dispatcher = CreateMessageDispatcher();

        // Act: Register multiple services concurrently
        var tasks = Enumerable.Range(0, 100)
            .Select(i => Task.Run(() =>
                dispatcher.RegisterServiceHandler($"Service{i}", new TestServiceHandler())
            ))
            .ToArray();

        Task.WaitAll(tasks);

        // Assert: All services registered successfully
        for (int i = 0; i < 100; i++)
        {
            dispatcher.GetServiceHandler($"Service{i}").Should().NotBeNull();
        }
    }

    [Fact]
    public void RegisterServiceHandler_ShouldThrowException_ForNullServiceName()
    {
        // Arrange: Dispatcher
        var dispatcher = CreateMessageDispatcher();
        var handler = new TestServiceHandler();

        // Act & Assert: Null service name throws
        Assert.Throws<ArgumentNullException>(
            () => dispatcher.RegisterServiceHandler(null!, handler)
        );
    }

    [Fact]
    public void RegisterServiceHandler_ShouldThrowException_ForNullHandler()
    {
        // Arrange: Dispatcher
        var dispatcher = CreateMessageDispatcher();

        // Act & Assert: Null handler throws
        Assert.Throws<ArgumentNullException>(
            () => dispatcher.RegisterServiceHandler("TestService", null!)
        );
    }

    [Fact]
    public void RegisterServiceHandler_ShouldThrowException_ForEmptyServiceName()
    {
        // Arrange: Dispatcher
        var dispatcher = CreateMessageDispatcher();
        var handler = new TestServiceHandler();

        // Act & Assert: Empty service name throws
        Assert.Throws<ArgumentException>(
            () => dispatcher.RegisterServiceHandler("", handler)
        );
        Assert.Throws<ArgumentException>(
            () => dispatcher.RegisterServiceHandler("   ", handler)
        );
    }

    [Fact]
    public void RegisterServiceHandler_ShouldAllowRegistration_BeforeStart()
    {
        // Arrange: New dispatcher (not started)
        var dispatcher = CreateMessageDispatcher();
        var handler = new TestServiceHandler();

        // Act: Register before starting
        dispatcher.RegisterServiceHandler("TestService", handler);

        // Assert: Registration succeeds
        dispatcher.GetServiceHandler("TestService").Should().BeSameAs(handler);
    }

    [Fact]
    public async Task RegisterServiceHandler_ShouldAllowRegistration_AfterStart()
    {
        // Arrange: Running dispatcher
        var dispatcher = CreateMessageDispatcher();
        await dispatcher.StartAsync(CancellationToken.None);

        var handler = new TestServiceHandler();

        // Act: Register after starting
        dispatcher.RegisterServiceHandler("TestService", handler);

        // Assert: Registration succeeds, handler immediately available
        await dispatcher.DispatchMessageAsync(CreateTestMessage("TestService", "Echo"));
        await Task.Delay(100);

        handler.ProcessedCount.Should().Be(1);
    }

    [Fact]
    public void RegisterServiceHandler_ShouldSupportMultipleServices()
    {
        // Arrange: Dispatcher
        var dispatcher = CreateMessageDispatcher();

        // Act: Register 1000 services
        for (int i = 0; i < 1000; i++)
        {
            dispatcher.RegisterServiceHandler($"Service{i}", new TestServiceHandler());
        }

        // Assert: All services retrievable
        for (int i = 0; i < 1000; i++)
        {
            dispatcher.GetServiceHandler($"Service{i}").Should().NotBeNull();
        }
    }

    #endregion

    #region Helper Methods

    private IMessageDispatcher CreateMessageDispatcher(DispatcherOptions? options = null)
    {
        throw new NotImplementedException("Requires IMessageDispatcher implementation");
    }

    private RpcMessage CreateTestMessage(string serviceName, string methodName,
        string? connectionId = null, MessagePriority priority = MessagePriority.Normal)
    {
        return new RpcMessage
        {
            RequestId = Guid.NewGuid(),
            ServiceName = serviceName,
            MethodName = methodName,
            ProtocolVersion = 1,
            MessageType = MessageType.Request,
            Payload = Array.Empty<byte>(),
            Metadata = new Dictionary<string, string>(),
            ReceivedAt = DateTime.UtcNow.Ticks
        };
    }

    #endregion

    #region Test Helper Classes

    private class TestServiceHandler : IServiceHandler
    {
        public int ProcessedCount { get; private set; }

        public Task<InvocationResult> InvokeAsync(string methodName, ReadOnlyMemory<byte> parameters, RpcRequestContext context)
        {
            ProcessedCount++;
            return Task.FromResult(new InvocationResult { IsSuccess = true });
        }

        public IReadOnlyList<string> GetMethodNames() => Array.Empty<string>();
    }

    private class SlowServiceHandler : IServiceHandler
    {
        private readonly int _delayMs;
        public int ProcessedCount { get; private set; }

        public SlowServiceHandler(int delayMs) => _delayMs = delayMs;

        public async Task<InvocationResult> InvokeAsync(string methodName, ReadOnlyMemory<byte> parameters, RpcRequestContext context)
        {
            await Task.Delay(_delayMs);
            ProcessedCount++;
            return new InvocationResult { IsSuccess = true };
        }

        public IReadOnlyList<string> GetMethodNames() => Array.Empty<string>();
    }

    private class OrderTrackingHandler : IServiceHandler
    {
        public List<string> ExecutionOrder { get; } = new();

        public Task<InvocationResult> InvokeAsync(string methodName, ReadOnlyMemory<byte> parameters, RpcRequestContext context)
        {
            ExecutionOrder.Add(methodName);
            return Task.FromResult(new InvocationResult { IsSuccess = true });
        }

        public IReadOnlyList<string> GetMethodNames() => Array.Empty<string>();
    }

    private class ConcurrentTrackingHandler : IServiceHandler
    {
        private int _currentConcurrency = 0;
        public int MaxConcurrency { get; private set; } = 0;

        public async Task<InvocationResult> InvokeAsync(string methodName, ReadOnlyMemory<byte> parameters, RpcRequestContext context)
        {
            var current = Interlocked.Increment(ref _currentConcurrency);
            if (current > MaxConcurrency)
            {
                MaxConcurrency = current;
            }

            await Task.Delay(50);
            Interlocked.Decrement(ref _currentConcurrency);

            return new InvocationResult { IsSuccess = true };
        }

        public IReadOnlyList<string> GetMethodNames() => Array.Empty<string>();
    }

    private class MetadataCapturingHandler : IServiceHandler
    {
        public Dictionary<string, string> CapturedMetadata { get; } = new();

        public Task<InvocationResult> InvokeAsync(string methodName, ReadOnlyMemory<byte> parameters, RpcRequestContext context)
        {
            foreach (var kvp in context.Metadata)
            {
                CapturedMetadata[kvp.Key] = kvp.Value;
            }
            return Task.FromResult(new InvocationResult { IsSuccess = true });
        }

        public IReadOnlyList<string> GetMethodNames() => Array.Empty<string>();
    }

    private class PriorityTrackingHandler : IServiceHandler
    {
        public List<string> ExecutionOrder { get; } = new();

        public Task<InvocationResult> InvokeAsync(string methodName, ReadOnlyMemory<byte> parameters, RpcRequestContext context)
        {
            ExecutionOrder.Add(methodName);
            return Task.FromResult(new InvocationResult { IsSuccess = true });
        }

        public IReadOnlyList<string> GetMethodNames() => Array.Empty<string>();
    }

    #endregion
}

public class DispatcherOptions
{
    public int MaxQueueDepth { get; set; } = 1000;
}

public class InvocationResult
{
    public bool IsSuccess { get; set; }
    public string ErrorType { get; set; } = string.Empty;
    public bool IsBackpressureApplied { get; set; }
}
