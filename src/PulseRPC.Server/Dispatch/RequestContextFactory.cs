using System.Diagnostics;
using PulseRPC.Server.Models;

namespace PulseRPC.Server.Dispatch;

/// <summary>
/// Factory for creating RequestContext instances.
/// Provides validation and testing utilities.
/// </summary>
public sealed class RequestContextFactory
{
    public RequestContextFactory()
    {
    }

    /// <summary>
    /// Creates a RequestContext from an RpcMessage and ServerConnection.
    /// </summary>
    public RpcRequestContext Create(
        RpcMessage message,
        ServerConnection connection,
        TimeSpan timeout,
        Activity? activity = null)
    {
        return RpcRequestContext.Create(message, connection, timeout, activity);
    }

    /// <summary>
    /// Creates a RequestContext with custom metadata.
    /// </summary>
    public RpcRequestContext CreateWithMetadata(
        Guid requestId,
        string serviceName,
        string methodName,
        ServerConnection connection,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string>? metadata = null,
        Activity? activity = null)
    {
        var message = new RpcMessage
        {
            RequestId = requestId,
            ServiceName = serviceName,
            MethodName = methodName,
            Payload = ReadOnlyMemory<byte>.Empty,
            Metadata = metadata,
            ReceivedAt = Stopwatch.GetTimestamp()
        };

        return Create(message, connection, timeout, activity);
    }

    /// <summary>
    /// Validates a request context.
    /// </summary>
    public bool IsValid(RpcRequestContext context, out string? errorMessage)
    {
        if (context.RequestId == Guid.Empty)
        {
            errorMessage = "RequestId cannot be empty";
            return false;
        }

        if (string.IsNullOrWhiteSpace(context.ServiceName))
        {
            errorMessage = "ServiceName cannot be empty";
            return false;
        }

        if (string.IsNullOrWhiteSpace(context.MethodName))
        {
            errorMessage = "MethodName cannot be empty";
            return false;
        }

        if (string.IsNullOrWhiteSpace(context.ConnectionId))
        {
            errorMessage = "ConnectionId cannot be empty";
            return false;
        }

        errorMessage = null;
        return true;
    }

    /// <summary>
    /// Creates a test context for unit testing.
    /// </summary>
    public static RpcRequestContext CreateTestContext(
        string serviceName = "TestService",
        string methodName = "TestMethod",
        TimeSpan? timeout = null)
    {
        var message = new RpcMessage
        {
            RequestId = Guid.NewGuid(),
            ServiceName = serviceName,
            MethodName = methodName,
            Payload = ReadOnlyMemory<byte>.Empty,
            ReceivedAt = Stopwatch.GetTimestamp()
        };

        var connection = new ServerConnection(
            Guid.NewGuid().ToString(),
            null,
            TransportType.TCP
        );

        return RpcRequestContext.Create(
            message,
            connection,
            timeout ?? TimeSpan.FromSeconds(30),
            null
        );
    }
}
