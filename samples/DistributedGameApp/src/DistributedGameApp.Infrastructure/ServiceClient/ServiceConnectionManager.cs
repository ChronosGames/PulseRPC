using DistributedGameApp.Infrastructure.Consul;
using Microsoft.Extensions.Logging;
using PulseRPC.Authentication;
using System.Collections.Concurrent;

namespace DistributedGameApp.Infrastructure.ServiceClient;

/// <summary>
/// 通用服务连接管理器
/// </summary>
/// <remarks>
/// 负责管理与特定服务类型的所有连接实例
/// </remarks>
public class ServiceConnectionManager : IAsyncDisposable
{
    private readonly ConsulServiceDiscovery _serviceDiscovery;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ServiceConnectionManager> _logger;
    private readonly string _serviceTypeName; // "BackendServer", "BattleServer", "GameServer" etc.
    private readonly IAuthenticationProvider? _authenticationProvider;

    private readonly ConcurrentDictionary<string, ServiceConnection> _connections = new();
    private bool _isInitialized;
    private bool _isDisposed;

    public ServiceConnectionManager(
        ConsulServiceDiscovery serviceDiscovery,
        ILoggerFactory loggerFactory,
        string serviceTypeName,
        IAuthenticationProvider? authenticationProvider = null)
    {
        _serviceDiscovery = serviceDiscovery ?? throw new ArgumentNullException(nameof(serviceDiscovery));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<ServiceConnectionManager>();
        _serviceTypeName = serviceTypeName ?? throw new ArgumentNullException(nameof(serviceTypeName));
        _authenticationProvider = authenticationProvider;
    }

    /// <summary>
    /// 连接数量
    /// </summary>
    public int ConnectionCount => _connections.Count;

