using dotnet_etcd;
using Etcdserverpb;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using PulseRPC.ServiceDiscovery;
using PulseRPC.ServiceRegistration;

namespace PulseRPC.Infrastructure.Etcd;

public class EtcdServiceDiscovery : IServiceDiscovery, IServiceRegistry, IDisposable
{
    private readonly EtcdClient _etcdClient;
    private readonly ILogger<EtcdServiceDiscovery> _logger;
    private readonly EtcdOptions _options;
    private readonly string _keyPrefix;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public EtcdServiceDiscovery(
        EtcdClient etcdClient,
        ILogger<EtcdServiceDiscovery> logger,
        IOptions<EtcdOptions> options)
    {
        _etcdClient = etcdClient ?? throw new ArgumentNullException(nameof(etcdClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _keyPrefix = _options.KeyPrefix.TrimEnd('/') + "/";

        _logger.LogInformation("Etcd service discovery initialized with endpoints: {Endpoints}",
            string.Join(", ", _options.Endpoints));
    }

    public async Task<IReadOnlyList<ServiceEndpoint>> DiscoverServicesAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var key = $"{_keyPrefix}services/{serviceName}/";
            var response = await _etcdClient.GetRangeAsync(key, rangeEnd: GetRangeEnd(key), cancellationToken: cancellationToken);

            var endpoints = new List<ServiceEndpoint>();

            foreach (var kv in response.Kvs)
            {
                try
                {
                    var json = kv.Value.ToStringUtf8();
                    var registration = JsonSerializer.Deserialize<ServiceRegistration>(json);

                    if (registration != null)
                    {
                        endpoints.Add(registration.Endpoint);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize service registration from etcd key: {Key}",
                        kv.Key.ToStringUtf8());
                }
            }

            _logger.LogDebug("Discovered {Count} endpoints for service: {ServiceName}",
                endpoints.Count, serviceName);

            return endpoints.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover services for: {ServiceName}", serviceName);
            throw;
        }
    }

    public async Task RegisterAsync(ServiceRegistration registration, CancellationToken cancellationToken = default)
    {
        if (registration == null)
            throw new ArgumentNullException(nameof(registration));

        try
        {
            await _semaphore.WaitAsync(cancellationToken);

            var key = $"{_keyPrefix}services/{registration.ServiceName}/{registration.Id}";
            var value = JsonSerializer.Serialize(registration);

            if (_options.UseLeases)
            {
                var leaseResponse = await _etcdClient.LeaseGrantAsync(new LeaseGrantRequest
                {
                    TTL = (long)_options.LeaseTtl.TotalSeconds
                }, cancellationToken: cancellationToken);

                await _etcdClient.PutAsync(key, value, new PutRequest
                {
                    Lease = leaseResponse.ID
                }, cancellationToken: cancellationToken);

                _logger.LogDebug("Registered service with lease: {ServiceName} (ID: {ServiceId}, Lease: {LeaseId})",
                    registration.ServiceName, registration.Id, leaseResponse.ID);
            }
            else
            {
                await _etcdClient.PutAsync(key, value, cancellationToken: cancellationToken);
                _logger.LogDebug("Registered service: {ServiceName} (ID: {ServiceId})",
                    registration.ServiceName, registration.Id);
            }

            _logger.LogInformation("Registered service: {ServiceName} (ID: {ServiceId})",
                registration.ServiceName, registration.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register service: {ServiceName} (ID: {ServiceId})",
                registration.ServiceName, registration.Id);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task UnregisterAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        try
        {
            await _semaphore.WaitAsync(cancellationToken);

            // 查找服务的完整key路径
            var searchKey = $"{_keyPrefix}services/";
            var response = await _etcdClient.GetRangeAsync(searchKey, rangeEnd: GetRangeEnd(searchKey), cancellationToken: cancellationToken);

            var keysToDelete = new List<string>();
            foreach (var kv in response.Kvs)
            {
                try
                {
                    var json = kv.Value.ToStringUtf8();
                    var registration = JsonSerializer.Deserialize<ServiceRegistration>(json);

                    if (registration?.Id == serviceId)
                    {
                        keysToDelete.Add(kv.Key.ToStringUtf8());
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize service registration during unregister");
                }
            }

            foreach (var key in keysToDelete)
            {
                await _etcdClient.DeleteAsync(key, cancellationToken: cancellationToken);
            }

            _logger.LogInformation("Unregistered service: {ServiceId}", serviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister service: {ServiceId}", serviceId);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<ServiceRegistration?> GetServiceAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var searchKey = $"{_keyPrefix}services/";
            var response = await _etcdClient.GetRangeAsync(searchKey, rangeEnd: GetRangeEnd(searchKey), cancellationToken: cancellationToken);

            foreach (var kv in response.Kvs)
            {
                try
                {
                    var json = kv.Value.ToStringUtf8();
                    var registration = JsonSerializer.Deserialize<ServiceRegistration>(json);

                    if (registration?.Id == serviceId)
                    {
                        return registration;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize service registration");
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get service: {ServiceId}", serviceId);
            throw;
        }
    }

    public async Task<IReadOnlyList<ServiceRegistration>> GetAllServicesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var key = $"{_keyPrefix}services/";
            var response = await _etcdClient.GetRangeAsync(key, rangeEnd: GetRangeEnd(key), cancellationToken: cancellationToken);

            var registrations = new List<ServiceRegistration>();

            foreach (var kv in response.Kvs)
            {
                try
                {
                    var json = kv.Value.ToStringUtf8();
                    var registration = JsonSerializer.Deserialize<ServiceRegistration>(json);

                    if (registration != null)
                    {
                        registrations.Add(registration);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize service registration from key: {Key}",
                        kv.Key.ToStringUtf8());
                }
            }

            return registrations.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all services");
            throw;
        }
    }

    public async Task UpdateHeartbeatAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        try
        {
            var service = await GetServiceAsync(serviceId, cancellationToken);
            if (service != null)
            {
                var updatedService = service with { LastHeartbeat = DateTime.UtcNow };
                await RegisterAsync(updatedService, cancellationToken);

                _logger.LogDebug("Updated heartbeat for service: {ServiceId}", serviceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update heartbeat for service: {ServiceId}", serviceId);
            throw;
        }
    }

    /// <summary>
    /// 获取范围查询的结束键
    /// </summary>
    /// <param name="key">起始键</param>
    /// <returns>结束键</returns>
    private static string GetRangeEnd(string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key);
        var lastByte = bytes[^1];

        if (lastByte < 0xFF)
        {
            bytes[^1] = (byte)(lastByte + 1);
            return Encoding.UTF8.GetString(bytes);
        }

        // 如果最后一个字节是0xFF，需要特殊处理
        return key + "~"; // 使用波浪号作为结束符
    }

    public void Dispose()
    {
        _semaphore?.Dispose();
        _etcdClient?.Dispose();
    }
}
