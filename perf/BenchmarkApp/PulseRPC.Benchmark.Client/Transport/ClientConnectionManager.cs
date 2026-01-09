using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Shared;
using PulseRPC.Client;
using PulseRPC.Benchmark.Shared.Models;
using PulseRPC.Transport;

namespace PulseRPC.Benchmark.Client.Transport;

/// <summary>
/// 客户端连接管理器
/// </summary>
public class ClientConnectionManager(ILogger<ClientConnectionManager> logger)
{
    private readonly ILogger<ClientConnectionManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ConcurrentDictionary<string, ClientConnection> _connections = new();
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
    private volatile bool _disposed;

    /// <summary>
    /// 创建连接
    /// </summary>
    public async Task<ClientConnection> CreateConnectionAsync(
        string serverAddress,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ClientConnectionManager));

        if (string.IsNullOrEmpty(serverAddress))
            throw new ArgumentException("服务器地址不能为空", nameof(serverAddress));

        if (string.IsNullOrEmpty(connectionId))
            throw new ArgumentException("连接ID不能为空", nameof(connectionId));

        await _connectionSemaphore.WaitAsync(cancellationToken);
        try
        {
            // 如果连接已存在且有效，直接返回
            if (_connections.TryGetValue(connectionId, out var existingConnection) &&
                existingConnection.IsConnected)
            {
                return existingConnection;
            }

            _logger.LogDebug("创建新连接: {ConnectionId} -> {ServerAddress}", connectionId, serverAddress);

            // 解析服务器地址
            var (host, port) = ParseServerAddress(serverAddress);

            // 创建新连接（真实 PulseRPC 客户端 + 服务代理）
            var connection = new ClientConnection(connectionId, host, port, _logger);
            await connection.ConnectAsync(cancellationToken);

            // 添加到连接池
            _connections.AddOrUpdate(connectionId, connection, (_, old) =>
            {
                old?.Dispose();
                return connection;
            });

            _logger.LogInformation("连接创建成功: {ConnectionId}", connectionId);
            return connection;
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    /// <summary>
    /// 获取连接
    /// </summary>
    public ClientConnection? GetConnection(string connectionId)
    {
        if (_disposed)
            return null;

        return _connections.TryGetValue(connectionId, out var connection) && connection.IsConnected
            ? connection
            : null;
    }

    /// <summary>
    /// 关闭指定连接
    /// </summary>
    public async Task CloseConnectionAsync(string connectionId)
    {
        if (_disposed)
            return;

        if (_connections.TryRemove(connectionId, out var connection))
        {
            await connection.DisconnectAsync();
            _logger.LogInformation("连接已关闭: {ConnectionId}", connectionId);
        }
    }

    /// <summary>
    /// 关闭所有连接
    /// </summary>
    public async Task CloseAllConnectionsAsync()
    {
        if (_disposed)
            return;

        _logger.LogInformation("关闭所有连接...");

        var tasks = new List<Task>();
        foreach (var connection in _connections.Values)
        {
            tasks.Add(connection.DisconnectAsync());
        }

        await Task.WhenAll(tasks);
        _connections.Clear();

        _logger.LogInformation("所有连接已关闭");
    }

    /// <summary>
    /// 获取连接统计信息
    /// </summary>
    public ConnectionStatistics GetStatistics()
    {
        var totalConnections = _connections.Count;
        var activeConnections = 0;

        foreach (var connection in _connections.Values)
        {
            if (connection.IsConnected)
                activeConnections++;
        }

        return new ConnectionStatistics
        {
        };
    }

    /// <summary>
    /// 解析服务器地址
    /// </summary>
    private static (string host, int port) ParseServerAddress(string serverAddress)
    {
        var parts = serverAddress.Split(':');
        if (parts.Length != 2)
        {
            throw new ArgumentException($"无效的服务器地址格式: {serverAddress}. 期望格式: host:port");
        }

        var host = parts[0].Trim();
        if (string.IsNullOrEmpty(host))
        {
            throw new ArgumentException("主机名不能为空");
        }

        if (!int.TryParse(parts[1].Trim(), out var port) || port <= 0 || port > 65535)
        {
            throw new ArgumentException($"无效的端口号: {parts[1]}");
        }

        return (host, port);
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            CloseAllConnectionsAsync().Wait(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "关闭连接时发生错误");
        }

        _connectionSemaphore.Dispose();
    }
}

/// <summary>
/// 客户端连接
/// </summary>
public class ClientConnection : IDisposable
{
    private readonly ILogger _logger;
    private volatile bool _isConnected;
    private volatile bool _disposed;

