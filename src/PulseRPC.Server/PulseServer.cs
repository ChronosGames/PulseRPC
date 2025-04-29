using Microsoft.Extensions.Logging;
using PulseRPC.Internal;
using PulseRPC.Protocol;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Server.Auth;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using PulseRPC.Server.Monitoring;

namespace PulseRPC.Server;

/// <summary>
/// Main server class for PulseRPC.
/// Listens for incoming connections and manages client sessions.
/// </summary>
public class PulseServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly ServiceRegistry _serviceRegistry;
    private readonly HubRegistry _hubRegistry;
    private readonly ILogger _logger;
    private readonly IAuthenticationProvider? _authenticationProvider;
    private readonly IAuthorizationProvider? _authorizationProvider;
    private readonly X509Certificate2? _serverCertificate;
    private readonly bool _useEncryption;
    private readonly RemoteCertificateValidationCallback? _clientCertificateValidation;
    private readonly MetricsCollector? _metricsCollector;
    private bool _isRunning;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, ClientSession> _activeSessions = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PulseServer"/> class.
    /// </summary>
    /// <param name="endpoint">The IP endpoint to listen on.</param>
    /// <param name="logger">Logger instance.</param>
    public PulseServer(IPEndPoint endpoint, ILogger logger)
    {
        _listener = new TcpListener(endpoint);
        _serviceRegistry = new ServiceRegistry();
        _hubRegistry = new HubRegistry(this); // Pass server reference for broadcasting
        _logger = logger;
    }

    /// <summary>
    /// 使用SSL/TLS加密初始化PulseServer
    /// </summary>
    /// <param name="endpoint">监听端点</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="serverCertificate">服务器证书</param>
    /// <param name="clientCertificateValidation">客户端证书验证回调</param>
    public PulseServer(
        IPEndPoint endpoint,
        ILogger logger,
        X509Certificate2 serverCertificate,
        RemoteCertificateValidationCallback? clientCertificateValidation = null)
        : this(endpoint, logger)
    {
        _serverCertificate = serverCertificate ?? throw new ArgumentNullException(nameof(serverCertificate));
        _useEncryption = true;
        _clientCertificateValidation = clientCertificateValidation;
    }

    /// <summary>
    /// 使用加密和认证/授权初始化PulseServer
    /// </summary>
    public PulseServer(
        IPEndPoint endpoint,
        ILogger logger,
        X509Certificate2 serverCertificate,
        IAuthenticationProvider authenticationProvider,
        IAuthorizationProvider? authorizationProvider = null,
        RemoteCertificateValidationCallback? clientCertificateValidation = null)
        : this(endpoint, logger)
    {
        _serverCertificate = serverCertificate ?? throw new ArgumentNullException(nameof(serverCertificate));
        _authenticationProvider = authenticationProvider ?? throw new ArgumentNullException(nameof(authenticationProvider));
        _authorizationProvider = authorizationProvider;
        _useEncryption = true;
        _clientCertificateValidation = clientCertificateValidation;
    }

    /// <summary>
    /// 使用指定的组件初始化 PulseServer
    /// </summary>
    public PulseServer(
        IPEndPoint endpoint,
        ILogger logger,
        MetricsCollector? metricsCollector = null)
        : this(endpoint, logger)
    {
        _metricsCollector = metricsCollector;
    }

    /// <summary>
    /// Registers a service implementation.
    /// </summary>
    /// <typeparam name="TService">The service interface type.</typeparam>
    /// <typeparam name="TImplementation">The service implementation type.</typeparam>
    /// <param name="implementation">The service implementation instance.</param>
    public void RegisterService<TService, TImplementation>(TImplementation implementation)
        where TService : class, IPulseService<TService>
        where TImplementation : class, TService
    {
        _serviceRegistry.Register<TService>(implementation);
        _logger.LogInformation("Registered service: {ServiceName}", typeof(TService).FullName);
    }

    /// <summary>
    /// Registers a service implementation where the implementation directly implements the service interface.
    /// </summary>
    /// <typeparam name="TService">The service interface type.</typeparam>
    /// <param name="implementation">The service implementation instance.</param>
    public void RegisterService<TService>(TService implementation)
        where TService : class, IPulseService<TService>
    {
        RegisterService<TService, TService>(implementation);
    }

    /// <summary>
    /// Registers a Hub implementation.
    /// </summary>
    /// <typeparam name="THub">The Hub interface type.</typeparam>
    /// <typeparam name="TReceiver">The Hub receiver interface type.</typeparam>
    /// <param name="implementationType">The Hub implementation type.</param>
    public void RegisterHub<THub, TReceiver>(Type implementationType)
        where THub : class, IPulseHub<THub, TReceiver>
        where TReceiver : class
    {
        _hubRegistry.Register<THub, TReceiver>(implementationType);
        _logger.LogInformation("Registered hub: {HubName}", typeof(THub).FullName);
    }

    /// <summary>
    /// Starts the server and begins listening for connections.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to stop the server.</param>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener.Start();
        _isRunning = true;
        _logger.LogInformation("PulseRPC server started on {Endpoint}", _listener.LocalEndpoint);

        try
        {
            while (_isRunning && !_cts.Token.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                // Don't wait for HandleClientAsync to complete
                _ = HandleClientAsync(client, _cts.Token);
            }
        }
        catch (OperationCanceledException) when (_cts.Token.IsCancellationRequested)
        {
            _logger.LogInformation("Server shutting down.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accepting client connections");
        }
        finally
        {
            _listener.Stop();
            _isRunning = false;
            _logger.LogInformation("PulseRPC server stopped.");
            // Clean up remaining sessions
            await CleanupSessionsAsync();
        }
    }

    /// <summary>
    /// Stops the server.
    /// </summary>
    public void Stop()
    {
        _isRunning = false;
        _cts?.Cancel();
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var clientId = Guid.NewGuid().ToString();
        var clientStream = client.GetStream();

        // 记录新连接指标
        PulseMetrics.RecordConnection();

        // 如果启用了加密，使用SSL包装网络流
        Stream securedStream = clientStream;
        if (_useEncryption && _serverCertificate != null)
        {
            var sslStream = new SslStream(
                clientStream,
                false,
                _clientCertificateValidation);

            try
            {
                await sslStream.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _serverCertificate,
                        ClientCertificateRequired = _clientCertificateValidation != null,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        CertificateRevocationCheckMode = X509RevocationMode.Online
                    },
                    cancellationToken);

                securedStream = sslStream;
                _logger.LogInformation("Client {ClientId} established secure connection using {Protocol}",
                    clientId, sslStream.SslProtocol);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to establish secure connection with client {ClientId}", clientId);
                sslStream.Dispose();
                client.Dispose();
                return;
            }
        }

        var clientSession = new ClientSession(
            client,
            clientId,
            _serviceRegistry,
            _hubRegistry,
            _logger,
            OnSessionDisconnected,
            _authenticationProvider,
            _authorizationProvider,
            securedStream); // 传递加密流

        if (!_activeSessions.TryAdd(clientId, clientSession))
        {
            _logger.LogWarning("Failed to add client session {ClientId}", clientId);
            await clientSession.DisposeAsync();
            return;
        }

        _logger.LogInformation("Client {ClientId} connected from {RemoteEndPoint}", clientId, client.Client.RemoteEndPoint);

        try
        {
            await clientSession.ProcessAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
             _logger.LogDebug("Processing canceled for client {ClientId}", clientId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing client {ClientId}", clientId);
        }
        finally
        {
             // Session cleanup is handled by OnSessionDisconnected callback
        }
    }

    private async void OnSessionDisconnected(string clientId)
    {
        if (_activeSessions.TryRemove(clientId, out var session))
        {
            _logger.LogInformation("Client {ClientId} disconnected.", clientId);
            // Perform any additional cleanup related to the session if needed
            await session.DisposeAsync();
        }
    }

     private async Task CleanupSessionsAsync()
    {
        var cleanupTasks = _activeSessions.Values.Select(session => session.DisposeAsync().AsTask()).ToList();
        _activeSessions.Clear();
        await Task.WhenAll(cleanupTasks);
        _logger.LogInformation("All active client sessions cleaned up.");
    }

    /// <summary>
    /// Broadcasts an event to all clients connected to a specific Hub group.
    /// </summary>
    internal async Task BroadcastToGroupAsync(string groupName, PulseEvent eventPacket, CancellationToken cancellationToken = default)
    {
        var sessionsInGroup = _activeSessions.Values.Where(s => s.IsInGroup(groupName));
        var broadcastTasks = sessionsInGroup.Select(session => session.SendEventAsync(eventPacket, cancellationToken));
        await Task.WhenAll(broadcastTasks);
    }

    /// <summary>
    /// Sends an event to a specific client.
    /// </summary>
    internal async Task SendToClientAsync(string clientId, PulseEvent eventPacket, CancellationToken cancellationToken = default)
    {
        if (_activeSessions.TryGetValue(clientId, out var session))
        {
            await session.SendEventAsync(eventPacket, cancellationToken);
        }
    }

    /// <summary>
    /// Sends an event to all connected clients (excluding those in specified groups, if any).
    /// </summary>
    internal async Task BroadcastToAllAsync(PulseEvent eventPacket, IEnumerable<string>? excludedGroups = null, CancellationToken cancellationToken = default)
    {
        var excludedClientIds = new HashSet<string>();
        if (excludedGroups != null)
        {
            foreach (var groupName in excludedGroups)
            {
                 foreach (var session in _activeSessions.Values.Where(s => s.IsInGroup(groupName)))
                 {
                     excludedClientIds.Add(session.ClientId);
                 }
            }
        }

        var targetSessions = _activeSessions.Values.Where(s => !excludedClientIds.Contains(s.ClientId));
        var broadcastTasks = targetSessions.Select(session => session.SendEventAsync(eventPacket, cancellationToken));
        await Task.WhenAll(broadcastTasks);
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources asynchronously.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        Stop();
        await CleanupSessionsAsync();
        _cts?.Dispose();
    }
}
