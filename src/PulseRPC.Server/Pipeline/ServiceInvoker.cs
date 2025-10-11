using PulseRPC.Server.Abstractions;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Server.Pipeline;

/// <summary>
/// Wraps CompiledServiceInvoker with timeout enforcement, exception handling, and context management.
/// Handles FR-014 to FR-020: timeout enforcement, exception isolation, context propagation.
/// </summary>
public sealed class ServiceInvoker : IServiceHandler
{
    private readonly CompiledServiceInvoker _compiledInvoker;
    private readonly TimeSpan _defaultTimeout;

    public ServiceInvoker(object serviceInstance, TimeSpan? defaultTimeout = null)
    {
        if (serviceInstance == null)
        {
            throw new ArgumentNullException(nameof(serviceInstance));
        }

        _compiledInvoker = new CompiledServiceInvoker(serviceInstance);
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Invokes a service method with timeout enforcement and exception isolation.
    /// </summary>
    public async Task<InvocationResult> InvokeAsync(
        string methodName,
        ReadOnlyMemory<byte> parameters,
        IRequestContext context)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            throw new ArgumentException("Method name cannot be null or empty", nameof(methodName));
        }

        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var startTime = Stopwatch.GetTimestamp();

        try
        {
            // Check if already cancelled
            if (context.CancellationToken.IsCancellationRequested)
            {
                var elapsed = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
                return InvocationResult.Failure(
                    "OperationCancelled",
                    "Request was cancelled before invocation",
                    null,
                    elapsed);
            }

            // Create timeout cancellation source
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            timeoutCts.CancelAfter(_defaultTimeout);

            // Create wrapped context with timeout token
            var wrappedContext = new TimeoutWrappedContext(context, timeoutCts.Token);

            try
            {
                // Invoke with timeout
                var invocationTask = _compiledInvoker.InvokeAsync(methodName, parameters, wrappedContext);
                var result = await invocationTask.ConfigureAwait(false);

                return result;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !context.CancellationToken.IsCancellationRequested)
            {
                // Timeout occurred
                var elapsed = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
                return InvocationResult.Failure(
                    "TimeoutException",
                    $"Method '{methodName}' exceeded timeout of {_defaultTimeout.TotalSeconds}s",
                    null,
                    elapsed);
            }
            catch (OperationCanceledException)
            {
                // Request cancelled
                var elapsed = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
                return InvocationResult.Failure(
                    "OperationCancelled",
                    "Request was cancelled during invocation",
                    null,
                    elapsed);
            }
            catch (Exception ex)
            {
                // Unexpected error during invocation
                var elapsed = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
                return InvocationResult.Failure(
                    ex.GetType().FullName ?? "Exception",
                    ex.Message,
                    SanitizeStackTrace(ex.StackTrace),
                    elapsed);
            }
        }
        catch (Exception ex)
        {
            // Outer exception handler
            var elapsed = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            return InvocationResult.Failure(
                "InvocationWrapperError",
                $"Error in ServiceInvoker: {ex.Message}",
                SanitizeStackTrace(ex.StackTrace),
                elapsed);
        }
    }

    /// <summary>
    /// Gets the list of available method names.
    /// </summary>
    public IReadOnlyList<string> GetMethodNames()
    {
        return _compiledInvoker.GetMethodNames();
    }

    /// <summary>
    /// Sanitizes stack trace to remove sensitive file paths.
    /// </summary>
    private static string? SanitizeStackTrace(string? stackTrace)
    {
        if (string.IsNullOrEmpty(stackTrace))
        {
            return null;
        }

        // Simple sanitization: remove full file paths
        // In production, use more sophisticated sanitization
        var lines = stackTrace.Split('\n');
        var sanitized = new List<string>();

        foreach (var line in lines)
        {
            var sanitizedLine = line;

            // Remove file path information (keep line numbers)
            var atIndex = line.IndexOf(" at ", StringComparison.Ordinal);
            if (atIndex >= 0)
            {
                var inIndex = line.IndexOf(" in ", StringComparison.Ordinal);
                if (inIndex > atIndex)
                {
                    sanitizedLine = line.Substring(0, inIndex);
                }
            }

            sanitized.Add(sanitizedLine);
        }

        return string.Join("\n", sanitized);
    }

    /// <summary>
    /// Wraps IRequestContext to provide timeout-aware cancellation token.
    /// </summary>
    private sealed class TimeoutWrappedContext : IRequestContext
    {
        private readonly IRequestContext _inner;
        private readonly CancellationToken _timeoutToken;

        public TimeoutWrappedContext(IRequestContext inner, CancellationToken timeoutToken)
        {
            _inner = inner;
            _timeoutToken = timeoutToken;
        }

        public Guid RequestId => _inner.RequestId;
        public string ConnectionId => _inner.ConnectionId;
        public string ClientId => _inner.ClientId;
        public string ServiceName => _inner.ServiceName;
        public string MethodName => _inner.MethodName;
        public IReadOnlyDictionary<string, string> Metadata => _inner.Metadata;
        public CancellationToken CancellationToken => _timeoutToken; // Use timeout token
        public long StartTimestamp => _inner.StartTimestamp;
        public ActivityContext TraceContext => _inner.TraceContext;
        public System.Security.Claims.ClaimsPrincipal? User => _inner.User;
        public bool IsCancelled => _timeoutToken.IsCancellationRequested;
    }
}
