using PulseRPC.Protocol;
using PulseRPC.Server;
using System.Collections.Concurrent;
using System.Reflection;
using PulseRPC.Server.Monitoring;

namespace PulseRPC.Internal;

/// <summary>
/// Interface for managing Hub registrations.
/// </summary>
internal interface IHubRegistry
{
    void Register<THub, TReceiver>(Type implementationType)
        where THub : class, IPulseHub<THub, TReceiver>
        where TReceiver : class;

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
        where TReceiver : class
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
        // 开始记录请求指标
        PulseMetrics.StartRequest(request.RequestId);

        if (!_methods.TryGetValue(request.MethodName, out var methodInfo))
        {
            // 记录请求失败
            PulseMetrics.EndRequest(request.RequestId, request.ServiceName, request.MethodName, false);

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

            // 记录请求成功
            PulseMetrics.EndRequest(request.RequestId, request.ServiceName, request.MethodName, true);

            return new PulseResponse
            {
                RequestId = request.RequestId,
                IsSuccess = true,
                Result = serializedResult
            };
        }
        catch (Exception ex)
        {
            // 记录请求失败
            PulseMetrics.EndRequest(request.RequestId, request.ServiceName, request.MethodName, false);

            var innerExceptionMessage = ex.InnerException != null ? $" Inner Exception: {ex.InnerException.Message}" : string.Empty;
            return new PulseResponse
            {
                RequestId = request.RequestId,
                IsSuccess = false,
                ErrorMessage = $"Error executing hub method '{request.MethodName}': {ex.Message}{innerExceptionMessage}"
            };
        }
    }

    /// <summary>
    /// 处理发送到Hub的事件
    /// </summary>
    public async Task HandleEventAsync(HubContext context, PulseEvent @event)
    {
        try
        {
            // 查找接收者接口上对应的方法
            var receiverMethod = _receiverMethods.FirstOrDefault(m => m.Name == @event.EventName);
            if (receiverMethod == null)
            {
                throw new InvalidOperationException($"Receiver method '{@event.EventName}' not found on hub receiver interface.");
            }

            // 获取参数信息
            var parameterTypes = receiverMethod.GetParameters().Select(p => p.ParameterType).ToArray();

            // 反序列化事件数据
            object? parameters = null;
            if (@event.EventData != null && @event.EventData.Length > 0)
            {
                parameters = DeserializeParameters(@event.EventData, parameterTypes);
            }

            // 由于HubContext不允许我们直接调用接收者接口的方法，
            // 我们需要通过Hub实例间接处理事件
            // 实际实现会根据Hub的设计而有所不同，这里只是一个框架

            // 记录处理的事件
            var eventId = Guid.NewGuid(); // 事件没有请求ID，所以我们生成一个用于日志
            PulseMetrics.StartRequest(eventId);

            try
            {
                // 示例：将事件转发到Hub的某个处理方法
                // 实际实现可能需要更复杂的逻辑
                if (context.HubInstance is HubBase hubBase)
                {
                    await hubBase.ProcessEventAsync(@event.EventName, parameters);
                    PulseMetrics.EndRequest(eventId, @event.HubName, @event.EventName, true);
                }
                else
                {
                    throw new InvalidOperationException($"Hub instance does not inherit from HubBase.");
                }
            }
            catch (Exception ex)
            {
                PulseMetrics.EndRequest(eventId, @event.HubName, @event.EventName, false);
                throw new InvalidOperationException($"Error processing event '{@event.EventName}': {ex.Message}", ex);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error handling event: {ex.Message}", ex);
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
