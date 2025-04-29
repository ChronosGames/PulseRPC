using MemoryPack;
using PulseRPC.Protocol;
using PulseRPC.Server.Hubs;
using System.Dynamic;
using System.Reflection;

namespace PulseRPC.Internal;

/// <summary>
/// Base class for dynamic proxies that invoke methods on client receivers.
/// </summary>
internal class HubClientsProxy : DynamicObject, IHubClients
{
    private readonly ClientSession _session;
    private readonly HubHandler _handler;
    private readonly Type _receiverType;
    private readonly MethodInfo[] _receiverMethods;
    private string? _targetGroup;
    private string? _targetClient;
    private IEnumerable<string>? _excludedGroups;

    internal HubClientsProxy(ClientSession session, HubHandler handler, Type receiverType, MethodInfo[] receiverMethods)
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

    private Task SendEventInternalAsync(string methodName, byte[] eventData)
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

    // 实现 IHubClients 接口
    public dynamic Caller
    {
        get
        {
            _targetClient = _session.ClientId;
            _targetGroup = null;
            _excludedGroups = null;
            return this;
        }
    }

    public dynamic All
    {
        get
        {
            _targetClient = null;
            _targetGroup = null;
            _excludedGroups = null;
            return this;
        }
    }

    public dynamic Group(string groupName)
    {
        _targetClient = null;
        _targetGroup = groupName;
        _excludedGroups = null;
        return this;
    }

    public dynamic AllExcept(params string[] excludedGroups)
    {
        _targetClient = null;
        _targetGroup = null;
        _excludedGroups = excludedGroups;
        return this;
    }
}

/// <summary>
/// Typed proxy for invoking methods on client receivers.
/// </summary>
/// <typeparam name="TReceiver">The receiver interface type.</typeparam>
internal class HubClientsProxy<TReceiver> : HubClientsProxy, IHubClients<TReceiver> where TReceiver : class
{
    internal HubClientsProxy(ClientSession session, HubHandler handler, Type receiverType, MethodInfo[] receiverMethods)
        : base(session, handler, receiverType, receiverMethods)
    {
    }

    /// <summary>
    /// Targets the calling client.
    /// </summary>
    public new TReceiver Caller
    {
        get
        {
            base.Caller.GetType(); // Just to invoke the base getter to set targeting
            return (TReceiver)(object)this; // Cast self to the receiver type
        }
    }

    /// <summary>
    /// Targets all connected clients.
    /// </summary>
    public new TReceiver All
    {
        get
        {
            base.All.GetType(); // Just to invoke the base getter to set targeting
            return (TReceiver)(object)this;
        }
    }

    /// <summary>
    /// Targets all clients in the specified group.
    /// </summary>
    public new TReceiver Group(string groupName)
    {
        base.Group(groupName);
        return (TReceiver)(object)this;
    }

    /// <summary>
    /// Targets all clients except those in the specified groups.
    /// </summary>
    public new TReceiver AllExcept(params string[] excludedGroups)
    {
        base.AllExcept(excludedGroups);
        return (TReceiver)(object)this;
    }

    // Override TryInvokeMember for typed dispatch
    public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
    {
        // Use the base implementation that includes our custom method calling logic
        return base.TryInvokeMember(binder, args, out result);
    }
}
