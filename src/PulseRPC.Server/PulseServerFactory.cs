using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Channels;
using PulseRPC.Serialization;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Extensions;
using PulseRPC.Server.Processing.Pipeline;
using PulseRPC.Shared;

namespace PulseRPC.Server;

/// <summary>
/// Deprecated static compatibility facade for applications that cannot yet use Generic Host.
/// </summary>
/// <remarks>
/// This facade now reuses the same internal runtime composition as
/// <c>services.AddPulseServer(...)</c> and owns its private DI provider, but Generic Host
/// remains the supported entry point for configuration, logging, and lifecycle ordering.
/// </remarks>
[Obsolete("Use Generic Host with services.AddPulseServer(...).", false)]
public static class PulseServerFactory
{
    /// <summary>
    /// 使用默认配置创建服务器
    /// </summary>
    /// <param name="port">TCP 监听端口</param>
    /// <returns>配置好的服务器实例</returns>
    public static IPulseServer CreateDefault(int port)
    {
        return Create(options => options.AddTcp(port));
    }

    /// <summary>
    /// 使用默认配置创建服务器（带日志工厂）
    /// </summary>
    /// <param name="port">TCP 监听端口</param>
    /// <param name="loggerFactory">日志工厂</param>
    /// <returns>配置好的服务器实例</returns>
    public static IPulseServer CreateDefault(int port, ILoggerFactory loggerFactory)
    {
        return Create(
            options => options.AddTcp(port),
            loggerFactory);
    }

    /// <summary>
    /// 使用指定预设创建服务器
    /// </summary>
    /// <param name="preset">服务器预设</param>
    /// <param name="port">TCP 监听端口</param>
    /// <returns>配置好的服务器实例</returns>
    public static IPulseServer Create(ServerPreset preset, int port)
    {
        _ = preset;
        return Create(options => options.AddTcp(port));
    }

    /// <summary>
    /// 使用指定预设创建服务器（带日志工厂）
    /// </summary>
    /// <param name="preset">服务器预设</param>
    /// <param name="port">TCP 监听端口</param>
    /// <param name="loggerFactory">日志工厂</param>
    /// <returns>配置好的服务器实例</returns>
    public static IPulseServer Create(ServerPreset preset, int port, ILoggerFactory loggerFactory)
    {
        _ = preset;
        return Create(
            options => options.AddTcp(port),
            loggerFactory);
    }

    /// <summary>
    /// 使用自定义配置创建服务器
    /// </summary>
    /// <param name="configure">配置委托</param>
    /// <returns>配置好的服务器实例</returns>
    public static IPulseServer Create(Action<PulseServerOptions> configure)
    {
        return Create(configure, null);
    }

    /// <summary>
    /// 使用自定义配置创建服务器（带日志工厂）
    /// </summary>
    /// <param name="configure">配置委托</param>
    /// <param name="loggerFactory">日志工厂</param>
    /// <returns>配置好的服务器实例</returns>
    public static IPulseServer Create(Action<PulseServerOptions> configure, ILoggerFactory? loggerFactory)
    {
        if (configure == null)
            throw new ArgumentNullException(nameof(configure));

        var services = new ServiceCollection();
        services.AddLogging();

        if (loggerFactory is not null)
        {
            services.AddSingleton(loggerFactory);
        }

        services.AddSingleton<IServiceRoutingTable>(
            ServiceRoutingTableRegistry.Instance ?? EmptyServiceRoutingTable.Instance);
        services.AddSingleton<IResponseSerializerRegistry>(
            ResponseSerializerRegistry.Instance ?? EmptyResponseSerializerRegistry.Instance);

        // Reuse the standard DI composition instead of maintaining a parallel,
        // partial dependency graph in the compatibility factory.
        services.AddPulseServer(configure);

        var provider = services.BuildServiceProvider();
        try
        {
            return new OwnedPulseServer(
                provider.GetRequiredService<PulseServer>(),
                provider);
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }
}

internal sealed class EmptyServiceRoutingTable : IServiceRoutingTable
{
    public static EmptyServiceRoutingTable Instance { get; } = new();

    private EmptyServiceRoutingTable()
    {
    }

    public bool IsProtocolIdValid(string hub, ushort protocolId) => false;
    public ReadOnlySpan<ushort> EnumerateProtocolIds() => [];

