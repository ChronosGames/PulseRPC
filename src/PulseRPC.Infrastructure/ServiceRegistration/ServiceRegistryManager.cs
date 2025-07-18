using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Cluster;
using System.Collections.Concurrent;
using PulseRPC.HealthCheck;

namespace PulseRPC.ServiceRegistration;

/// <summary>
/// 服务注册管理器配置选项
/// </summary>
public class ServiceRegistryOptions
{
    /// <summary>
    /// 配置节名称
    /// </summary>
    public const string SectionName = "ServiceRegistry";

    /// <summary>
    /// 是否启用服务注册
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 健康检查间隔
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 服务注册重试次数
    /// </summary>
    public int RegistrationRetryCount { get; set; } = 3;

    /// <summary>
    /// 服务注册重试延迟
    /// </summary>
    public TimeSpan RegistrationRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 是否在启动时自动注册所有服务
    /// </summary>
    public bool AutoRegisterOnStartup { get; set; } = true;

    /// <summary>
    /// 是否在停止时自动注销所有服务
    /// </summary>
    public bool AutoUnregisterOnShutdown { get; set; } = true;

    /// <summary>
    /// 注册失败时是否继续启动服务
    /// </summary>
    public bool ContinueOnRegistrationFailure { get; set; } = true;
}

/// <summary>
/// 服务注册管理器 - 处理服务的注册、注销、健康检查等逻辑
/// </summary>
public sealed class ServiceRegistryManager : BackgroundService
{
    private readonly IServiceRegistry _serviceRegistry;
    private readonly ServiceRegistryOptions _options;
    private readonly ILogger<ServiceRegistryManager> _logger;

    // 已注册的服务端点
    private readonly ConcurrentDictionary<string, RegisteredServiceInfo> _registeredServices = new();

    // 待注册的服务队列
    private readonly ConcurrentQueue<ServiceEndpoint> _pendingRegistrations = new();

    // 健康检查定时器
    private Timer? _healthCheckTimer;

    private bool _disposed;

    public ServiceRegistryManager(
        IServiceRegistry serviceRegistry,
        IOptions<ServiceRegistryOptions> options,
        ILogger<ServiceRegistryManager> logger)
    {
        _serviceRegistry = serviceRegistry;
        _options = options.Value;
        _logger = logger;

        _logger.LogInformation("ServiceRegistryManager 已初始化，启用状态: {Enabled}", _options.Enabled);
    }

