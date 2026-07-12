using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Channels;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Transport;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Shared;

namespace PulseRPC.Server;

/// <summary>
/// 命名服务器实现，包装 PulseServer 并添加服务器名称
/// 支持在同一进程中运行多个独立的服务器实例
/// </summary>
internal sealed class NamedPulseServer : INamedPulseServer
{
    private readonly ServerRuntime _runtime;
    private int _eventsDetached;

    /// <inheritdoc />
    public string ServerName { get; }

    /// <summary>
    /// 构造命名服务器实例
    /// </summary>
    /// <param name="serverName">服务器名称（唯一标识）</param>
    /// <param name="runtime">该命名服务器独占的运行时组合根。</param>
    public NamedPulseServer(string serverName, ServerRuntime runtime)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            throw new ArgumentException("Server name cannot be null or whitespace", nameof(serverName));

        ServerName = serverName;

        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _runtime.StateChanged += OnRuntimeStateChanged;
        _runtime.ClientConnected += OnRuntimeClientConnected;
        _runtime.ClientDisconnected += OnRuntimeClientDisconnected;
    }

    internal ServerRuntime Runtime => _runtime;

    // === IPulseServer 接口委托实现 ===

    /// <inheritdoc />
    public ServerState State => _runtime.State;

    /// <inheritdoc />
    public bool IsRunning => _runtime.IsRunning;

    /// <inheritdoc />
    public int ActiveConnectionCount => _runtime.ActiveConnectionCount;

    /// <inheritdoc />
    public event EventHandler<ServerStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<ClientConnectedEventArgs>? ClientConnected;

    /// <inheritdoc />
    public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
        => _runtime.StartAsync(cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
        => _runtime.StopAsync(cancellationToken);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, TransportInfo> GetTransports()
        => _runtime.GetTransports();

    /// <inheritdoc />
    public TransportInfo? GetDefaultTransport()
        => _runtime.GetDefaultTransport();

    /// <inheritdoc />
    public IReadOnlyList<ConnectionInfo> GetActiveConnections()
        => _runtime.GetActiveConnections();

    /// <inheritdoc />
    public Task<int> BroadcastAsync(ReadOnlyMemory<byte> data, Func<TransportContext, bool>? filter = null, CancellationToken cancellationToken = default)
        => _runtime.BroadcastAsync(data, filter, cancellationToken);

    /// <inheritdoc />
    public Task<bool> SendAsync(string connectionId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        => _runtime.SendAsync(connectionId, data, cancellationToken);

    // === Transport Channel Management (双向RPC支持) ===

    /// <inheritdoc />
    public ITransportChannel? GetChannel(string connectionId)
        => _runtime.GetChannel(connectionId);

    /// <inheritdoc />
    public IReadOnlyList<ITransportChannel> GetAllChannels()
        => _runtime.GetAllChannels();

    /// <inheritdoc />
    public ITransportChannelPool ChannelPool => _runtime.ChannelPool;

    /// <inheritdoc />
    public IReadOnlyList<ServiceInfo> GetRegisteredServices()
        => _runtime.GetRegisteredServices();

    /// <inheritdoc />
    public ServerPerformanceMetrics GetPerformanceMetrics()
        => _runtime.GetPerformanceMetrics();

    /// <inheritdoc />
    public void ResetPerformanceMetrics()
        => _runtime.ResetPerformanceMetrics();

    // === Disposal ===

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            _runtime.Dispose();
        }
        finally
        {
            DetachRuntimeEvents();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _runtime.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            DetachRuntimeEvents();
        }
    }

    private void OnRuntimeStateChanged(object? sender, ServerStateChangedEventArgs args)
        => StateChanged?.Invoke(this, args);

    private void OnRuntimeClientConnected(object? sender, ClientConnectedEventArgs args)
        => ClientConnected?.Invoke(this, args);

    private void OnRuntimeClientDisconnected(object? sender, ClientDisconnectedEventArgs args)
        => ClientDisconnected?.Invoke(this, args);

    private void DetachRuntimeEvents()
    {
        if (Interlocked.Exchange(ref _eventsDetached, 1) != 0)
        {
            return;
        }

        _runtime.StateChanged -= OnRuntimeStateChanged;
        _runtime.ClientConnected -= OnRuntimeClientConnected;
        _runtime.ClientDisconnected -= OnRuntimeClientDisconnected;
    }
}
