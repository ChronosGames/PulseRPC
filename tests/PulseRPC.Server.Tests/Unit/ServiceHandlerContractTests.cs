using FluentAssertions;
using Xunit;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Models;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace PulseRPC.Server.Tests.Unit;

/// <summary>
/// Contract tests for IServiceHandler interface (service-handler.yaml)
/// Tests the contract for service method invocation, method discovery, and error handling.
/// These tests MUST FAIL initially (TDD approach) until IServiceHandler and implementations exist.
/// </summary>
public class ServiceHandlerContractTests
{
    #region IServiceHandler.InvokeAsync Contract Tests (T010)

    [Fact]
    public async Task InvokeAsync_ShouldDeserializeParameters_AndCallMethod()
    {
        // Arrange: Service handler with Echo method
        var handler = CreateTestServiceHandler();
        var parameters = SerializeParameters(new object[] { "TestInput" });
        var context = CreateTestContext();

        // Act: Invoke Echo method
        var result = await handler.InvokeAsync("Echo", parameters, context);

        // Assert: Method called with deserialized parameters
        result.IsSuccess.Should().BeTrue();
        var returnValue = DeserializeResult<string>(result.Payload);
        returnValue.Should().Be("TestInput");
    }

    [Fact]
    public async Task InvokeAsync_ShouldHandleMultipleParameters()
    {
        // Arrange: Method with 3 parameters (int, string, bool)
        var handler = CreateCalculatorServiceHandler();
        var parameters = SerializeParameters(new object[] { 10, "operation", true });
        var context = CreateTestContext();

        // Act: Invoke method
        var result = await handler.InvokeAsync("Calculate", parameters, context);

        // Assert: All parameters deserialized correctly
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ShouldSerializeReturnValue_ToPayload()
    {
        // Arrange: Method returning complex object
        var handler = CreateTestServiceHandler();
        var parameters = SerializeParameters(new object[] { 123 });
        var context = CreateTestContext();

        // Act: Invoke method that returns complex object
        var result = await handler.InvokeAsync("GetUser", parameters, context);

        // Assert: Return value serialized to Payload
        result.IsSuccess.Should().BeTrue();
        result.Payload.Should().NotBeEmpty();

        var user = DeserializeResult<UserData>(result.Payload);
        user.Id.Should().Be(123);
        user.Name.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InvokeAsync_ShouldSupportVoidMethods()
    {
        // Arrange: Method with void return type
        var handler = CreateTestServiceHandler();
        var parameters = SerializeParameters(new object[] { "log message" });
        var context = CreateTestContext();

        // Act: Invoke void method
        var result = await handler.InvokeAsync("LogMessage", parameters, context);

        // Assert: Success with empty payload
        result.IsSuccess.Should().BeTrue();
        result.Payload.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_ShouldSupportAsyncMethods()
    {
        // Arrange: Async method (Task<T> return type)
        var handler = CreateTestServiceHandler();
        var parameters = SerializeParameters(new object[] { "async input" });
        var context = CreateTestContext();

        // Act: Invoke async method
        var result = await handler.InvokeAsync("EchoAsync", parameters, context);

        // Assert: Awaited correctly, result returned
        result.IsSuccess.Should().BeTrue();
        var returnValue = DeserializeResult<string>(result.Payload);
        returnValue.Should().Be("async input");
    }

    [Fact]
    public async Task InvokeAsync_ShouldRespectCancellationToken()
    {
        // Arrange: Long-running method with cancellation token
        var handler = CreateTestServiceHandler();
        var cts = new CancellationTokenSource();
        var context = CreateTestContext(cts.Token);
        var parameters = SerializeParameters(new object[] { 10000 }); // 10 second delay

        // Act: Start invocation, cancel after 500ms
        var task = handler.InvokeAsync("SlowMethod", parameters, context);
        await Task.Delay(500);
        cts.Cancel();

        var result = await task;

        // Assert: Cancelled gracefully
        result.IsSuccess.Should().BeFalse();
        result.ErrorType.Should().Contain("Cancelled");
    }

    [Fact]
    public async Task InvokeAsync_ShouldProvideRequestContext_ToServiceMethod()
    {
        // Arrange: Method that accesses RequestContext
        var handler = CreateTestServiceHandler();
        var requestId = Guid.NewGuid();
        var context = CreateTestContext(requestId: requestId);
        var parameters = SerializeParameters(Array.Empty<object>());

        // Act: Invoke method that reads context
        var result = await handler.InvokeAsync("GetRequestId", parameters, context);

        // Assert: Context accessible in method
        result.IsSuccess.Should().BeTrue();
        var returnedId = DeserializeResult<Guid>(result.Payload);
        returnedId.Should().Be(requestId);
    }

    #endregion

    #region IServiceHandler.GetMethodNames Contract Tests (T011)

    [Fact]
    public void GetMethodNames_ShouldReturnAllPublicMethods()
    {
        // Arrange: Service handler with multiple public methods
        var handler = CreateTestServiceHandler();

        // Act: Get method names
        var methodNames = handler.GetMethodNames();

        // Assert: All public methods listed
        methodNames.Should().Contain("Echo");
        methodNames.Should().Contain("EchoAsync");
        methodNames.Should().Contain("GetUser");
        methodNames.Should().Contain("LogMessage");
    }

    [Fact]
    public void GetMethodNames_ShouldExcludePrivateMethods()
    {
        // Arrange: Service with private helper methods
        var handler = CreateTestServiceHandler();

        // Act: Get method names
        var methodNames = handler.GetMethodNames();

        // Assert: Private methods not exposed
        methodNames.Should().NotContain("PrivateHelper");
        methodNames.Should().NotContain("InternalMethod");
    }

    [Fact]
    public void GetMethodNames_ShouldExcludeInheritedObjectMethods()
    {
        // Arrange: Service handler
        var handler = CreateTestServiceHandler();

        // Act: Get method names
        var methodNames = handler.GetMethodNames();

        // Assert: Object methods (ToString, GetHashCode, etc.) excluded
        methodNames.Should().NotContain("ToString");
        methodNames.Should().NotContain("GetHashCode");
        methodNames.Should().NotContain("Equals");
        methodNames.Should().NotContain("GetType");
    }

    [Fact]
    public void GetMethodNames_ShouldReturnReadOnlyCollection()
    {
        // Arrange: Service handler
        var handler = CreateTestServiceHandler();

        // Act: Get method names
        var methodNames = handler.GetMethodNames();

        // Assert: Collection is read-only (cannot modify)
        methodNames.Should().BeAssignableTo<IReadOnlyList<string>>();
    }

    [Fact]
    public void GetMethodNames_ShouldBeCached_ForPerformance()
    {
        // Arrange: Service handler
        var handler = CreateTestServiceHandler();

        // Act: Call multiple times
        var first = handler.GetMethodNames();
        var second = handler.GetMethodNames();

        // Assert: Same instance returned (cached)
        first.Should().BeSameAs(second);
    }

    #endregion

    #region IServiceHandler Error Handling Contract Tests (T012)

    [Fact]
    public async Task InvokeAsync_ShouldReturnError_WhenMethodNotFound()
    {
        // Arrange: Service handler
        var handler = CreateTestServiceHandler();
        var context = CreateTestContext();
        var parameters = SerializeParameters(Array.Empty<object>());

        // Act: Invoke non-existent method
        var result = await handler.InvokeAsync("NonExistentMethod", parameters, context);

        // Assert: MethodNotFound error
        result.IsSuccess.Should().BeFalse();
        result.ErrorType.Should().Contain("MethodNotFound");
        result.ErrorMessage.Should().Contain("NonExistentMethod");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnError_WhenDeserializationFails()
    {
        // Arrange: Invalid/corrupted parameter data
        var handler = CreateTestServiceHandler();
        var context = CreateTestContext();
        var corruptedData = new byte[] { 0xFF, 0xFE, 0xFD, 0xFC };

        // Act: Invoke with corrupted parameters
        var result = await handler.InvokeAsync("Echo", corruptedData, context);

        // Assert: Deserialization error
        result.IsSuccess.Should().BeFalse();
        result.ErrorType.Should().Contain("Deserialization");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnError_WhenParameterCountMismatch()
    {
        // Arrange: Method expects 2 parameters, providing 1
        var handler = CreateTestServiceHandler();
        var context = CreateTestContext();
        var parameters = SerializeParameters(new object[] { "only one param" });

        // Act: Invoke method requiring 2 parameters
        var result = await handler.InvokeAsync("MethodWith2Params", parameters, context);

        // Assert: Parameter mismatch error
        result.IsSuccess.Should().BeFalse();
        result.ErrorType.Should().Contain("ParameterCount");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnError_WhenParameterTypeMismatch()
    {
        // Arrange: Method expects int, providing string
        var handler = CreateTestServiceHandler();
        var context = CreateTestContext();
        var parameters = SerializeParameters(new object[] { "not an int" });

        // Act: Invoke method expecting int
        var result = await handler.InvokeAsync("MethodExpectingInt", parameters, context);

        // Assert: Type mismatch error
        result.IsSuccess.Should().BeFalse();
        result.ErrorType.Should().Contain("TypeMismatch");
    }

    [Fact]
    public async Task InvokeAsync_ShouldCaptureException_WhenMethodThrows()
    {
        // Arrange: Method that throws InvalidOperationException
        var handler = CreateFaultyServiceHandler();
        var context = CreateTestContext();
        var parameters = SerializeParameters(new object[] { "trigger" });

        // Act: Invoke failing method
        var result = await handler.InvokeAsync("ThrowException", parameters, context);

        // Assert: Exception captured in result
        result.IsSuccess.Should().BeFalse();
        result.ErrorType.Should().Contain("InvalidOperationException");
        result.ErrorMessage.Should().Contain("trigger");
        result.StackTrace.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InvokeAsync_ShouldHandleCancellation_Gracefully()
    {
        // Arrange: Already-cancelled token
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var context = CreateTestContext(cts.Token);
        var handler = CreateTestServiceHandler();
        var parameters = SerializeParameters(Array.Empty<object>());

        // Act: Invoke with cancelled token
        var result = await handler.InvokeAsync("Echo", parameters, context);

        // Assert: Cancellation handled without crash
        result.IsSuccess.Should().BeFalse();
        result.ErrorType.Should().Contain("Cancelled");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnError_WhenSerializationFails()
    {
        // Arrange: Method returning non-serializable object
        var handler = CreateTestServiceHandler();
        var context = CreateTestContext();
        var parameters = SerializeParameters(Array.Empty<object>());

        // Act: Invoke method returning non-serializable type
        var result = await handler.InvokeAsync("ReturnNonSerializable", parameters, context);

        // Assert: Serialization error
        result.IsSuccess.Should().BeFalse();
        result.ErrorType.Should().Contain("Serialization");
    }

    [Fact]
    public async Task InvokeAsync_ShouldIsolateErrors_BetweenInvocations()
    {
        // Arrange: Handler that failed once
        var handler = CreateTestServiceHandler();
        var context1 = CreateTestContext();
        var context2 = CreateTestContext();

        // Act: First invocation fails, second succeeds
        var failResult = await handler.InvokeAsync("NonExistentMethod", Array.Empty<byte>(), context1);
        var successResult = await handler.InvokeAsync("Echo", SerializeParameters(new[] { "test" }), context2);

        // Assert: First error doesn't affect second invocation
        failResult.IsSuccess.Should().BeFalse();
        successResult.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Helper Methods

    private IServiceHandler CreateTestServiceHandler()
    {
        throw new NotImplementedException("Requires IServiceHandler implementation");
    }

    private IServiceHandler CreateCalculatorServiceHandler()
    {
        throw new NotImplementedException();
    }

    private IServiceHandler CreateFaultyServiceHandler()
    {
        throw new NotImplementedException();
    }

    private RpcRequestContext CreateTestContext(CancellationToken? cancellationToken = null, Guid? requestId = null)
    {
        throw new NotImplementedException("Requires RpcRequestContext implementation");
    }

    private byte[] SerializeParameters(object[] parameters)
    {
        throw new NotImplementedException("Requires MemoryPack serialization");
    }

    private T DeserializeResult<T>(ReadOnlyMemory<byte> data)
    {
        throw new NotImplementedException("Requires MemoryPack deserialization");
    }

    #endregion

    #region Test Data Classes

    private class UserData
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    #endregion
}
