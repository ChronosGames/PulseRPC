using Microsoft.Extensions.Hosting;
using PulseRPC.ServiceDiscovery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Infrastructure;
using System.Collections.Concurrent;
using PulseRPC.HealthCheck;
using PulseRPC.Configuration;
using PulseRPC.Routing;

namespace PulseRPC.ServiceRegistration;

/// <summary>
/// 服务注册器 - 负责服务的注册与注销
/// </summary>
public class ServiceRegistrar(
    IServiceRegistry serviceRegistry,
    IOptions<ServiceRegistrationOptions> options,
    ILogger<ServiceRegistrar> logger,
    IHealthChecker? healthChecker = null)
    : IHostedService, IDisposable
{
    private readonly ServiceRegistrationOptions _options = options.Value;

    // 已注册的服务记录
    private readonly ConcurrentDictionary<string, RegisteredServiceInfo> _registeredServices = new();

    // 健康检查定时器
    private Timer? _healthCheckTimer;

    // 心跳定时器
    private Timer? _heartbeatTimer;

    private bool _disposed;

    /// <summary>
    /// 注册服务
    /// </summary>
    /// <param name="serviceInfo">服务信息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>注册任务</returns>
    public async Task<bool> RegisterServiceAsync(ServiceInfo serviceInfo, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(ServiceRegistrar));
        try
        {
            // 创建服务端点
            var endpoint = new ServiceEndpoint
            {
                ServiceId = serviceInfo.ServiceId,
                ServiceType = serviceInfo.ServiceName,
                Host = serviceInfo.Channel.Address.Host,
                Port = serviceInfo.Channel.Address.Port,
                IsHealthy = true,
                Metadata = serviceInfo.Metadata?.Data.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString() ?? "") ?? new Dictionary<string, string>()
            };

            // 执行注册
            await serviceRegistry.RegisterAsync(endpoint, cancellationToken);

            // 记录注册信息
            var registeredInfo = new RegisteredServiceInfo
            {
                ServiceInfo = serviceInfo,
                ServiceEndpoint = endpoint,
                RegisteredAt = DateTime.UtcNow,
                LastHealthCheck = DateTime.UtcNow,
                HealthStatus = HealthStatus.Unknown
            };

            _registeredServices.AddOrUpdate(serviceInfo.ServiceId, registeredInfo, (_, _) => registeredInfo);

            logger.LogInformation("服务注册成功: {ServiceName}({ServiceId}) @ {Address}",
                serviceInfo.ServiceName, serviceInfo.ServiceId, serviceInfo.GetAddress());

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "服务注册失败: {ServiceName}({ServiceId})",
                serviceInfo.ServiceName, serviceInfo.ServiceId);
            return false;
        }
    }

    /// <summary>
    /// 注销服务
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>注销任务</returns>
    public async Task<bool> UnregisterServiceAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return false;
        }

        try
        {
            if (!_registeredServices.TryGetValue(serviceId, out var registeredInfo))
            {
                logger.LogWarning("尝试注销未注册的服务: {ServiceId}", serviceId);
                return false;
            }

            // 执行注销
            await serviceRegistry.UnregisterAsync(registeredInfo.ServiceId, cancellationToken);

            // 移除注册记录
            _registeredServices.TryRemove(serviceId, out _);

            logger.LogInformation("服务注销成功: {ServiceName}({ServiceId})",
                registeredInfo.ServiceInfo.ServiceName, serviceId);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "服务注销失败: {ServiceId}", serviceId);
            return false;
        }
    }

    /// <summary>
    /// 更新服务健康状态
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="status">健康状态</param>
    /// <param name="message">状态消息</param>
    public async Task UpdateHealthStatusAsync(string serviceId, HealthStatus status, string? message = null)
    {
        if (_disposed) return;

        try
        {
            if (!_registeredServices.TryGetValue(serviceId, out var registeredInfo))
            {
                logger.LogWarning("尝试更新未注册服务的健康状态: {ServiceId}", serviceId);
                return;
            }

            // 更新端点健康状态
            registeredInfo.ServiceEndpoint.IsHealthy = (status == HealthStatus.Healthy);
            registeredInfo.HealthStatus = status;
            registeredInfo.LastHealthCheck = DateTime.UtcNow;

            // 如果服务注册中心支持健康状态更新
            if (serviceRegistry is IHealthReporter healthReporter)
            {
                await healthReporter.ReportHealthAsync(registeredInfo.ServiceEndpoint, status, message);
            }

            logger.LogDebug("更新服务健康状态: {ServiceId} -> {Status}", serviceId, status);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "更新服务健康状态失败: {ServiceId}", serviceId);
        }
    }

    /// <summary>
    /// 获取已注册的服务信息
    /// </summary>
    /// <param name="serviceId">服务ID，为空则返回所有服务</param>
    /// <returns>服务信息列表</returns>
    public IReadOnlyList<RegisteredServiceInfo> GetRegisteredServices(string? serviceId = null)
    {
        if (!string.IsNullOrEmpty(serviceId))
        {
            return _registeredServices.TryGetValue(serviceId, out var info)
                ? new[] { info }
                : Array.Empty<RegisteredServiceInfo>();
        }

        return _registeredServices.Values.ToArray();
    }

    /// <summary>
    /// 获取服务统计信息
    /// </summary>
    /// <returns>服务统计信息</returns>
    public ServiceRegistrationStatistics GetStatistics()
    {
        var stats = new ServiceRegistrationStatistics
        {
            TotalRegistered = _registeredServices.Count,
            HealthyServices = _registeredServices.Values.Count(s => s.HealthStatus == HealthStatus.Healthy),
            UnhealthyServices = _registeredServices.Values.Count(s => s.HealthStatus == HealthStatus.Unhealthy),
            UnknownStatusServices = _registeredServices.Values.Count(s => s.HealthStatus == HealthStatus.Unknown),
            Services = _registeredServices.Values.GroupBy(s => s.ServiceInfo.ServiceName)
                .ToDictionary(g => g.Key, g => g.Count())
        };

        return stats;
    }

    #region IHostedService

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_disposed) return;

        logger.LogInformation("ServiceRegistrar 正在启动...");

        // 自动注册配置的服务
        if (_options.AutoRegisterServices?.Count > 0)
        {
            foreach (var serviceInfo in _options.AutoRegisterServices)
            {
                await RegisterServiceAsync(serviceInfo, cancellationToken);
            }
        }

        // 启动健康检查定时器
        if (_options.EnableHealthCheck && healthChecker != null)
        {
            _healthCheckTimer = new Timer(PerformHealthCheck, null,
                _options.HealthCheckInterval, _options.HealthCheckInterval);
            logger.LogInformation("健康检查定时器已启动，间隔: {Interval}", _options.HealthCheckInterval);
        }

        // 启动心跳定时器
        if (_options.EnableHeartbeat)
        {
            _heartbeatTimer = new Timer(SendHeartbeat, null,
                _options.HeartbeatInterval, _options.HeartbeatInterval);
            logger.LogInformation("心跳定时器已启动，间隔: {Interval}", _options.HeartbeatInterval);
        }

        logger.LogInformation("ServiceRegistrar 启动完成");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_disposed) return;

        logger.LogInformation("ServiceRegistrar 正在停止...");

        // 停止定时器
        _healthCheckTimer?.Dispose();
        _heartbeatTimer?.Dispose();

        // 注销所有服务
        var tasks = _registeredServices.Keys.Select(serviceId =>
            UnregisterServiceAsync(serviceId, cancellationToken)).ToArray();

        try
        {
            await Task.WhenAll(tasks);
            logger.LogInformation("所有服务已注销");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "注销服务时发生错误");
        }

        logger.LogInformation("ServiceRegistrar 停止完成");
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 执行健康检查
    /// </summary>
    private async void PerformHealthCheck(object? state)
    {
        if (_disposed || healthChecker == null) return;

        try
        {
            var services = _registeredServices.Values.ToArray();
            if (services.Length == 0) return;

            logger.LogDebug("开始执行健康检查，服务数量: {Count}", services.Length);

            var endpoints = services.Select(s => s.ServiceEndpoint).ToArray();
            var healthResults = await healthChecker.CheckHealthBatchAsync(endpoints);

            foreach (var (serviceId, result) in healthResults)
            {
                var service = services.FirstOrDefault(s => s.ServiceEndpoint.ServiceId == serviceId);
                if (service == null)
                {
                    continue;
                }

                var oldStatus = service.HealthStatus;
                service.HealthStatus = result.Status;
                service.LastHealthCheck = DateTime.UtcNow;
                service.ServiceEndpoint.IsHealthy = (result.Status == HealthStatus.Healthy);

                // 状态变化时记录日志
                if (oldStatus != result.Status)
                {
                    logger.LogInformation("服务健康状态变化: {ServiceId} {OldStatus} -> {NewStatus}",
                        serviceId, oldStatus, result.Status);

                    // 报告健康状态变化
                    if (serviceRegistry is IHealthReporter healthReporter)
                    {
                        await healthReporter.ReportHealthAsync(service.ServiceEndpoint, result.Status);
                    }
                }
            }

            var healthyCount = healthResults.Count(r => r.Value.Status == HealthStatus.Healthy);
            logger.LogDebug("健康检查完成，健康服务: {Healthy}/{Total}", healthyCount, healthResults.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "执行健康检查失败");
        }
    }

    /// <summary>
    /// 发送心跳
    /// </summary>
    private async void SendHeartbeat(object? state)
    {
        if (_disposed) return;

        try
        {
            var services = _registeredServices.Values.ToArray();
            if (services.Length == 0) return;

            logger.LogDebug("发送服务心跳，服务数量: {Count}", services.Length);

            foreach (var service in services)
            {
                try
                {
                    // 如果服务注册中心支持心跳
                    if (serviceRegistry is IHeartbeatSender heartbeatSender)
                    {
                        await heartbeatSender.SendHeartbeatAsync(service.ServiceEndpoint);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "发送心跳失败: {ServiceId}", service.ServiceInfo.ServiceId);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "发送心跳时发生错误");
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        // 停止定时器
        _healthCheckTimer?.Dispose();
        _heartbeatTimer?.Dispose();

        // 清除注册记录
        _registeredServices.Clear();

        logger.LogInformation("ServiceRegistrar 已释放资源");
    }

    #endregion
}

/// <summary>
/// 已注册的服务信息
/// </summary>
public class RegisteredServiceInfo
{
    /// <summary>
    /// 服务信息
    /// </summary>
    public required ServiceInfo ServiceInfo { get; init; }

    /// <summary>
    /// 服务端点
    /// </summary>
    public required ServiceEndpoint ServiceEndpoint { get; init; }

    /// <summary>
    /// 注册时间
    /// </summary>
    public DateTime RegisteredAt { get; set; }

    /// <summary>
    /// 最后健康检查时间
    /// </summary>
    public DateTime LastHealthCheck { get; set; }

    /// <summary>
    /// 健康状态
    /// </summary>
    public HealthStatus HealthStatus { get; set; }

    /// <summary>
    /// 获取服务ID
    /// </summary>
    public string ServiceId => ServiceInfo.ServiceId;
}

/// <summary>
/// 服务注册统计信息
/// </summary>
public class ServiceRegistrationStatistics
{
    /// <summary>
    /// 总注册服务数
    /// </summary>
    public int TotalRegistered { get; set; }

    /// <summary>
    /// 健康服务数
    /// </summary>
    public int HealthyServices { get; set; }

    /// <summary>
    /// 不健康服务数
    /// </summary>
    public int UnhealthyServices { get; set; }

    /// <summary>
    /// 状态未知服务数
    /// </summary>
    public int UnknownStatusServices { get; set; }

    /// <summary>
    /// 按服务名分组的服务数量
    /// </summary>
    public Dictionary<string, int> Services { get; set; } = new();
}

/// <summary>
/// 服务信息
/// </summary>
public class ServiceInfo
{
    /// <summary>
    /// 服务ID
    /// </summary>
    public string ServiceId { get; set; } = string.Empty;

    /// <summary>
    /// 服务名称（服务类型）
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// 服务实例名称（用于多实例场景）
    /// </summary>
    public string? InstanceName { get; set; }

    /// <summary>
    /// 承载此服务的通道端点
    /// </summary>
    public required ChannelEndpoint Channel { get; set; }

    /// <summary>
    /// 服务元数据
    /// </summary>
    public ServiceMetadata? Metadata { get; set; }

    /// <summary>
    /// 创建服务信息的便捷方法
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="serviceName">服务名称</param>
    /// <param name="host">主机地址</param>
    /// <param name="port">端口</param>
    /// <param name="protocol">传输协议</param>
    /// <param name="instanceName">实例名称</param>
    /// <param name="metadata">元数据</param>
    public static ServiceInfo Create(
        string serviceId,
        string serviceName,
        string host,
        int port,
        TransportProtocol protocol = TransportProtocol.Tcp,
        string? instanceName = null,
        ServiceMetadata? metadata = null)
    {
        return new ServiceInfo
        {
            ServiceId = serviceId,
            ServiceName = serviceName,
            InstanceName = instanceName,
            Channel = new ChannelEndpoint
            {
                ChannelId = $"{serviceId}_channel",
                ChannelName = $"{serviceName}_channel",
                Protocol = protocol,
                Address = new NetworkAddress
                {
                    Host = host,
                    Port = port,
                    UseTls = false
                }
            },
            Metadata = metadata
        };
    }

    /// <summary>
    /// 获取服务的网络地址
    /// </summary>
    public string GetAddress() => Channel.Address.ToString();
}

/// <summary>
/// 健康状态报告接口
/// </summary>
public interface IHealthReporter
{
    /// <summary>
    /// 报告健康状态
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <param name="status">健康状态</param>
    /// <param name="message">状态消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>报告任务</returns>
    Task ReportHealthAsync(ServiceEndpoint endpoint, HealthStatus status, string? message = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// 心跳发送接口
/// </summary>
public interface IHeartbeatSender
{
    /// <summary>
    /// 发送心跳
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>心跳任务</returns>
    Task SendHeartbeatAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken = default);
}