    /// <summary>
    /// 初始化 - 从 Consul 发现服务并建立连接（带重试）
    /// </summary>
    /// <param name="maxRetries">最大重试次数（默认3次）</param>
    /// <param name="retryDelayMs">重试延迟（默认1000ms）</param>
    /// <param name="allowEmpty">是否允许服务列表为空（默认false，为true时即使没有服务也继续）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task InitializeAsync(
        int maxRetries = 3,
        int retryDelayMs = 1000,
        bool allowEmpty = false,
        CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            _logger.LogWarning("ServiceConnectionManager 已经初始化: {ServiceType}", _serviceTypeName);
            return;
        }

        _logger.LogInformation("正在初始化 ServiceConnectionManager: {ServiceType}", _serviceTypeName);

        var retryCount = 0;
        List<Consul.ServiceRegistration>? services = null;

        // 重试逻辑 - 发现服务
        while (retryCount <= maxRetries)
        {
            try
            {
                // 从 Consul 获取服务列表
                services = await _serviceDiscovery.GetServicesAsync(_serviceTypeName, cancellationToken);

                if (services != null && services.Count > 0)
                {
                    _logger.LogInformation("发现 {Count} 个服务实例: {ServiceType}", services.Count, _serviceTypeName);
                    break;
                }

                if (retryCount < maxRetries)
                {
                    _logger.LogWarning("未发现任何服务实例: {ServiceType}，第 {RetryCount}/{MaxRetries} 次重试...",
                        _serviceTypeName, retryCount + 1, maxRetries);
                    await Task.Delay(retryDelayMs, cancellationToken);
                    retryCount++;
                }
                else
                {
                    if (allowEmpty)
                    {
                        _logger.LogWarning("未发现任何服务实例: {ServiceType}，允许空连接，初始化完成",
                            _serviceTypeName);
                    }
                    else
                    {
                        _logger.LogError("未发现任何服务实例: {ServiceType}，已达到最大重试次数 {MaxRetries}",
                            _serviceTypeName, maxRetries);
                    }
                    break;
                }
            }
            catch (Exception ex)
            {
                if (retryCount < maxRetries)
                {
                    _logger.LogWarning(ex, "服务发现失败: {ServiceType}，第 {RetryCount}/{MaxRetries} 次重试...",
                        _serviceTypeName, retryCount + 1, maxRetries);
                    await Task.Delay(retryDelayMs, cancellationToken);
                    retryCount++;
                }
                else
                {
                    _logger.LogError(ex, "服务发现失败: {ServiceType}，已达到最大重试次数 {MaxRetries}",
                        _serviceTypeName, maxRetries);
                    throw;
                }
            }
        }

        // 建立连接
        if (services != null && services.Count > 0)
        {
            await ConnectToServicesAsync(services, cancellationToken);
        }

        _isInitialized = true;
    }

    /// <summary>
    /// 刷新服务列表 - 从 Consul 重新发现服务并建立连接
    /// </summary>
    public async Task RefreshServicesAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            _logger.LogWarning("ServiceConnectionManager 未初始化，无法刷新: {ServiceType}", _serviceTypeName);
            return;
        }

        _logger.LogInformation("正在刷新服务列表: {ServiceType}", _serviceTypeName);

        try
        {
            // 从 Consul 获取最新的服务列表
            var services = await _serviceDiscovery.GetServicesAsync(_serviceTypeName, cancellationToken);

            if (services == null || services.Count == 0)
            {
                _logger.LogWarning("刷新服务列表时未发现任何服务实例: {ServiceType}", _serviceTypeName);
                return;
            }

            var currentServiceIds = _connections.Keys.ToHashSet();
            var newServiceIds = services.Select(s => s.ServiceId).ToHashSet();

            // 移除已失效的连接
            var removedIds = currentServiceIds.Except(newServiceIds).ToList();
            foreach (var serviceId in removedIds)
            {
                if (_connections.TryRemove(serviceId, out var connection))
                {
                    _logger.LogInformation("移除失效的服务连接: {ServiceId}", serviceId);
                    await connection.DisposeAsync();
                }
            }

            // 添加新的连接
            var addedServices = services.Where(s => !currentServiceIds.Contains(s.ServiceId)).ToList();
            if (addedServices.Count > 0)
            {
                _logger.LogInformation("发现 {Count} 个新的服务实例: {ServiceType}", addedServices.Count, _serviceTypeName);
                await ConnectToServicesAsync(addedServices, cancellationToken);
            }

            _logger.LogInformation("服务列表刷新完成: {ServiceType}, 当前连接数: {Count}",
                _serviceTypeName, _connections.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刷新服务列表失败: {ServiceType}", _serviceTypeName);
        }
    }

    /// <summary>
    /// 连接到服务列表
    /// </summary>
    private async Task ConnectToServicesAsync(List<Consul.ServiceRegistration> services, CancellationToken cancellationToken)
    {
        // 并行建立所有连接
        var connectionTasks = services.Select(async service =>
        {
            try
            {
                var connectionLogger = _loggerFactory.CreateLogger<ServiceConnection>();
                var connection = new ServiceConnection(service, connectionLogger, _authenticationProvider);

                var success = await connection.ConnectAsync(cancellationToken);

                if (success)
                {
                    _connections[service.ServiceId] = connection;
                    _logger.LogInformation("连接成功: {ServiceId} ({Host}:{Port})",
                        service.ServiceId, service.Host, service.TcpPort);
                }
                else
                {
                    _logger.LogWarning("连接失败: {ServiceId} ({Host}:{Port})",
                        service.ServiceId, service.Host, service.TcpPort);
                    await connection.DisposeAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "连接异常: {ServiceId}", service.ServiceId);
            }
        });

        await Task.WhenAll(connectionTasks);

        _logger.LogInformation("ServiceConnectionManager 连接完成: {ServiceType}, 成功连接: {Count}/{Total}",
            _serviceTypeName, _connections.Count, services.Count);
    }

    /// <summary>
    /// 获取指定服务的连接
    /// </summary>
    public ServiceConnection? GetConnection(string serviceId)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(ServiceConnectionManager));

        return _connections.TryGetValue(serviceId, out var connection) ? connection : null;
    }

    /// <summary>
    /// 获取所有连接
    /// </summary>
    public List<ServiceConnection> GetAllConnections()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(ServiceConnectionManager));

        return _connections.Values
            .Where(c => c.State == ConnectionState.Connected)
            .ToList();
    }

    /// <summary>
    /// 添加连接
    /// </summary>
    public bool TryAddConnection(ServiceConnection connection)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(ServiceConnectionManager));

        return _connections.TryAdd(connection.ServiceInfo.ServiceId, connection);
    }

    /// <summary>
    /// 移除连接
    /// </summary>
    public async Task<bool> RemoveConnectionAsync(string serviceId)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(ServiceConnectionManager));

        if (_connections.TryRemove(serviceId, out var connection))
        {
            await connection.DisposeAsync();
            _logger.LogInformation("连接已移除: {ServiceId}", serviceId);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 获取统计信息
    /// </summary>
    public ServiceConnectionStats GetStats()
    {
        var connections = _connections.Values.ToList();

        return new ServiceConnectionStats
        {
            ServiceTypeName = _serviceTypeName,
            TotalConnections = connections.Count,
            ConnectedCount = connections.Count(c => c.State == ConnectionState.Connected),
            DisconnectedCount = connections.Count(c => c.State == ConnectionState.Disconnected),
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

        _logger.LogInformation("正在释放 ServiceConnectionManager: {ServiceType}", _serviceTypeName);

        // 并行释放所有连接
        var disposeTasks = _connections.Values.Select(async connection =>
        {
            try
            {
                await connection.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放连接异常: {ServiceId}", connection.ServiceInfo.ServiceId);
            }
        });

        await Task.WhenAll(disposeTasks);

        _connections.Clear();

        _logger.LogInformation("ServiceConnectionManager 已释放: {ServiceType}", _serviceTypeName);
    }
}

/// <summary>
/// 连接统计信息
/// </summary>
public class ServiceConnectionStats
{
    /// <summary>
    /// 服务类型名称
    /// </summary>
    public string ServiceTypeName { get; init; } = string.Empty;

    /// <summary>
    /// 总连接数
    /// </summary>
    public int TotalConnections { get; init; }

    /// <summary>
    /// 已连接数
    /// </summary>
    public int ConnectedCount { get; init; }

    /// <summary>
    /// 断开连接数
    /// </summary>
    public int DisconnectedCount { get; init; }

    /// <summary>
    /// 总请求数
    /// </summary>
    public long TotalRequests { get; init; }
}