    public ValueTask<object?> RouteByProtocolIdAsync(
        IServiceProvider serviceProvider,
        ushort protocolId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
        => ValueTask.FromException<object?>(UnknownProtocol(protocolId));

    public ValueTask<object?> RouteByProtocolIdAsync(
        IServiceProvider serviceProvider,
        string hub,
        ushort protocolId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
        => ValueTask.FromException<object?>(UnknownProtocol(protocolId));

    public ValueTask<object?> RouteByProtocolIdAsync(
        IServiceProvider serviceProvider,
        ushort protocolId,
        string serviceKey,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
        => ValueTask.FromException<object?>(UnknownProtocol(protocolId));

    public ValueTask<object?> RouteByProtocolIdAsync(
        IServiceProvider serviceProvider,
        string hub,
        ushort protocolId,
        string serviceKey,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
        => ValueTask.FromException<object?>(UnknownProtocol(protocolId));

    private static InvalidOperationException UnknownProtocol(ushort protocolId)
        => new($"No generated service routing table is registered for protocol 0x{protocolId:X4}.");
}

internal sealed class EmptyResponseSerializerRegistry : IResponseSerializerRegistry
{
    public static EmptyResponseSerializerRegistry Instance { get; } = new();

    private EmptyResponseSerializerRegistry()
    {
    }

    public bool TryGetSerializer(
        ushort protocolId,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IResponseSerializer? serializer)
    {
        serializer = null;
        return false;
    }

    public ReadOnlySpan<IResponseSerializer> EnumerateSerializers() => [];
}

internal sealed class OwnedPulseServer : IPulseServer
{
    private readonly PulseServer _server;
    private readonly IServiceProvider _provider;
    private readonly object _disposeLock = new();
    private Task? _disposeTask;
    private int _eventsDetached;

    public OwnedPulseServer(PulseServer server, IServiceProvider provider)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _server.StateChanged += OnServerStateChanged;
        _server.ClientConnected += OnServerClientConnected;
        _server.ClientDisconnected += OnServerClientDisconnected;
    }

    internal ServerRuntime Runtime => _server.Runtime;

    public ServerState State => _server.State;
    public bool IsRunning => _server.IsRunning;
    public int ActiveConnectionCount => _server.ActiveConnectionCount;

    public event EventHandler<ServerStateChangedEventArgs>? StateChanged;

    public event EventHandler<ClientConnectedEventArgs>? ClientConnected;

    public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;

    public Task StartAsync(CancellationToken cancellationToken = default)
        => _server.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default)
        => _server.StopAsync(cancellationToken);

    public IReadOnlyDictionary<string, TransportInfo> GetTransports()
        => _server.GetTransports();

    public TransportInfo? GetDefaultTransport()
        => _server.GetDefaultTransport();

    public IReadOnlyList<ConnectionInfo> GetActiveConnections()
        => _server.GetActiveConnections();

    public Task<int> BroadcastAsync(
        ReadOnlyMemory<byte> data,
        Func<TransportContext, bool>? filter = null,
        CancellationToken cancellationToken = default)
        => _server.BroadcastAsync(data, filter, cancellationToken);

    public Task<bool> SendAsync(
        string connectionId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
        => _server.SendAsync(connectionId, data, cancellationToken);

    public ITransportChannel? GetChannel(string connectionId)
        => _server.GetChannel(connectionId);

    public IReadOnlyList<ITransportChannel> GetAllChannels()
        => _server.GetAllChannels();

    [Obsolete("Use GetChannel/GetAllChannels. Runtime channels are owned by IPulseServer; pool mutation is not supported.", false)]
    public ITransportChannelPool ChannelPool => _server.Runtime.ChannelPool;

    public IReadOnlyList<ServiceInfo> GetRegisteredServices()
        => _server.GetRegisteredServices();

    public ServerPerformanceMetrics GetPerformanceMetrics()
        => _server.GetPerformanceMetrics();

    public void ResetPerformanceMetrics()
        => _server.ResetPerformanceMetrics();

    public void Dispose()
        => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            if (_provider is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else if (_provider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        finally
        {
            DetachServerEvents();
        }

        GC.SuppressFinalize(this);
    }

    private void OnServerStateChanged(object? sender, ServerStateChangedEventArgs args)
        => StateChanged?.Invoke(this, args);

    private void OnServerClientConnected(object? sender, ClientConnectedEventArgs args)
        => ClientConnected?.Invoke(this, args);

    private void OnServerClientDisconnected(object? sender, ClientDisconnectedEventArgs args)
        => ClientDisconnected?.Invoke(this, args);

    private void DetachServerEvents()
    {
        if (Interlocked.Exchange(ref _eventsDetached, 1) != 0)
        {
            return;
        }

        _server.StateChanged -= OnServerStateChanged;
        _server.ClientConnected -= OnServerClientConnected;
        _server.ClientDisconnected -= OnServerClientDisconnected;
    }
}
