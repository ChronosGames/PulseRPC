using System.Diagnostics;
using PulseRPC.Server.Models;

namespace PulseRPC.Server.Observability;

/// <summary>
/// Activity-based distributed tracing integration using W3C Trace Context.
/// Zero allocations in fast path when tracing is disabled.
/// </summary>
public static class DistributedTracingIntegration
{
    private static readonly ActivitySource ActivitySource = new("PulseRPC.Server", "1.0.0");

    /// <summary>
    /// Starts a new activity for message processing.
    /// </summary>
    public static Activity? StartMessageActivity(RpcMessage message, bool enabled)
    {
        if (!enabled)
            return null;

        var activity = ActivitySource.StartActivity("ProcessMessage", ActivityKind.Server);
        if (activity == null)
            return null;

        // Set standard tags
        activity.SetTag("rpc.system", "pulserpc");
        activity.SetTag("rpc.service", message.ServiceName);
        activity.SetTag("rpc.method", message.MethodName);
        activity.SetTag("rpc.request_id", message.RequestId.ToString());
        activity.SetTag("net.protocol.version", message.ProtocolVersion.ToString());

        // Set message metadata as tags
        if (message.Metadata != null)
        {
            foreach (var kvp in message.Metadata)
            {
                activity.SetTag($"rpc.metadata.{kvp.Key}", kvp.Value);
            }
        }

        return activity;
    }

    /// <summary>
    /// Records the completion of a message activity.
    /// </summary>
    public static void CompleteMessageActivity(Activity? activity, ResponseEnvelope response)
    {
        if (activity == null)
            return;

        activity.SetTag("rpc.response.success", response.IsSuccess);
        activity.SetTag("rpc.response.duration_ms", response.DurationMs);

        if (!response.IsSuccess && response.ExceptionDetails != null)
        {
            activity.SetTag("rpc.error.type", response.ExceptionDetails.ExceptionType);
            activity.SetTag("rpc.error.message", response.ExceptionDetails.Message);
            activity.SetStatus(ActivityStatusCode.Error, response.ExceptionDetails.Message);
        }
        else
        {
            activity.SetStatus(ActivityStatusCode.Ok);
        }

        activity.Stop();
    }

    /// <summary>
    /// Starts a child activity for service invocation.
    /// </summary>
    public static Activity? StartInvocationActivity(string serviceName, string methodName, bool enabled)
    {
        if (!enabled)
            return null;

        var activity = ActivitySource.StartActivity("InvokeMethod", ActivityKind.Internal);
        if (activity == null)
            return null;

        activity.SetTag("rpc.service", serviceName);
        activity.SetTag("rpc.method", methodName);

        return activity;
    }

    /// <summary>
    /// Records an error in the activity.
    /// </summary>
    public static void RecordError(Activity? activity, Exception exception)
    {
        if (activity == null)
            return;

        activity.SetTag("error", true);
        activity.SetTag("error.type", exception.GetType().FullName);
        activity.SetTag("error.message", exception.Message);
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);

        // Add exception event
        var tags = new ActivityTagsCollection
        {
            { "exception.type", exception.GetType().FullName },
            { "exception.message", exception.Message },
            { "exception.stacktrace", exception.StackTrace }
        };

        activity.AddEvent(new ActivityEvent("exception", DateTimeOffset.UtcNow, tags));
    }

    /// <summary>
    /// Extracts trace context from metadata.
    /// </summary>
    public static ActivityContext ExtractTraceContext(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata == null)
            return default;

        // W3C Trace Context format: traceparent header
        if (metadata.TryGetValue("traceparent", out var traceparent))
        {
            return ActivityContext.Parse(traceparent, null);
        }

        return default;
    }

    /// <summary>
    /// Injects trace context into metadata.
    /// </summary>
    public static Dictionary<string, string> InjectTraceContext(Activity? activity, Dictionary<string, string>? metadata = null)
    {
        var result = metadata != null ? new Dictionary<string, string>(metadata) : new Dictionary<string, string>();

        if (activity != null)
        {
            result["traceparent"] = activity.Id ?? string.Empty;
            if (!string.IsNullOrEmpty(activity.TraceStateString))
            {
                result["tracestate"] = activity.TraceStateString;
            }
        }

        return result;
    }

    /// <summary>
    /// Creates baggage from metadata.
    /// </summary>
    public static void SetBaggage(Activity? activity, IReadOnlyDictionary<string, string>? metadata)
    {
        if (activity == null || metadata == null)
            return;

        foreach (var kvp in metadata)
        {
            // Only set baggage for known correlation keys
            if (kvp.Key.StartsWith("baggage-", StringComparison.OrdinalIgnoreCase))
            {
                activity.SetBaggage(kvp.Key[8..], kvp.Value);
            }
        }
    }

    /// <summary>
    /// Records a custom event in the activity.
    /// </summary>
    public static void RecordEvent(Activity? activity, string eventName, params (string key, object? value)[] tags)
    {
        if (activity == null)
            return;

        var tagCollection = new ActivityTagsCollection();
        foreach (var (key, value) in tags)
        {
            tagCollection.Add(key, value);
        }

        activity.AddEvent(new ActivityEvent(eventName, DateTimeOffset.UtcNow, tagCollection));
    }

    /// <summary>
    /// Gets the current activity (if any).
    /// </summary>
    public static Activity? Current => Activity.Current;

    /// <summary>
    /// Checks if tracing is enabled for the current context.
    /// </summary>
    public static bool IsEnabled => ActivitySource.HasListeners();
}
