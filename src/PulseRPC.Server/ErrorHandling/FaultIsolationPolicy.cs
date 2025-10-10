using Microsoft.Extensions.Logging;
using PulseRPC.Server.Models;
using PulseRPC.Server.Response;

namespace PulseRPC.Server.ErrorHandling;

/// <summary>
/// Implements fault isolation policy - exception boundary at service invocation.
/// Ensures one bad service doesn't crash the entire server.
/// </summary>
public sealed class FaultIsolationPolicy
{
    private readonly ILogger<FaultIsolationPolicy> _logger;
    private readonly ResponseProcessor _responseProcessor;

    public FaultIsolationPolicy(
        ILogger<FaultIsolationPolicy>? logger = null,
        ResponseProcessor? responseProcessor = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FaultIsolationPolicy>.Instance;
        _responseProcessor = responseProcessor ?? new ResponseProcessor();
    }

    /// <summary>
    /// Executes a service method with fault isolation.
    /// Catches all exceptions and converts them to error responses.
    /// </summary>
    public async Task<ResponseEnvelope> ExecuteWithIsolationAsync<T>(
        Func<Task<T>> serviceMethod,
        RpcRequestContext context)
    {
        try
        {
            // Execute service method
            var result = await serviceMethod();

            // Create success response
            return _responseProcessor.CreateSuccessResponse(
                context.RequestId,
                result,
                context.StartTimestamp
            );
        }
        catch (OperationCanceledException ex) when (context.CancellationToken.IsCancellationRequested)
        {
            // Timeout or disconnect
            _logger.LogWarning(ex,
                "Service method cancelled: {ServiceName}.{MethodName} (RequestId: {RequestId}, Duration: {Duration}ms)",
                context.ServiceName,
                context.MethodName,
                context.RequestId,
                context.GetElapsedTime().TotalMilliseconds);

            return ErrorResponseFactory.CreateTimeoutError(
                context.RequestId,
                context.ServiceName,
                context.MethodName,
                context.GetElapsedTime().TotalMilliseconds
            );
        }
        catch (Exception ex)
        {
            // Service method threw exception
            _logger.LogError(ex,
                "Service method threw exception: {ServiceName}.{MethodName} (RequestId: {RequestId}, Duration: {Duration}ms)",
                context.ServiceName,
                context.MethodName,
                context.RequestId,
                context.GetElapsedTime().TotalMilliseconds);

            // Create error response with sanitized exception details
            return _responseProcessor.CreateErrorResponse(
                context.RequestId,
                ex,
                context.StartTimestamp
            );
        }
    }

    /// <summary>
    /// Executes a void service method with fault isolation.
    /// </summary>
    public async Task<ResponseEnvelope> ExecuteWithIsolationAsync(
        Func<Task> serviceMethod,
        RpcRequestContext context)
    {
        try
        {
            // Execute service method
            await serviceMethod();

            // Create success response with empty payload
            return _responseProcessor.CreateSuccessResponse(
                context.RequestId,
                null,
                context.StartTimestamp
            );
        }
        catch (OperationCanceledException ex) when (context.CancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Service method cancelled: {ServiceName}.{MethodName} (RequestId: {RequestId})",
                context.ServiceName,
                context.MethodName,
                context.RequestId);

            return ErrorResponseFactory.CreateTimeoutError(
                context.RequestId,
                context.ServiceName,
                context.MethodName,
                context.GetElapsedTime().TotalMilliseconds
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Service method threw exception: {ServiceName}.{MethodName} (RequestId: {RequestId})",
                context.ServiceName,
                context.MethodName,
                context.RequestId);

            return _responseProcessor.CreateErrorResponse(
                context.RequestId,
                ex,
                context.StartTimestamp
            );
        }
    }
}
