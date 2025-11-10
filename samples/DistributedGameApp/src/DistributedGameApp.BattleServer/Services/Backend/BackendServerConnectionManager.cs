using DistributedGameApp.Infrastructure.Consul;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace DistributedGameApp.BattleServer.Services.Backend;

/// <summary>
/// BackendServer 连接管理器 - 负责管理到多个 BackendServer 的连接池
/// </summary>
public class BackendServerConnectionManager : IAsyncDisposable
{
    private readonly ConsulServiceDiscovery _serviceDiscovery;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<BackendServerConnectionManager> _logger;
    private readonly ConcurrentDictionary<string, BackendServerConnection> _connections = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly string _serviceType;

    private Task? _serviceWatchTask;
    private bool _isInitialized;
    private bool _isDisposed;

    /// <summary>
    /// 连接变更事件（用于通知路由器更新）
    /// </summary>
    public event EventHandler<ConnectionChangeEvent>? ConnectionChanged;

    public BackendServerConnectionManager(
        ConsulServiceDiscovery serviceDiscovery,
        ILoggerFactory loggerFactory,
        string serviceType = "BackendServer")
    {
        _serviceDiscovery = serviceDiscovery ?? throw new ArgumentNullException(nameof(serviceDiscovery));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _serviceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
        _logger = loggerFactory.CreateLogger<BackendServerConnectionManager>();
    }

