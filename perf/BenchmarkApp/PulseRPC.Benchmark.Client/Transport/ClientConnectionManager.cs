using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

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

            // 创建新连接
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
            TotalConnections = totalConnections,
            ActiveConnections = activeConnections,
            InactiveConnections = totalConnections - activeConnections
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

            // 这里应该实现实际的PulseRPC连接逻辑
            // 目前使用模拟实现
            await Task.Delay(100, cancellationToken); // 模拟连接时间

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

            // 这里应该实现实际的连接关闭逻辑
            await Task.Delay(50); // 模拟断开时间

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

            // 这里应该实现实际的请求发送逻辑
            await Task.Delay(Random.Shared.Next(1, 10), cancellationToken);

            // 模拟响应
            return default(TResponse)!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送请求失败: {ConnectionId}", ConnectionId);
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
public class ConnectionStatistics
{
    public int TotalConnections { get; set; }
    public int ActiveConnections { get; set; }
    public int InactiveConnections { get; set; }
}
