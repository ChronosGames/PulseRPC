using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Channels;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Integration;
using PulseRPC.Server.MessageEngine;
using PulseRPC.Server.Transport;
using PulseRPC.Transport;

namespace PulseRPC.Server;

/// <summary>
/// 命名服务器实现，包装 UnifiedPulseServer 并添加服务器名称
/// 支持在同一进程中运行多个独立的服务器实例
/// </summary>
internal sealed class NamedUnifiedPulseServer : INamedPulseServer
{
    private readonly UnifiedPulseServer _innerServer;

    /// <inheritdoc />
    public string ServerName { get; }

    /// <summary>
    /// 构造命名服务器实例
    /// </summary>
    /// <param name="serverName">服务器名称（唯一标识）</param>
    /// <param name="messageEngine">消息引擎</param>
    /// <param name="channelManager">通道管理器</param>
    /// <param name="transportIntegrationManager">传输集成管理器</param>
    /// <param name="loggerFactory">日志工厂</param>
    /// <param name="options">服务器配置选项</param>
    public NamedUnifiedPulseServer(
        string serverName,
        ITieredMessageEngine messageEngine,
        IServerChannelManager channelManager,
        ITransportIntegrationManager transportIntegrationManager,
        ILoggerFactory loggerFactory,
        IOptions<UnifiedServerOptions> options)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            throw new ArgumentException("Server name cannot be null or whitespace", nameof(serverName));

        ServerName = serverName;

        // 创建内部服务器实例
        _innerServer = new UnifiedPulseServer(
            messageEngine,
            channelManager,
            transportIntegrationManager,
            loggerFactory,
            options);
    }

    // === IPulseServer 接口委托实现 ===

    /// <inheritdoc />
    public ServerState State => _innerServer.State;

    /// <inheritdoc />
    public bool IsRunning => _innerServer.IsRunning;

    /// <inheritdoc />
    public int ActiveConnectionCount => _innerServer.ActiveConnectionCount;

    /// <inheritdoc />
    public event EventHandler<ServerStateChangedEventArgs>? StateChanged
    {
        add => _innerServer.StateChanged += value;
        remove => _innerServer.StateChanged -= value;
    }

    /// <inheritdoc />
    public event EventHandler<ClientConnectedEventArgs>? ClientConnected
    {
        add => _innerServer.ClientConnected += value;
        remove => _innerServer.ClientConnected -= value;
    }

    /// <inheritdoc />
    public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected
    {
        add => _innerServer.ClientDisconnected += value;
        remove => _innerServer.ClientDisconnected -= value;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
        => _innerServer.StartAsync(cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
        => _innerServer.StopAsync(cancellationToken);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, TransportInfo> GetTransports()
        => _innerServer.GetTransports();

    /// <inheritdoc />
    public TransportInfo? GetDefaultTransport()
        => _innerServer.GetDefaultTransport();

    /// <inheritdoc />
    public IReadOnlyList<ConnectionInfo> GetActiveConnections()
        => _innerServer.GetActiveConnections();

    /// <inheritdoc />
    public Task<int> BroadcastAsync(ReadOnlyMemory<byte> data, Func<TransportContext, bool>? filter = null, CancellationToken cancellationToken = default)
        => _innerServer.BroadcastAsync(data, filter, cancellationToken);

    /// <inheritdoc />
    public Task<bool> SendAsync(string connectionId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        => _innerServer.SendAsync(connectionId, data, cancellationToken);

    // === Transport Channel Management (双向RPC支持) ===

    /// <inheritdoc />
    public ITransportChannel? GetChannel(string connectionId)
        => _innerServer.GetChannel(connectionId);

    /// <inheritdoc />
    public IReadOnlyList<ITransportChannel> GetAllChannels()
        => _innerServer.GetAllChannels();

    /// <inheritdoc />
    public ITransportChannelPool ChannelPool => _innerServer.ChannelPool;

    /// <inheritdoc />
    public IReadOnlyList<ServiceInfo> GetRegisteredServices()
        => _innerServer.GetRegisteredServices();

    /// <inheritdoc />
    public ServerPerformanceMetrics GetPerformanceMetrics()
        => _innerServer.GetPerformanceMetrics();

    /// <inheritdoc />
    public void ResetPerformanceMetrics()
        => _innerServer.ResetPerformanceMetrics();

    // === Disposal ===

    /// <inheritdoc />
    public void Dispose()
        => _innerServer.Dispose();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
        => await _innerServer.DisposeAsync();
}