    /// <summary>
    /// 注册服务端点
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>注册是否成功</returns>
    public async Task<bool> RegisterServiceAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("服务注册已禁用，跳过注册服务: {ServiceId}", endpoint.ServiceId);
            return false;
        }

        try
        {
            await RegisterWithRetryAsync(endpoint, cancellationToken);

            // 记录已注册的服务
            var serviceInfo = new RegisteredServiceInfo
            {
                Endpoint = endpoint,
                RegisteredAt = DateTime.UtcNow,
                LastHealthCheck = DateTime.UtcNow,
                RegistrationAttempts = 0,
                IsHealthy = true
            };

            _registeredServices[endpoint.ServiceId] = serviceInfo;

            _logger.LogInformation("服务注册成功: {ServiceName}({ServiceId}) @ {Address}",
                endpoint.ServiceType, endpoint.ServiceId, endpoint.GetServiceAddress());

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "服务注册失败: {ServiceName}({ServiceId})",
                endpoint.ServiceType, endpoint.ServiceId);

            if (!_options.ContinueOnRegistrationFailure)
            {
                throw;
            }

            // 添加到待注册队列，稍后重试
            _pendingRegistrations.Enqueue(endpoint);
            return false;
        }
    }

    /// <summary>
    /// 注销服务端点
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>注销是否成功</returns>
    public async Task<bool> UnregisterServiceAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return false;
        }

        try
        {
            await _serviceRegistry.UnregisterAsync(serviceId, cancellationToken);

            _registeredServices.TryRemove(serviceId, out _);

            _logger.LogInformation("服务注销成功: {ServiceId}", serviceId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "服务注销失败: {ServiceId}", serviceId);
            return false;
        }
    }

    /// <summary>
    /// 更新服务健康状态
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="status">健康状态</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>更新是否成功</returns>
    public async Task<bool> UpdateServiceHealthAsync(string serviceId, HealthStatus status, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return false;
        }

        try
        {
            await _serviceRegistry.UpdateHealthAsync(serviceId, status, cancellationToken);

            if (_registeredServices.TryGetValue(serviceId, out var serviceInfo))
            {
                serviceInfo.Endpoint.Health = status;
                serviceInfo.LastHealthCheck = DateTime.UtcNow;
                serviceInfo.IsHealthy = status == HealthStatus.Healthy;
            }

            _logger.LogDebug("服务健康状态更新成功: {ServiceId} -> {Status}", serviceId, status);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新服务健康状态失败: {ServiceId}", serviceId);
            return false;
        }
    }

    /// <summary>
    /// 获取已注册的服务列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务端点列表</returns>
    public async Task<IReadOnlyList<ServiceEndpoint>> GetRegisteredServicesAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Array.Empty<ServiceEndpoint>();
        }

        try
        {
            return await _serviceRegistry.GetRegisteredServicesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取已注册服务列表失败");
            return Array.Empty<ServiceEndpoint>();
        }
    }

    /// <summary>
    /// 获取本地注册的服务统计信息
    /// </summary>
    public Dictionary<string, object> GetStatistics()
    {
        return new Dictionary<string, object>
        {
            ["Enabled"] = _options.Enabled,
            ["RegisteredServiceCount"] = _registeredServices.Count,
            ["PendingRegistrationCount"] = _pendingRegistrations.Count,
            ["HealthyServiceCount"] = _registeredServices.Values.Count(s => s.IsHealthy),
            ["UnhealthyServiceCount"] = _registeredServices.Values.Count(s => !s.IsHealthy),
            ["Services"] = _registeredServices.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    ServiceName = kvp.Value.Endpoint.ServiceType,
                    EndPoint = kvp.Value.Endpoint.ToString(),
                    HealthStatus = kvp.Value.Endpoint.Health.ToString(),
                    RegisteredAt = kvp.Value.RegisteredAt,
                    LastHealthCheck = kvp.Value.LastHealthCheck,
                    RegistrationAttempts = kvp.Value.RegistrationAttempts,
                    IsHealthy = kvp.Value.IsHealthy
                })
        };
    }

    /// <summary>
    /// 后台服务执行逻辑
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("服务注册已禁用，后台服务不会启动");
            return;
        }

        _logger.LogInformation("ServiceRegistryManager 后台服务已启动");

        // 启动健康检查定时器
        _healthCheckTimer = new Timer(PerformHealthCheck, null,
            _options.HealthCheckInterval, _options.HealthCheckInterval);

        // 处理待注册的服务
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingRegistrations(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理待注册服务时发生错误");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        _logger.LogInformation("ServiceRegistryManager 后台服务已停止");
    }

    /// <summary>
    /// 停止后台服务
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("正在停止 ServiceRegistryManager...");

        // 停止健康检查定时器
        _healthCheckTimer?.Dispose();

        // 注销所有已注册的服务
        if (_options.AutoUnregisterOnShutdown)
        {
            await UnregisterAllServicesAsync(cancellationToken);
        }

        await base.StopAsync(cancellationToken);
        _logger.LogInformation("ServiceRegistryManager 已停止");
    }

    #region Private Methods

    /// <summary>
    /// 带重试的服务注册
    /// </summary>
    private async Task RegisterWithRetryAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken)
    {
        var attempts = 0;
        Exception? lastException = null;

        while (attempts < _options.RegistrationRetryCount)
        {
            try
            {
                attempts++;
                await _serviceRegistry.RegisterAsync(endpoint, cancellationToken);
                return; // 注册成功
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "服务注册尝试 {Attempt}/{MaxAttempts} 失败: {ServiceId}",
                    attempts, _options.RegistrationRetryCount, endpoint.ServiceId);

                if (attempts < _options.RegistrationRetryCount)
                {
                    await Task.Delay(_options.RegistrationRetryDelay, cancellationToken);
                }
            }
        }

        // 所有重试都失败了
        throw new InvalidOperationException($"服务注册失败，已重试 {_options.RegistrationRetryCount} 次", lastException);
    }

    /// <summary>
    /// 处理待注册的服务
    /// </summary>
    private async Task ProcessPendingRegistrations(CancellationToken cancellationToken)
    {
        var processedCount = 0;
        var maxProcessCount = 10; // 每次最多处理10个待注册服务

        while (processedCount < maxProcessCount && _pendingRegistrations.TryDequeue(out var endpoint))
        {
            try
            {
                await RegisterServiceAsync(endpoint, cancellationToken);
                processedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理待注册服务失败: {ServiceId}", endpoint.ServiceId);
            }
        }

        if (processedCount > 0)
        {
            _logger.LogDebug("已处理 {Count} 个待注册服务", processedCount);
        }
    }

    /// <summary>
    /// 执行健康检查
    /// </summary>
    private void PerformHealthCheck(object? state)
    {
        if (_disposed)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var healthCheckTasks = _registeredServices.Values.Select(async serviceInfo =>
                {
                    try
                    {
                        // 这里可以添加实际的健康检查逻辑
                        // 目前只是更新健康检查时间和状态
                        var isHealthy = await CheckServiceHealth(serviceInfo.Endpoint);
                        var newStatus = isHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy;

                        if (serviceInfo.Endpoint.Health != newStatus)
                        {
                            await UpdateServiceHealthAsync(serviceInfo.Endpoint.ServiceId, newStatus);
                        }

                        serviceInfo.LastHealthCheck = DateTime.UtcNow;
                        serviceInfo.IsHealthy = isHealthy;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "健康检查失败: {ServiceId}", serviceInfo.Endpoint.ServiceId);
                        serviceInfo.IsHealthy = false;
                    }
                });

                await Task.WhenAll(healthCheckTasks);

                _logger.LogDebug("已完成 {Count} 个服务的健康检查", _registeredServices.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行健康检查时发生错误");
            }
        });
    }

    /// <summary>
    /// 检查服务健康状态
    /// </summary>
    private async Task<bool> CheckServiceHealth(ServiceEndpoint endpoint)
    {
        try
        {
            // 这里可以实现具体的健康检查逻辑
            // 例如：TCP连接检查、HTTP健康检查端点等

            // 暂时返回true，表示服务健康
            await Task.Delay(1); // 模拟异步检查
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 注销所有已注册的服务
    /// </summary>
    private async Task UnregisterAllServicesAsync(CancellationToken cancellationToken)
    {
        var unregisterTasks = _registeredServices.Keys.Select(async serviceId =>
        {
            try
            {
                await UnregisterServiceAsync(serviceId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注销服务失败: {ServiceId}", serviceId);
            }
        });

        await Task.WhenAll(unregisterTasks);
        _registeredServices.Clear();
    }

    #endregion

    #region IDisposable

    public override void Dispose()
    {
        base.Dispose();

        Dispose(true);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed || !disposing)
        {
            return;
        }

        _disposed = true;
        _healthCheckTimer?.Dispose();

        // 同步注销所有服务
        if (!_options.AutoUnregisterOnShutdown)
        {
            return;
        }

        try
        {
            UnregisterAllServicesAsync(CancellationToken.None).Wait(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "释放资源时注销服务失败");
        }
    }

    #endregion

    /// <summary>
    /// 已注册服务信息
    /// </summary>
    private class RegisteredServiceInfo
    {
        public required ServiceEndpoint Endpoint { get; init; }
        public DateTime RegisteredAt { get; init; }
        public DateTime LastHealthCheck { get; set; }
        public int RegistrationAttempts { get; set; }
        public bool IsHealthy { get; set; }
    }
}
