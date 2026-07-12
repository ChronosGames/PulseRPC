using Microsoft.Extensions.Logging;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Services;
using PulseRPC.Server.Health; using PulseRPC.Server.Processing; using PulseRPC.Server.Channels; using PulseRPC.Server.Services.Scheduling;

namespace PulseRPC.Server.Extensions;

/// <summary>
/// Structured logging extensions for the message pipeline.
/// Provides consistent, context-rich logging without PII.
/// </summary>
public static class PipelineLogging
{
    // Log event IDs for filtering and alerting
    private static class EventIds
    {
        public const int MessageReceived = 1000;
        public const int MessageProcessed = 1001;
        public const int MessageFailed = 1002;
        public const int ServiceInvoked = 1010;
        public const int ServiceTimeout = 1011;
        public const int ServiceError = 1012;
        public const int ResponseSent = 1020;
        public const int ResponseFailed = 1021;
        public const int ConnectionAccepted = 2000;
        public const int ConnectionClosed = 2001;
        public const int BackpressureActivated = 3000;
        public const int BackpressureReleased = 3001;
        public const int PipelineStarted = 4000;
        public const int PipelineStopped = 4001;
    }

    /// <summary>
    /// Logs message received event.
    /// </summary>
    public static void LogMessageReceived(
        this ILogger logger,
        RpcMessage message,
        ServerConnection connection)
    {
        logger.LogDebug(
            EventIds.MessageReceived,
            "Message received: {ServiceName}.{MethodName} from {ConnectionId} (RequestId: {RequestId}, Size: {PayloadSize} bytes)",
            message.ServiceName,
            message.MethodName,
            connection.ConnectionId,
            message.RequestId,
            message.Payload.Length);
    }

    /// <summary>
    /// Logs successful message processing.
    /// </summary>
    public static void LogMessageProcessed(
        this ILogger logger,
        Guid requestId,
        string serviceName,
        string methodName,
        double durationMs)
    {
        logger.LogInformation(
            EventIds.MessageProcessed,
            "Message processed: {ServiceName}.{MethodName} (RequestId: {RequestId}, Duration: {Duration}ms)",
            serviceName,
            methodName,
            requestId,
            durationMs);
    }

    /// <summary>
    /// Logs message processing failure.
    /// </summary>
    public static void LogMessageFailed(
        this ILogger logger,
        Guid requestId,
        string serviceName,
        string methodName,
        Exception exception,
        double durationMs)
    {
        logger.LogError(
            EventIds.MessageFailed,
            exception,
            "Message processing failed: {ServiceName}.{MethodName} (RequestId: {RequestId}, Duration: {Duration}ms, Error: {ErrorType})",
            serviceName,
            methodName,
            requestId,
            durationMs,
            exception.GetType().Name);
    }

    /// <summary>
    /// Logs service method invocation.
    /// </summary>
    public static void LogServiceInvoked(
        this ILogger logger,
        string serviceName,
        string methodName,
        Guid requestId)
    {
        logger.LogDebug(
            EventIds.ServiceInvoked,
            "Invoking service method: {ServiceName}.{MethodName} (RequestId: {RequestId})",
            serviceName,
            methodName,
            requestId);
    }

    /// <summary>
    /// Logs service timeout.
    /// </summary>
    public static void LogServiceTimeout(
        this ILogger logger,
        string serviceName,
        string methodName,
        Guid requestId,
        double timeoutMs)
    {
        logger.LogWarning(
            EventIds.ServiceTimeout,
            "Service method timed out: {ServiceName}.{MethodName} (RequestId: {RequestId}, Timeout: {Timeout}ms)",
            serviceName,
            methodName,
            requestId,
            timeoutMs);
    }

    /// <summary>
    /// Logs service error.
    /// </summary>
    public static void LogServiceError(
        this ILogger logger,
        string serviceName,
        string methodName,
        Guid requestId,
        Exception exception)
    {
        logger.LogError(
            EventIds.ServiceError,
            exception,
            "Service method threw exception: {ServiceName}.{MethodName} (RequestId: {RequestId}, ErrorType: {ErrorType})",
            serviceName,
            methodName,
            requestId,
            exception.GetType().Name);
    }

