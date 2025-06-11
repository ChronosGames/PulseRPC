using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseServiceDiscovery.Abstractions.Models;
using PulseServiceDiscovery.Server.Options;
using System.Collections.Concurrent;

namespace PulseServiceDiscovery.Server.Storage;

/// <summary>
/// 基于内存的服务存储实现
/// </summary>
public class MemoryServiceStorage : IServiceStorage
{
    private readonly ConcurrentDictionary<string, ServiceRegistration> _services = new();
    private readonly ConcurrentDictionary<string, List<string>> _servicesByName = new();
    private readonly ILogger<MemoryServiceStorage> _logger;
    private readonly StorageOptions _options;
    private readonly object _lock = new();

    public MemoryServiceStorage(
        ILogger<MemoryServiceStorage> logger,
        IOptions<ServerOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value?.Storage ?? throw new ArgumentNullException(nameof(options));
    }

    public Task StoreServiceAsync(ServiceRegistration registration, CancellationToken cancellationToken = default)
    {
        if (registration == null)
            throw new ArgumentNullException(nameof(registration));

        if (string.IsNullOrWhiteSpace(registration.Id))
            throw new ArgumentException("Registration ID cannot be null or empty", nameof(registration));

        // 检查存储容量限制
        if (_services.Count >= _options.MaxEntries && !_services.ContainsKey(registration.Id))
        {
            _logger.LogWarning("Storage capacity limit reached: {MaxEntries}", _options.MaxEntries);
            throw new InvalidOperationException($"Storage capacity limit reached: {_options.MaxEntries}");
        }

        lock (_lock)
        {
            // 如果是更新现有服务，先从名称索引中移除
            if (_services.TryGetValue(registration.Id, out var existingRegistration))
            {
                RemoveFromNameIndex(existingRegistration);
            }

            // 存储服务注册信息
            _services[registration.Id] = registration;

            // 更新按名称的索引
            AddToNameIndex(registration);
        }

        _logger.LogDebug("Stored service: {ServiceName} (ID: {ServiceId})",
            registration.ServiceName, registration.Id);

        return Task.CompletedTask;
    }

    public Task<ServiceRegistration?> GetServiceAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        _services.TryGetValue(serviceId, out var registration);
        return Task.FromResult(registration);
    }

    public Task<IReadOnlyList<ServiceRegistration>> GetServicesByNameAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        var services = new List<ServiceRegistration>();

        if (_servicesByName.TryGetValue(serviceName, out var serviceIds))
        {
            lock (_lock)
            {
                foreach (var serviceId in serviceIds.ToList()) // 创建副本避免并发修改
                {
                    if (_services.TryGetValue(serviceId, out var registration))
                    {
                        services.Add(registration);
                    }
                }
            }
        }

        return Task.FromResult<IReadOnlyList<ServiceRegistration>>(services.AsReadOnly());
    }

    public Task<IReadOnlyList<ServiceRegistration>> GetAllServicesAsync(CancellationToken cancellationToken = default)
    {
        var services = _services.Values.ToList().AsReadOnly();
        return Task.FromResult<IReadOnlyList<ServiceRegistration>>(services);
    }

    public Task RemoveServiceAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        lock (_lock)
        {
            if (_services.TryRemove(serviceId, out var registration))
            {
                RemoveFromNameIndex(registration);

                _logger.LogDebug("Removed service: {ServiceName} (ID: {ServiceId})",
                    registration.ServiceName, serviceId);
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        var exists = _services.ContainsKey(serviceId);
        return Task.FromResult(exists);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var count = _services.Count;
            _services.Clear();
            _servicesByName.Clear();

            _logger.LogInformation("Cleared {Count} services from memory storage", count);
        }

        return Task.CompletedTask;
    }

    public Task<Dictionary<string, object>> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var stats = new Dictionary<string, object>
        {
            ["StorageType"] = "Memory",
            ["TotalServices"] = _services.Count,
            ["ServiceTypes"] = _servicesByName.Count,
            ["MaxEntries"] = _options.MaxEntries,
            ["UsagePercentage"] = _options.MaxEntries > 0 ?
                (double)_services.Count / _options.MaxEntries * 100 : 0,
            ["ServicesByName"] = _servicesByName.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Count)
        };

        return Task.FromResult(stats);
    }

    /// <summary>
    /// 将服务添加到名称索引
    /// </summary>
    /// <param name="registration">服务注册信息</param>
    private void AddToNameIndex(ServiceRegistration registration)
    {
        _servicesByName.AddOrUpdate(
            registration.ServiceName,
            new List<string> { registration.Id },
            (_, existing) =>
            {
                if (!existing.Contains(registration.Id))
                {
                    existing.Add(registration.Id);
                }
                return existing;
            });
    }

    /// <summary>
    /// 从名称索引中移除服务
    /// </summary>
    /// <param name="registration">服务注册信息</param>
    private void RemoveFromNameIndex(ServiceRegistration registration)
    {
        if (_servicesByName.TryGetValue(registration.ServiceName, out var serviceIds))
        {
            serviceIds.Remove(registration.Id);

            // 如果该服务名下没有服务了，移除整个条目
            if (serviceIds.Count == 0)
            {
                _servicesByName.TryRemove(registration.ServiceName, out _);
            }
        }
    }

    /// <summary>
    /// 获取内存使用信息
    /// </summary>
    /// <returns>内存使用信息</returns>
    public MemoryUsageInfo GetMemoryUsage()
    {
        return new MemoryUsageInfo
        {
            ServiceCount = _services.Count,
            ServiceNameIndexCount = _servicesByName.Count,
            MaxEntries = _options.MaxEntries,
            UsagePercentage = _options.MaxEntries > 0 ?
                (double)_services.Count / _options.MaxEntries * 100 : 0
        };
    }
}

/// <summary>
/// 内存使用信息
/// </summary>
public record MemoryUsageInfo
{
    /// <summary>
    /// 服务数量
    /// </summary>
    public int ServiceCount { get; init; }

    /// <summary>
    /// 服务名索引数量
    /// </summary>
    public int ServiceNameIndexCount { get; init; }

    /// <summary>
    /// 最大条目数
    /// </summary>
    public int MaxEntries { get; init; }

    /// <summary>
    /// 使用百分比
    /// </summary>
    public double UsagePercentage { get; init; }
}
