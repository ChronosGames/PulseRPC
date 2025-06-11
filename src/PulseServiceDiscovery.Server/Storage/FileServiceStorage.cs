using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseServiceDiscovery.Abstractions.Models;
using PulseServiceDiscovery.Server.Options;
using System.Collections.Concurrent;
using System.Text.Json;

namespace PulseServiceDiscovery.Server.Storage;

/// <summary>
/// 基于文件的服务存储实现
/// </summary>
public class FileServiceStorage : IServiceStorage, IDisposable
{
    private readonly ConcurrentDictionary<string, ServiceRegistration> _services = new();
    private readonly ConcurrentDictionary<string, List<string>> _servicesByName = new();
    private readonly ILogger<FileServiceStorage> _logger;
    private readonly StorageOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Timer _persistenceTimer;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly object _memoryLock = new();
    private volatile bool _isDirty = false;

    public FileServiceStorage(
        ILogger<FileServiceStorage> logger,
        IOptions<ServerOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value?.Storage ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(_options.FilePath))
        {
            _options.FilePath = Path.Combine(AppContext.BaseDirectory, "services.json");
        }

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        // 启动定时持久化
        _persistenceTimer = new Timer(PersistIfDirty, null,
            _options.PersistenceInterval, _options.PersistenceInterval);

        // 启动时加载数据
        _ = Task.Run(LoadFromFileAsync);

        _logger.LogInformation("File storage initialized with path: {FilePath}", _options.FilePath);
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

        lock (_memoryLock)
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

            // 标记为需要持久化
            _isDirty = true;
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
            lock (_memoryLock)
            {
                foreach (var serviceId in serviceIds.ToList())
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

        lock (_memoryLock)
        {
            if (_services.TryRemove(serviceId, out var registration))
            {
                RemoveFromNameIndex(registration);
                _isDirty = true;

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

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        lock (_memoryLock)
        {
            var count = _services.Count;
            _services.Clear();
            _servicesByName.Clear();
            _isDirty = true;

            _logger.LogInformation("Cleared {Count} services from file storage", count);
        }

        // 立即持久化清空状态
        await PersistToFileAsync(cancellationToken);
    }

    public Task<Dictionary<string, object>> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var filePath = _options.FilePath ?? "Unknown";
        var fileInfo = File.Exists(filePath) ? new FileInfo(filePath) : null;

        var stats = new Dictionary<string, object>
        {
            ["StorageType"] = "File",
            ["FilePath"] = filePath,
            ["TotalServices"] = _services.Count,
            ["ServiceTypes"] = _servicesByName.Count,
            ["MaxEntries"] = _options.MaxEntries,
            ["UsagePercentage"] = _options.MaxEntries > 0 ?
                (double)_services.Count / _options.MaxEntries * 100 : 0,
            ["PersistenceInterval"] = _options.PersistenceInterval.ToString(),
            ["IsDirty"] = _isDirty,
            ["FileExists"] = File.Exists(filePath),
            ["ServicesByName"] = _servicesByName.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Count)
        };

        if (fileInfo != null)
        {
            stats["FileSize"] = fileInfo.Length;
            stats["LastModified"] = fileInfo.LastWriteTime;
        }

        return Task.FromResult(stats);
    }

    /// <summary>
    /// 手动触发持久化
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_isDirty)
        {
            await PersistToFileAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 从文件加载数据
    /// </summary>
    private async Task LoadFromFileAsync()
    {
        if (string.IsNullOrWhiteSpace(_options.FilePath) || !File.Exists(_options.FilePath))
        {
            _logger.LogInformation("No existing file to load from: {FilePath}", _options.FilePath);
            return;
        }

        try
        {
            await _fileLock.WaitAsync();

            var json = await File.ReadAllTextAsync(_options.FilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogInformation("File is empty: {FilePath}", _options.FilePath);
                return;
            }

            var services = JsonSerializer.Deserialize<List<ServiceRegistration>>(json, _jsonOptions);
            if (services == null || services.Count == 0)
            {
                _logger.LogInformation("No services found in file: {FilePath}", _options.FilePath);
                return;
            }

            lock (_memoryLock)
            {
                _services.Clear();
                _servicesByName.Clear();

                foreach (var service in services)
                {
                    if (!string.IsNullOrWhiteSpace(service.Id))
                    {
                        _services[service.Id] = service;
                        AddToNameIndex(service);
                    }
                }

                _isDirty = false;
            }

            _logger.LogInformation("Loaded {Count} services from file: {FilePath}",
                services.Count, _options.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load services from file: {FilePath}", _options.FilePath);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// 持久化到文件
    /// </summary>
    private async Task PersistToFileAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.FilePath))
        {
            _logger.LogWarning("No file path configured for persistence");
            return;
        }

        try
        {
            await _fileLock.WaitAsync(cancellationToken);

            List<ServiceRegistration> services;
            lock (_memoryLock)
            {
                services = _services.Values.ToList();
                _isDirty = false;
            }

            // 确保目录存在
            var directory = Path.GetDirectoryName(_options.FilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(services, _jsonOptions);

            // 使用临时文件确保原子写入
            var tempFile = _options.FilePath + ".tmp";
            await File.WriteAllTextAsync(tempFile, json, cancellationToken);

            // 原子性地替换原文件
            if (File.Exists(_options.FilePath))
            {
                File.Replace(tempFile, _options.FilePath, null);
            }
            else
            {
                File.Move(tempFile, _options.FilePath);
            }

            _logger.LogDebug("Persisted {Count} services to file: {FilePath}",
                services.Count, _options.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist services to file: {FilePath}", _options.FilePath);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// 定时器回调：如果数据脏了就持久化
    /// </summary>
    private async void PersistIfDirty(object? state)
    {
        if (_isDirty)
        {
            await PersistToFileAsync();
        }
    }

    /// <summary>
    /// 将服务添加到名称索引
    /// </summary>
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

    public void Dispose()
    {
        _persistenceTimer?.Dispose();

        // 在释放前最后一次持久化
        if (_isDirty)
        {
            try
            {
                PersistToFileAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist data during disposal");
            }
        }

        _fileLock?.Dispose();
    }
}
