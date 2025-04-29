using MemoryPack;
using PulseRPC.Protocol;
using System.Dynamic;
using System.Reflection;

namespace PulseRPC.Internal;

/// <summary>
/// Base class for dynamic proxies that invoke methods on client receivers.
/// </summary>
internal abstract class HubClientsProxy : DynamicObject
{
    protected readonly ClientSession _session;
    protected readonly HubHandler _handler;
    protected readonly Type _receiverType;
    protected readonly MethodInfo[] _receiverMethods;
    protected string? _targetGroup;
    protected string? _targetClient;
    protected IEnumerable<string>? _excludedGroups;

    protected HubClientsProxy(ClientSession session, HubHandler handler, Type receiverType, MethodInfo[] receiverMethods)
    {
        _session = session;
        _handler = handler;
        _receiverType = receiverType;
        _receiverMethods = receiverMethods;
    }

    // Entry point for dynamic calls like Clients.Caller.MethodName()
    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        switch (binder.Name)
        {
            case "Caller":
                _targetClient = _session.ClientId;
                _targetGroup = null;
                _excludedGroups = null;
                result = this; // Return self for chained calls
                return true;
            case "All":
                _targetClient = null;
                _targetGroup = null;
                _excludedGroups = null;
                result = this; // Return self for chained calls
                return true;
            default:
                result = null;
                return false; // Could also handle specific client/group calls here if needed
        }
    }

    // Entry point for dynamic calls like Clients.Group("name").MethodName()
    public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
    {
        if (binder.Name == "Group")
        {
            if (args?.Length == 1 && args[0] is string groupName)
            {
                _targetClient = null;
                _targetGroup = groupName;
                _excludedGroups = null;
                result = this; // Return self for chained calls
                return true;
            }
        }
        else if (binder.Name == "AllExcept")
        {
             if (args?.Length == 1 && args[0] is IEnumerable<string> excludedGroups)
            {
                _targetClient = null;
                _targetGroup = null;
                _excludedGroups = excludedGroups;
                result = this; // Return self for chained calls
                return true;
            }
        }
        else
        {
            // This is the actual method call on the receiver interface (e.g., Clients.Caller.ReceiveMessage(...))
            var methodName = binder.Name;
            var method = FindReceiverMethod(methodName, args);

            if (method != null)
            {
                var parameterData = SerializeEventArgs(args);
                result = SendEventInternalAsync(methodName, parameterData);
                return true;
            }
        }

        result = null;
        return false;
    }

    protected MethodInfo? FindReceiverMethod(string methodName, object?[]? args)
    {
        // Basic matching by name and parameter count
        // TODO: Add more robust overload resolution if needed
        var argCount = args?.Length ?? 0;
        return _receiverMethods.FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == argCount);
    }

    protected byte[] SerializeEventArgs(object?[]? args)
    {
        if (args == null || args.Length == 0)
        {
            return Array.Empty<byte>();
        }
        if (args.Length == 1)
        {
            // Optimize for single argument
            var argType = args[0]?.GetType() ?? typeof(object);
            return MemoryPackSerializer.Serialize(argType, args[0]);
        }

        // Serialize multiple arguments as an array/tuple
        // Using object[] for simplicity here, consider generating specific tuples
        return MemoryPackSerializer.Serialize<object?[]>(args);
    }

    protected Task SendEventInternalAsync(string methodName, byte[] eventData)
    {
        if (_targetClient != null)
        {
            return _handler.SendToClientAsync(_targetClient, methodName, eventData);
        }
        if (_targetGroup != null)
        {
            return _handler.BroadcastToGroupAsync(_targetGroup, methodName, eventData);
        }
        // Default to All (or AllExcept)
        return _handler.BroadcastToAllAsync(methodName, eventData, _excludedGroups);
    }
}

/// <summary>
/// Typed proxy for invoking methods on client receivers.
/// </summary>
/// <typeparam name="TReceiver">The receiver interface type.</typeparam>
internal class HubClientsProxy<TReceiver> : HubClientsProxy where TReceiver : class
{
    public HubClientsProxy(ClientSession session, HubHandler handler, Type receiverType, MethodInfo[] receiverMethods)
        : base(session, handler, receiverType, receiverMethods)
    {
    }

    /// <summary>
    /// Targets the calling client.
    /// </summary>
    public TReceiver Caller
    {
        get
        {
            _targetClient = _session.ClientId;
            _targetGroup = null;
            _excludedGroups = null;
            return (TReceiver)(object)this; // Cast self to the receiver type
        }
    }

    /// <summary>
    /// Targets all connected clients.
    /// </summary>
    public TReceiver All
    {
        get
        {
            _targetClient = null;
            _targetGroup = null;
            _excludedGroups = null;
            return (TReceiver)(object)this;
        }
    }

    /// <summary>
    /// Targets all clients in the specified group.
    /// </summary>
    public TReceiver Group(string groupName)
    {
        _targetClient = null;
        _targetGroup = groupName;
        _excludedGroups = null;
        return (TReceiver)(object)this;
    }

    /// <summary>
    /// Targets all clients except those in the specified groups.
    /// </summary>
    public TReceiver AllExcept(params string[] excludedGroups)
    {
        _targetClient = null;
        _targetGroup = null;
        _excludedGroups = excludedGroups;
        return (TReceiver)(object)this;
    }

    // Override TryInvokeMember for typed dispatch
    public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
    {
        // Find the method on the TReceiver interface
        var method = FindReceiverMethod(binder.Name, args);
        if (method != null)
        {
            // Serialize arguments
            var parameterData = SerializeEventArgs(args);

            // Determine target and send
            result = SendEventInternalAsync(binder.Name, parameterData);
            return true;
        }

        // Fallback to base for Group/AllExcept calls etc.
        return base.TryInvokeMember(binder, args, out result);
    }
}