    /// <summary>
    /// Logs response sent.
    /// </summary>
    public static void LogResponseSent(
        this ILogger logger,
        Guid requestId,
        string connectionId,
        int responseSize,
        bool isSuccess)
    {
        logger.LogDebug(
            EventIds.ResponseSent,
            "Response sent: RequestId {RequestId} to {ConnectionId} (Size: {Size} bytes, Success: {Success})",
            requestId,
            connectionId,
            responseSize,
            isSuccess);
    }

    /// <summary>
    /// Logs response transmission failure.
    /// </summary>
    public static void LogResponseFailed(
        this ILogger logger,
        Guid requestId,
        string connectionId,
        Exception exception)
    {
        logger.LogError(
            EventIds.ResponseFailed,
            exception,
            "Failed to send response: RequestId {RequestId} to {ConnectionId}",
            requestId,
            connectionId);
    }

    /// <summary>
    /// Logs connection accepted.
    /// </summary>
    public static void LogConnectionAccepted(
        this ILogger logger,
        ServerConnection connection)
    {
        logger.LogInformation(
            EventIds.ConnectionAccepted,
            "Connection accepted: {ConnectionId} from {ClientAddress} ({Transport})",
            connection.ConnectionId,
            connection.ClientAddress?.ToString() ?? "unknown",
            connection.TransportProtocol);
    }

    /// <summary>
    /// Logs connection closed.
    /// </summary>
    public static void LogConnectionClosed(
        this ILogger logger,
        ServerConnection connection,
        string reason)
    {
        logger.LogInformation(
            EventIds.ConnectionClosed,
            "Connection closed: {ConnectionId} (Reason: {Reason}, Duration: {Duration}s, MessagesSent: {Sent}, MessagesReceived: {Received})",
            connection.ConnectionId,
            reason,
            connection.GetDuration().TotalSeconds,
            connection.MessagesSent,
            connection.MessagesReceived);
    }

    /// <summary>
    /// Logs backpressure activation.
    /// </summary>
    public static void LogBackpressureActivated(
        this ILogger logger,
        BackpressureLevel level,
        double queueSaturation)
    {
        logger.LogWarning(
            EventIds.BackpressureActivated,
            "Backpressure activated: Level {Level} (Queue saturation: {Saturation:P})",
            level,
            queueSaturation);
    }

    /// <summary>
    /// Logs backpressure release.
    /// </summary>
    public static void LogBackpressureReleased(
        this ILogger logger,
        double queueSaturation)
    {
        logger.LogInformation(
            EventIds.BackpressureReleased,
            "Backpressure released (Queue saturation: {Saturation:P})",
            queueSaturation);
    }

    /// <summary>
    /// Logs pipeline started.
    /// </summary>
    public static void LogPipelineStarted(
        this ILogger logger,
        int maxConcurrentRequests,
        TimeSpan defaultTimeout)
    {
        logger.LogInformation(
            EventIds.PipelineStarted,
            "Message pipeline started (MaxConcurrent: {MaxConcurrent}, DefaultTimeout: {Timeout}ms)",
            maxConcurrentRequests,
            defaultTimeout.TotalMilliseconds);
    }

    /// <summary>
    /// Logs pipeline stopped.
    /// </summary>
    public static void LogPipelineStopped(
        this ILogger logger,
        long totalProcessed,
        long totalErrors)
    {
        logger.LogInformation(
            EventIds.PipelineStopped,
            "Message pipeline stopped (TotalProcessed: {Total}, TotalErrors: {Errors}, ErrorRate: {ErrorRate:P})",
            totalProcessed,
            totalErrors,
            totalProcessed > 0 ? (double)totalErrors / totalProcessed : 0);
    }

    /// <summary>
    /// Creates a structured log scope with request context.
    /// </summary>
    public static IDisposable? BeginRequestScope(
        this ILogger logger,
        IPulseContext context)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["RequestId"] = context.RequestId,
            ["ServiceName"] = context.ServiceName,
            ["MethodName"] = context.MethodName,
            ["ConnectionId"] = context.ConnectionId ?? "unknown",
            ["UserId"] = context.UserId ?? "anonymous"
        });
    }
}