    private IPulseClient? _pulseClient;
    private IBenchmarkHub? _service;

    public string ConnectionId { get; }
    public string Host { get; }
    public int Port { get; }
    public bool IsConnected => _isConnected && !_disposed;
    public DateTime ConnectedAt { get; private set; }
    public DateTime LastActivityAt { get; private set; }

    public ClientConnection(string connectionId, string host, int port, ILogger logger)
    {
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        Host = host ?? throw new ArgumentNullException(nameof(host));
        Port = port;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 建立连接
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ClientConnection));

        if (_isConnected)
            return;

        try
        {
            _logger.LogDebug("连接到服务器: {Host}:{Port}", Host, Port);

            // 创建 PulseRPC 客户端 - 基于指南文档的最佳实践
            _pulseClient = new PulseClientBuilder()
                .AddTcpConnection("123", "UnknownServer", Host, Port, ConnectionStrategy.Persistent)
                .WithTransportOptions(TransportType.TCP, new TcpTransportOptions()
                {
                    ConnectionTimeout = 5000,
                    NoDelay = true,
                    SendBufferSize = 8192,
                    RecvBufferSize = 8192,
                })
                .Build();

            // 初始化并连接
            await _pulseClient.InitializeAsync(cancellationToken);

            // 获取服务代理
            _service = await _pulseClient.GetServiceAsync<IBenchmarkHub>();

            _isConnected = true;
            ConnectedAt = DateTime.UtcNow;
            LastActivityAt = DateTime.UtcNow;

            _logger.LogDebug("连接建立成功: {ConnectionId}", ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "连接失败: {ConnectionId}", ConnectionId);
            throw;
        }
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (!_isConnected || _disposed)
            return;

        try
        {
            _logger.LogDebug("断开连接: {ConnectionId}", ConnectionId);

            if (_pulseClient != null)
            {
                await _pulseClient.StopAsync();
                _pulseClient = null;
            }
            _service = null;

            _isConnected = false;
            _logger.LogDebug("连接已断开: {ConnectionId}", ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "断开连接时发生错误: {ConnectionId}", ConnectionId);
        }
    }

    /// <summary>
    /// 发送请求（模拟）
    /// </summary>
    public async Task<TResponse> SendRequestAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException($"连接未建立: {ConnectionId}");

        try
        {
            LastActivityAt = DateTime.UtcNow;

            if (_service == null)
                throw new InvalidOperationException("服务代理未初始化");

            object? responseObj = null;

            // 根据请求类型路由到基准服务方法
            switch (request)
            {
                case PingRequest pingReq:
                    responseObj = await _service.PingAsync(pingReq);
                    break;
                case EchoRequest echoReq:
                    responseObj = await _service.EchoAsync(echoReq, cancellationToken);
                    break;
                case ThroughputTestRequest throughputReq:
                    responseObj = await _service.ThroughputTestAsync(throughputReq, cancellationToken);
                    break;
                case StreamTestRequest streamReq:
                    responseObj = await _service.StreamTestAsync(streamReq, cancellationToken);
                    break;
                case UploadRequest uploadReq:
                    responseObj = await _service.UploadAsync(uploadReq, cancellationToken);
                    break;
                case DownloadRequest downloadReq:
                    responseObj = await _service.DownloadAsync(downloadReq, cancellationToken);
                    break;
                default:
                    throw new NotSupportedException($"不支持的请求类型: {typeof(TRequest).Name}");
            }

            return (TResponse)responseObj!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送请求失败: {ConnectionId}", ConnectionId);
            throw;
        }
    }

    /// <summary>
    /// 发送 Notify 请求（无返回值，Fire-and-Forget）
    /// </summary>
    public async ValueTask SendNotifyAsync(NotifyRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException($"连接未建立: {ConnectionId}");

        try
        {
            LastActivityAt = DateTime.UtcNow;

            if (_service == null)
                throw new InvalidOperationException("服务代理未初始化");

            await _service.NotifyAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "发送 Notify 失败: {ConnectionId}", ConnectionId);
            throw;
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            DisconnectAsync().Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "释放连接资源时发生错误: {ConnectionId}", ConnectionId);
        }
    }
}

/// <summary>
/// 连接统计信息
/// </summary>
// public class ConnectionStatistics
// {
//     public int TotalConnections { get; set; }
//     public int ActiveConnections { get; set; }
//     public int InactiveConnections { get; set; }
// }
