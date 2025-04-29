using System.Reflection;

namespace PulseRPC.Internal;

/// <summary>
/// Represents the context of a Hub instance within a client session.
/// Provides access to client, groups, and other Hub-related functionalities.
/// </summary>
internal class HubContext : IAsyncDisposable
{
    private readonly ClientSession _session;
    private readonly HubHandler _handler;
    private readonly HubClientsProxy _clientsProxy;
    private readonly HubGroupsProxy _groupsProxy;

    /// <summary>
    /// Gets the Hub implementation instance associated with this context.
    /// </summary>
    public object HubInstance { get; }

    /// <summary>
    /// Gets the unique identifier for the connection associated with this context.
    /// </summary>
    public string ConnectionId => _session.ClientId;

    /// <summary>
    /// Gets a proxy object for invoking methods on the connected client(s).
    /// </summary>
    public dynamic Clients => _clientsProxy;

    /// <summary>
    /// Gets an object for managing group membership.
    /// </summary>
    public IHubGroups Groups => _groupsProxy;

    /// <summary>
    /// Gets the groups the current connection belongs to.
    /// </summary>
    public IEnumerable<string> SessionGroups => _session.GetGroups();

    internal HubContext(ClientSession session, object hubInstance, HubHandler handler, Type receiverType, MethodInfo[] receiverMethods)
    {
        _session = session;
        HubInstance = hubInstance;
        _handler = handler;
        _clientsProxy = new HubClientsProxy(session, handler, receiverType, receiverMethods);
        _groupsProxy = new HubGroupsProxy(session, handler);
    }

    /// <summary>
    /// Cleans up resources associated with the Hub context.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        // Dispose HubInstance if it implements IDisposable or IAsyncDisposable
        if (HubInstance is IAsyncDisposable asyncDisposable)
        {
            return asyncDisposable.DisposeAsync();
        }
        if (HubInstance is IDisposable disposable)
        {
            disposable.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Base class for Hub implementations, providing access to context.
/// Users should inherit from PulseHub<THub, TReceiver> instead.
/// </summary>
public abstract class HubBase
{
    internal HubContext? _context;

    /// <summary>
    /// Gets the context for the current Hub invocation.
    /// </summary>
    protected HubContext Context => _context ?? throw new InvalidOperationException("HubContext has not been initialized.");

    /// <summary>
    /// Gets a dynamic proxy to invoke methods on the connected client(s).
    /// Example: Clients.Caller.ReceiveMessage("hello"); Clients.Group("myGroup").Notify("update");
    /// </summary>
    protected dynamic Clients => Context.Clients;

    /// <summary>
    /// Gets an object for managing group membership for the current connection.
    /// </summary>
    protected IHubGroups Groups => Context.Groups;

    /// <summary>
    /// Called by the framework to initialize the Hub context.
    /// </summary>
    internal void InitializeContext(HubContext context)
    {
        _context = context;
        OnConnectedAsync().ConfigureAwait(false).GetAwaiter().GetResult(); // Call synchronously for initialization
    }

    /// <summary>
    /// Called when a client connects to this Hub instance.
    /// </summary>
    /// <returns>A task that represents the asynchronous connect event.</returns>
    public virtual Task OnConnectedAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when a client disconnects from this Hub instance.
    /// </summary>
    /// <returns>A task that represents the asynchronous disconnect event.</returns>
    public virtual Task OnDisconnectedAsync()
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// Typed base class for Hub implementations.
/// </summary>
/// <typeparam name="THub">The Hub interface.</typeparam>
/// <typeparam name="TReceiver">The receiver interface for client callbacks.</typeparam>
public abstract class PulseHub<THub, TReceiver> : HubBase, IPulseHub<THub, TReceiver>
    where THub : class, IPulseHub<THub, TReceiver>
    where TReceiver : class
{
    /// <summary>
    /// Gets a typed proxy to invoke methods on the connected client(s).
    /// Provides better type safety and discoverability compared to the dynamic Clients property.
    /// </summary>
    protected new HubClientsProxy<TReceiver> Clients => (HubClientsProxy<TReceiver>)base.Clients;
}

/// <summary>
/// Interface for managing group membership.
/// </summary>
public interface IHubGroups
{
    Task AddToGroupAsync(string groupName, CancellationToken cancellationToken = default);
    Task RemoveFromGroupAsync(string groupName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Internal implementation for managing group membership for a connection.
/// </summary>
internal class HubGroupsProxy : IHubGroups
{
    private readonly ClientSession _session;
    private readonly HubHandler _handler;

    public HubGroupsProxy(ClientSession session, HubHandler handler)
    {
        _session = session;
        _handler = handler;
    }

    public Task AddToGroupAsync(string groupName, CancellationToken cancellationToken = default)
    {
        // Server-side group management is handled by ClientSession
        _session.AddToGroup(groupName);
        // TODO: Potentially notify other servers in a distributed scenario
        return Task.CompletedTask;
    }

    public Task RemoveFromGroupAsync(string groupName, CancellationToken cancellationToken = default)
    {
        // Server-side group management is handled by ClientSession
        _session.RemoveFromGroup(groupName);
        // TODO: Potentially notify other servers in a distributed scenario
        return Task.CompletedTask;
    }
}
