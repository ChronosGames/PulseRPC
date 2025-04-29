using PulseRPC.Protocol;
using PulseRPC.Server;
using System.Collections.Concurrent;
using System.Reflection;

namespace PulseRPC.Internal;

/// <summary>
/// Interface for managing Hub registrations.
/// </summary>
internal interface IHubRegistry
{
    void Register<THub, TReceiver>(Type implementationType)
        where THub : class, IPulseHub<THub, TReceiver>;

    HubHandler? GetHandler(string hubName);
}

/// <summary>
/// Handles registration and retrieval of Hub implementations.
/// </summary>
internal class HubRegistry : IHubRegistry
{
    private readonly ConcurrentDictionary<string, HubHandler> _hubHandlers = new();
    private readonly PulseServer _server; // Reference to the server for broadcasting

    public HubRegistry(PulseServer server)
    {
        _server = server;
    }

    public void Register<THub, TReceiver>(Type implementationType)
        where THub : class, IPulseHub<THub, TReceiver>
    {
        var hubType = typeof(THub);
        var hubName = hubType.FullName ?? hubType.Name;

        // Ensure implementationType implements THub
        if (!hubType.IsAssignableFrom(implementationType))
        {
            throw new InvalidOperationException($"Type '{implementationType.FullName}' must implement interface '{hubName}'.");
        }

        // Ensure implementationType has a parameterless constructor or one injectable by DI (if DI is used later)
        if (implementationType.GetConstructor(Type.EmptyTypes) == null && !implementationType.IsAbstract && !implementationType.IsInterface)
        {
            // Simplified check, real DI would be more complex
           // throw new InvalidOperationException($"Hub implementation type '{implementationType.FullName}' must have a parameterless constructor.");
        }

        var methods = hubType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                             .Where(m => m.DeclaringType == hubType)
                             .ToDictionary(m => m.Name, m => m);

        var receiverType = typeof(TReceiver);
        var handler = new HubHandler(_server, implementationType, methods, receiverType);

        if (!_hubHandlers.TryAdd(hubName, handler))
        {
            throw new InvalidOperationException($"Hub '{hubName}' is already registered.");
        }
    }

    public HubHandler? GetHandler(string hubName)
    {
        _hubHandlers.TryGetValue(hubName, out var handler);
        return handler;
    }
}

/// <summary>
/// Handles requests for a specific registered Hub.
/// </summary>
internal class HubHandler : RequestHandlerBase
{
    private readonly PulseServer _server;
    private readonly Type _implementationType;
    private readonly Dictionary<string, MethodInfo> _methods;
    private readonly Type _receiverType;
    private readonly MethodInfo[] _receiverMethods;

    public HubHandler(PulseServer server, Type implementationType, Dictionary<string, MethodInfo> methods, Type receiverType)
    {
        _server = server;
        _implementationType = implementationType;
        _methods = methods;
        _receiverType = receiverType;
        _receiverMethods = receiverType.GetMethods(BindingFlags.Public | BindingFlags.Instance); // Cache receiver methods
    }

    /// <summary>
    /// Creates a new HubContext for a client session.
    /// </summary>
    public HubContext CreateContext(ClientSession session)
    {
        // Create instance of the Hub implementation
        // TODO: Handle constructor injection if DI is implemented
        var hubInstance = Activator.CreateInstance(_implementationType) ?? throw new InvalidOperationException($"Failed to create instance of Hub type '{_implementationType.FullName}'.");
        var hubContext = new HubContext(session, hubInstance, this, _receiverType, _receiverMethods);

        // Initialize the base Hub properties if the implementation inherits from PulseHub
         if (hubInstance is HubBase baseHub)
        {
            baseHub.InitializeContext(hubContext);
        }

        return hubContext;
    }

    /// <summary>
    /// Handles a request targeted at this Hub.
    /// </summary>
    public async Task<PulseResponse> HandleRequestAsync(HubContext context, PulseRequest request)
    {
        if (!_methods.TryGetValue(request.MethodName, out var methodInfo))
        {
            return new PulseResponse
            {
                RequestId = request.RequestId,
                IsSuccess = false,
                ErrorMessage = $"Method '{request.MethodName}' not found on hub '{request.ServiceName}'."
            };
        }

        try
        {
            var parameterTypes = methodInfo.GetParameters().Select(p => p.ParameterType).ToArray();
            var parameters = DeserializeParameters(request.Parameters, parameterTypes);

            // Invoke the method on the specific Hub instance for this session
            var result = await InvokeMethodAsync(methodInfo, context.HubInstance, parameters);
            var serializedResult = SerializeResult(result);

            return new PulseResponse
            {
                RequestId = request.RequestId,
                IsSuccess = true,
                Result = serializedResult
            };
        }
        catch (Exception ex)
        {
            var innerExceptionMessage = ex.InnerException != null ? $" Inner Exception: {ex.InnerException.Message}" : string.Empty;
            return new PulseResponse
            {
                RequestId = request.RequestId,
                IsSuccess = false,
                ErrorMessage = $"Error executing hub method '{request.MethodName}': {ex.Message}{innerExceptionMessage}"
            };
        }
    }

    // Method for broadcasting from the Hub instance
    public Task BroadcastToGroupAsync(string groupName, string methodName, byte[] eventData, CancellationToken cancellationToken = default)
    {
        var hubName = _implementationType.BaseType?.GetGenericArguments().FirstOrDefault()?.FullName ?? _implementationType.FullName ?? "UnknownHub";
        var eventPacket = new PulseEvent { HubName = hubName, EventName = methodName, EventData = eventData };
        return _server.BroadcastToGroupAsync(groupName, eventPacket, cancellationToken);
    }

    public Task SendToClientAsync(string clientId, string methodName, byte[] eventData, CancellationToken cancellationToken = default)
    {
         var hubName = _implementationType.BaseType?.GetGenericArguments().FirstOrDefault()?.FullName ?? _implementationType.FullName ?? "UnknownHub";
        var eventPacket = new PulseEvent { HubName = hubName, EventName = methodName, EventData = eventData };
        return _server.SendToClientAsync(clientId, eventPacket, cancellationToken);
    }

    public Task BroadcastToAllAsync(string methodName, byte[] eventData, IEnumerable<string>? excludedGroups = null, CancellationToken cancellationToken = default)
    {
         var hubName = _implementationType.BaseType?.GetGenericArguments().FirstOrDefault()?.FullName ?? _implementationType.FullName ?? "UnknownHub";
        var eventPacket = new PulseEvent { HubName = hubName, EventName = methodName, EventData = eventData };
        return _server.BroadcastToAllAsync(eventPacket, excludedGroups, cancellationToken);
    }
}
