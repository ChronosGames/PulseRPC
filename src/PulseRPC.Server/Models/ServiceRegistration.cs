using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Scheduling;

namespace PulseRPC.Server.Models;

/// <summary>
/// Represents a registered RPC service with compiled method invokers.
/// </summary>
public sealed class ServiceRegistration
{
    private long _invocationCount;
    private long _errorCount;
    private long _totalDurationMs;

    /// <summary>
    /// Unique service identifier.
    /// </summary>
    public string ServiceName { get; init; } = string.Empty;

    /// <summary>
    /// CLR type of service implementation.
    /// </summary>
    public Type ServiceType { get; init; } = typeof(object);

    /// <summary>
    /// Method name to compiled delegate mapping.
    /// </summary>
    public IReadOnlyDictionary<string, CompiledMethodInvoker> Methods { get; init; }
        = new Dictionary<string, CompiledMethodInvoker>();

    /// <summary>
    /// Service handler for method invocations.
    /// </summary>
    public IServiceHandler? Handler { get; init; }

    /// <summary>
    /// Service-specific options.
    /// </summary>
    public ServiceOptions? Options { get; init; }

    /// <summary>
    /// Registration timestamp.
    /// </summary>
    public DateTime RegisteredAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Per-method timeout (default).
    /// </summary>
    public TimeSpan TimeoutPolicy { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Dispatch priority.
    /// </summary>
    public MessagePriority Priority { get; init; } = MessagePriority.Normal;

    /// <summary>
    /// Service state.
    /// </summary>
    public ServiceState State { get; internal set; } = ServiceState.Registered;

    /// <summary>
    /// Cumulative method invocations.
    /// </summary>
    public long InvocationCount => Interlocked.Read(ref _invocationCount);

    /// <summary>
    /// Cumulative invocation errors.
    /// </summary>
    public long ErrorCount => Interlocked.Read(ref _errorCount);

    /// <summary>
    /// Cumulative execution time in milliseconds.
    /// </summary>
    public long TotalDurationMs => Interlocked.Read(ref _totalDurationMs);

    /// <summary>
    /// Average execution time in milliseconds.
    /// </summary>
    public double AverageExecutionMs
    {
        get
        {
            var count = InvocationCount;
            return count > 0 ? (double)TotalDurationMs / count : 0;
        }
    }

    /// <summary>
    /// Transitions the service to a new state.
    /// </summary>
    public bool TryTransitionState(ServiceState newState)
    {
        // Validate state transitions
        var isValidTransition = (State, newState) switch
        {
            (ServiceState.Registered, ServiceState.Active) => true,
            (ServiceState.Active, ServiceState.Paused) => true,
            (ServiceState.Paused, ServiceState.Active) => true,
            (_, ServiceState.Unregistered) => true, // Can always unregister
            _ => false
        };

        if (isValidTransition)
        {
            State = newState;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Records a method invocation.
    /// </summary>
    public void RecordInvocation(long durationMs, bool isError = false)
    {
        Interlocked.Increment(ref _invocationCount);
        Interlocked.Add(ref _totalDurationMs, durationMs);
        if (isError)
        {
            Interlocked.Increment(ref _errorCount);
        }
    }

    /// <summary>
    /// Checks if the service is active and can handle requests.
    /// </summary>
    public bool CanHandleRequests => State == ServiceState.Active;
}

/// <summary>
/// Placeholder for compiled method invoker (will be implemented in T023).
/// </summary>
public sealed class CompiledMethodInvoker
{
    /// <summary>
    /// Method name.
    /// </summary>
    public string MethodName { get; init; } = string.Empty;

    /// <summary>
    /// Method parameter types.
    /// </summary>
    public Type[] ParameterTypes { get; init; } = Array.Empty<Type>();

    /// <summary>
    /// Method return type.
    /// </summary>
    public Type ReturnType { get; init; } = typeof(void);

    /// <summary>
    /// Compiled delegate (will be set in T023).
    /// </summary>
    public Delegate? CompiledDelegate { get; init; }

    /// <summary>
    /// Whether the method is async.
    /// </summary>
    public bool IsAsync { get; init; }
}

/// <summary>
/// Per-service configuration options.
/// </summary>
public sealed class ServiceOptions
{
    /// <summary>
    /// Timeout for this service's method invocations.
    /// </summary>
    public TimeSpan? DefaultTimeout { get; set; }

    /// <summary>
    /// Priority for this service's messages.
    /// </summary>
    public MessagePriority Priority { get; set; } = MessagePriority.Normal;

    /// <summary>
    /// Maximum concurrent requests for this service (0 = unlimited).
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 0;

    /// <summary>
    /// Rate limit: maximum requests per second (0 = unlimited).
    /// </summary>
    public int MaxRequestsPerSecond { get; set; } = 0;
}