    /// <summary>
    /// 初始化连接管理器
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            _logger.LogWarning("BackendServerConnectionManager 已经初始化");
            return;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized)
                return;

            _logger.LogInformation("初始化 BackendServerConnectionManager (类型: {ServiceType})...", _serviceType);

            // 1. 从 Consul 获取所有服务实例
            var services = await _serviceDiscovery.GetServicesAsync(_serviceType, cancellationToken);

            _logger.LogInformation("发现 {Count} 个 {ServiceType} 实例", services.Count, _serviceType);

            // 2. 为每个服务建立连接
            var connectTasks = services.Select(async service =>
            {
                try
                {
                    await AddConnectionAsync(service, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "初始化连接失败: {ServiceId}", service.ServiceId);
                }
            });

            await Task.WhenAll(connectTasks);

            // 3. 启动服务监听任务
            _serviceWatchTask = WatchServicesAsync(_cts.Token);

            _isInitialized = true;
            _logger.LogInformation("BackendServerConnectionManager 初始化完成，已连接 {Count} 个实例",
                _connections.Count);
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// 获取指定服务的连接
    /// </summary>
    public BackendServerConnection? GetConnection(string serviceId)
    {
        if (string.IsNullOrEmpty(serviceId))
            throw new ArgumentException("ServiceId cannot be null or empty", nameof(serviceId));

        _connections.TryGetValue(serviceId, out var connection);
        return connection;
    }

    /// <summary>
    /// 获取所有可用的连接
    /// </summary>
    public List<BackendServerConnection> GetAllConnections()
    {
        return _connections.Values
            .Where(c => c.State == ConnectionState.Connected)
            .ToList();
    }

    /// <summary>
    /// 获取连接数量
    /// </summary>
    public int ConnectionCount => _connections.Count;

    /// <summary>
    /// 添加连接
    /// </summary>
    private async Task AddConnectionAsync(ServiceRegistration service, CancellationToken cancellationToken)
    {
        if (_connections.ContainsKey(service.ServiceId))
        {
            _logger.LogDebug("连接已存在: {ServiceId}", service.ServiceId);
            return;
        }

        _logger.LogInformation("添加 BackendServer 连接: {ServiceId} at {Host}:{Port}",
            service.ServiceId, service.Host, service.TcpPort);

        var connectionLogger = _loggerFactory.CreateLogger<BackendServerConnection>();
        var connection = new BackendServerConnection(service, connectionLogger);

        // 订阅连接状态变更
        connection.StateChanged += OnConnectionStateChanged;

        // 建立连接
        var connected = await connection.ConnectAsync(cancellationToken);

        if (connected)
        {
            _connections[service.ServiceId] = connection;
            ConnectionChanged?.Invoke(this, new ConnectionChangeEvent(
                ConnectionChangeType.Added, service.ServiceId, connection));

            _logger.LogInformation("成功添加连接: {ServiceId}", service.ServiceId);
        }
        else
        {
            _logger.LogWarning("连接失败: {ServiceId}", service.ServiceId);
            await connection.DisposeAsync();
        }
    }

    /// <summary>
    /// 移除连接
    /// </summary>
    private async Task RemoveConnectionAsync(string serviceId)
    {
        if (_connections.TryRemove(serviceId, out var connection))
        {
            _logger.LogInformation("移除 BackendServer 连接: {ServiceId}", serviceId);

            connection.StateChanged -= OnConnectionStateChanged;

            await connection.DisposeAsync();

            ConnectionChanged?.Invoke(this, new ConnectionChangeEvent(
                ConnectionChangeType.Removed, serviceId, null));

            _logger.LogInformation("成功移除连接: {ServiceId}", serviceId);
        }
    }

    /// <summary>
    /// 监听 Consul 服务变更
    /// </summary>
    private async Task WatchServicesAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("开始监听 {ServiceType} 服务变更...", _serviceType);

        try
        {
            await _serviceDiscovery.WatchServicesAsync(
                _serviceType,
                async (changeType, service) =>
                {
                    if (service == null)
                        return;

                    try
                    {
                        switch (changeType)
                        {
                            case ServiceChangeType.Added:
                                _logger.LogInformation("检测到新服务: {ServiceId}", service.ServiceId);
                                await AddConnectionAsync(service, cancellationToken);
                                break;

                            case ServiceChangeType.Removed:
                                _logger.LogInformation("检测到服务移除: {ServiceId}", service.ServiceId);
                                await RemoveConnectionAsync(service.ServiceId);
                                break;

                            case ServiceChangeType.Modified:
                                _logger.LogInformation("检测到服务变更: {ServiceId}", service.ServiceId);
                                // 重新建立连接
                                await RemoveConnectionAsync(service.ServiceId);
                                await AddConnectionAsync(service, cancellationToken);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "处理服务变更失败: {ServiceId}, {ChangeType}",
                            service.ServiceId, changeType);
                    }
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("服务监听已停止");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "服务监听异常");
        }
    }

    /// <summary>
    /// 连接状态变更处理
    /// </summary>
    private void OnConnectionStateChanged(object? sender, ConnectionState newState)
    {
        if (sender is BackendServerConnection connection)
        {
            _logger.LogInformation("连接状态变更: {ServiceId} -> {State}",
                connection.ServiceInfo.ServiceId, newState);

            // 如果连接断开，尝试重连
            if (newState == ConnectionState.Disconnected)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));

                    try
                    {
                        _logger.LogInformation("尝试重连: {ServiceId}", connection.ServiceInfo.ServiceId);
                        await connection.ConnectAsync(_cts.Token);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "重连失败: {ServiceId}", connection.ServiceInfo.ServiceId);
                    }
                });
            }
        }
    }

    /// <summary>
    /// 获取连接统计信息
    /// </summary>
    public ConnectionManagerStats GetStats()
    {
        var connections = _connections.Values.ToList();

        return new ConnectionManagerStats
        {
            TotalConnections = connections.Count,
            ConnectedCount = connections.Count(c => c.State == ConnectionState.Connected),
            DisconnectedCount = connections.Count(c => c.State == ConnectionState.Disconnected),
            ConnectingCount = connections.Count(c => c.State == ConnectionState.Connecting),
            TotalRequests = connections.Sum(c => c.RequestCount)
        };
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        _logger.LogInformation("正在释放 BackendServerConnectionManager...");

        // 停止服务监听
        await _cts.CancelAsync();

        if (_serviceWatchTask != null)
        {
            try
            {
                await _serviceWatchTask;
            }
            catch
            {
                // Ignore
            }
        }

        // 断开所有连接
        var disconnectTasks = _connections.Values.Select(c => c.DisposeAsync().AsTask());
        await Task.WhenAll(disconnectTasks);

        _connections.Clear();

        _initLock.Dispose();
        _cts.Dispose();

        _logger.LogInformation("BackendServerConnectionManager 已释放");
    }
}

/// <summary>
/// 连接变更事件
/// </summary>
public class ConnectionChangeEvent
{
    public ConnectionChangeType Type { get; }
    public string ServiceId { get; }
    public BackendServerConnection? Connection { get; }

    public ConnectionChangeEvent(
        ConnectionChangeType type,
        string serviceId,
        BackendServerConnection? connection)
    {
        Type = type;
        ServiceId = serviceId;
        Connection = connection;
    }
}

/// <summary>
/// 连接变更类型
/// </summary>
public enum ConnectionChangeType
{
    Added,
    Removed,
    Modified
}

/// <summary>
/// 连接管理器统计信息
/// </summary>
public class ConnectionManagerStats
{
    /// <summary>
    /// 总连接数
    /// </summary>
    public int TotalConnections { get; init; }

    /// <summary>
    /// 已连接数量
    /// </summary>
    public int ConnectedCount { get; init; }

    /// <summary>
    /// 断开连接数量
    /// </summary>
    public int DisconnectedCount { get; init; }

    /// <summary>
    /// 连接中数量
    /// </summary>
    public int ConnectingCount { get; init; }

    /// <summary>
    /// 总请求数
    /// </summary>
    public long TotalRequests { get; init; }
}
