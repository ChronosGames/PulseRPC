using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Net.Http;
using System.Text;
using PulseRPC.Client.ServiceDiscovery;
using PulseRPC.Server.ServiceDiscovery;

namespace PulseRPC.ServiceDiscovery.Implementations
{
    /// <summary>
    /// Etcd 服务发现实现
    /// 基于 Etcd v3 API 实现服务注册与发现
    /// </summary>
    public class EtcdServiceDiscovery : IServiceDiscovery, IServiceRegistry, IDisposable
    {
        private readonly EtcdOptions _options;
        private readonly ILogger<EtcdServiceDiscovery> _logger;
        private readonly HttpClient _httpClient;
        private readonly ConcurrentDictionary<string, ServiceEndpoint[]> _serviceCache;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _watchCancellations;
        private readonly Timer _refreshTimer;
        private readonly SemaphoreSlim _refreshSemaphore;
        private volatile bool _disposed;

        /// <summary>
        /// 服务前缀键
        /// </summary>
        private const string ServiceKeyPrefix = "/services/";

        public EtcdServiceDiscovery(
            IOptions<EtcdOptions> options,
            ILogger<EtcdServiceDiscovery> logger,
            HttpClient? httpClient = null)
        {
            _options = options.Value;
            _logger = logger;
            _httpClient = httpClient ?? new HttpClient();
            _serviceCache = new ConcurrentDictionary<string, ServiceEndpoint[]>();
            _watchCancellations = new ConcurrentDictionary<string, CancellationTokenSource>();
            _refreshSemaphore = new SemaphoreSlim(1, 1);

            // 配置 HTTP 客户端
            ConfigureHttpClient();

            // 启动定时刷新缓存
            if (_options.RefreshInterval > TimeSpan.Zero)
            {
                _refreshTimer = new Timer(RefreshCacheCallback, null, _options.RefreshInterval, _options.RefreshInterval);
            }

            _logger.LogInformation("EtcdServiceDiscovery 已初始化，服务器: {Endpoints}", string.Join(", ", _options.Endpoints));
        }

        #region IServiceRegistry Implementation

        /// <summary>
        /// 注册服务端点到 Etcd
        /// </summary>
        public async Task RegisterAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EtcdServiceDiscovery));

            try
            {
                var key = GetServiceKey(endpoint.ServiceName, endpoint.ServiceId);
                var value = JsonSerializer.Serialize(CreateEtcdServiceRecord(endpoint));

                var leaseId = await CreateLeaseAsync(_options.ServiceTtl, cancellationToken);
                await PutWithLeaseAsync(key, value, leaseId, cancellationToken);

                // 启动租约续约
                _ = Task.Run(() => KeepAliveLeaseAsync(leaseId, cancellationToken), cancellationToken);

                _logger.LogInformation("服务已注册到 Etcd: {ServiceId} -> {EndPoint}", endpoint.ServiceId, endpoint.EndPoint);

                // 清除相关缓存
                InvalidateServiceCache(endpoint.ServiceName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注册服务到 Etcd 失败: {ServiceId}", endpoint.ServiceId);
                throw;
            }
        }

        /// <summary>
        /// 从 Etcd 注销服务端点
        /// </summary>
        public async Task DeregisterAsync(string serviceId, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EtcdServiceDiscovery));

            try
            {
                // 查找服务键
                var key = await FindServiceKeyAsync(serviceId, cancellationToken);
                if (string.IsNullOrEmpty(key))
                {
                    _logger.LogWarning("未找到服务ID对应的键: {ServiceId}", serviceId);
                    return;
                }

                await DeleteKeyAsync(key, cancellationToken);

                _logger.LogInformation("服务已从 Etcd 注销: {ServiceId}", serviceId);

                // 清除缓存
                var serviceName = ExtractServiceNameFromKey(key);
                if (!string.IsNullOrEmpty(serviceName))
                {
                    InvalidateServiceCache(serviceName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从 Etcd 注销服务失败: {ServiceId}", serviceId);
                throw;
            }
        }

        /// <summary>
        /// 更新服务端点健康状态
        /// </summary>
        public async Task UpdateHealthStatusAsync(string serviceId, HealthStatus status, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EtcdServiceDiscovery));

            try
            {
                var key = await FindServiceKeyAsync(serviceId, cancellationToken);
                if (string.IsNullOrEmpty(key))
                {
                    _logger.LogWarning("未找到服务ID对应的键: {ServiceId}", serviceId);
                    return;
                }

                var existingValue = await GetKeyAsync(key, cancellationToken);
                if (string.IsNullOrEmpty(existingValue))
                {
                    _logger.LogWarning("服务记录不存在: {ServiceId}", serviceId);
                    return;
                }

                var record = JsonSerializer.Deserialize<EtcdServiceRecord>(existingValue);
                if (record != null)
                {
                    record.HealthStatus = status.ToString();
                    record.LastHealthCheck = DateTime.UtcNow;

                    var updatedValue = JsonSerializer.Serialize(record);
                    await PutAsync(key, updatedValue, cancellationToken);

                    _logger.LogDebug("服务健康状态已更新: {ServiceId} -> {Status}", serviceId, status);

                    // 清除缓存
                    var serviceName = ExtractServiceNameFromKey(key);
                    if (!string.IsNullOrEmpty(serviceName))
                    {
                        InvalidateServiceCache(serviceName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新服务健康状态失败: {ServiceId}", serviceId);
                throw;
            }
        }

        #endregion

        #region IServiceDiscovery Implementation

        /// <summary>
        /// 发现指定名称的所有服务端点
        /// </summary>
        public async Task<IReadOnlyList<ServiceEndpoint>> DiscoverAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EtcdServiceDiscovery));

            try
            {
                // 尝试从缓存获取
                if (_options.EnableCaching && _serviceCache.TryGetValue(serviceName, out var cachedEndpoints))
                {
                    _logger.LogDebug("从缓存返回服务端点: {ServiceName}, 数量: {Count}", serviceName, cachedEndpoints.Length);
                    return cachedEndpoints;
                }

                // 从 Etcd 查询
                var endpoints = await FetchServiceEndpointsAsync(serviceName, cancellationToken);

                // 更新缓存
                if (_options.EnableCaching)
                {
                    _serviceCache.TryAdd(serviceName, endpoints.ToArray());
                }

                _logger.LogDebug("从 Etcd 发现服务端点: {ServiceName}, 数量: {Count}", serviceName, endpoints.Count);
                return endpoints;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发现服务端点失败: {ServiceName}", serviceName);
                throw;
            }
        }

        /// <summary>
        /// 根据标签发现服务端点
        /// </summary>
        public async Task<IReadOnlyList<ServiceEndpoint>> DiscoverByTagsAsync(
            string serviceName,
            IDictionary<string, string> tags,
            CancellationToken cancellationToken = default)
        {
            var allEndpoints = await DiscoverAsync(serviceName, cancellationToken);

            // 过滤匹配标签的端点
            var filteredEndpoints = allEndpoints.Where(endpoint =>
            {
                return tags.All(tag =>
                    endpoint.Tags.TryGetValue(tag.Key, out var value) && value == tag.Value);
            }).ToList();

            _logger.LogDebug("根据标签过滤服务端点: {ServiceName}, 过滤前: {Before}, 过滤后: {After}",
                serviceName, allEndpoints.Count, filteredEndpoints.Count);

            return filteredEndpoints.AsReadOnly();
        }

        /// <summary>
        /// 监听服务变化
        /// </summary>
        public async IAsyncEnumerable<ServiceEndpoint[]> WatchAsync(
            string serviceName,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EtcdServiceDiscovery));

            var watchCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _watchCancellations.TryAdd(serviceName, watchCts);

            try
            {
                var keyPrefix = GetServiceKeyPrefix(serviceName);

                _logger.LogInformation("开始监听服务变化: {ServiceName}", serviceName);

                // 先返回当前的服务端点
                var currentEndpoints = await FetchServiceEndpointsAsync(serviceName, cancellationToken);
                yield return currentEndpoints.ToArray();

                // 开始监听变化
                await foreach (var changeEvent in WatchKeyPrefixAsync(keyPrefix, watchCts.Token))
                {
                    if (changeEvent.Type == EtcdEventType.Put || changeEvent.Type == EtcdEventType.Delete)
                    {
                        // 服务发生变化，重新获取端点
                        InvalidateServiceCache(serviceName);
                        var updatedEndpoints = await FetchServiceEndpointsAsync(serviceName, cancellationToken);

                        _logger.LogDebug("检测到服务变化: {ServiceName}, 类型: {EventType}, 当前端点数: {Count}",
                            serviceName, changeEvent.Type, updatedEndpoints.Count);

                        yield return updatedEndpoints.ToArray();
                    }
                }
            }
            finally
            {
                _watchCancellations.TryRemove(serviceName, out _);
                watchCts.Dispose();
                _logger.LogInformation("停止监听服务变化: {ServiceName}", serviceName);
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 配置 HTTP 客户端
        /// </summary>
        private void ConfigureHttpClient()
        {
            _httpClient.Timeout = _options.RequestTimeout;

            if (!string.IsNullOrEmpty(_options.Username) && !string.IsNullOrEmpty(_options.Password))
            {
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}"));
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            }
        }

        /// <summary>
        /// 从 Etcd 获取服务端点
        /// </summary>
        private async Task<List<ServiceEndpoint>> FetchServiceEndpointsAsync(string serviceName, CancellationToken cancellationToken)
        {
            var keyPrefix = GetServiceKeyPrefix(serviceName);
            var kvPairs = await GetKeyPrefixAsync(keyPrefix, cancellationToken);

            var endpoints = new List<ServiceEndpoint>();

            foreach (var kvPair in kvPairs)
            {
                try
                {
                    var record = JsonSerializer.Deserialize<EtcdServiceRecord>(kvPair.Value);
                    if (record != null)
                    {
                        var endpoint = ConvertToServiceEndpoint(record);
                        if (endpoint != null)
                        {
                            endpoints.Add(endpoint);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "解析服务记录失败: {Key} -> {Value}", kvPair.Key, kvPair.Value);
                }
            }

            return endpoints;
        }

        /// <summary>
        /// 创建 Etcd 服务记录
        /// </summary>
        private EtcdServiceRecord CreateEtcdServiceRecord(ServiceEndpoint endpoint)
        {
            return new EtcdServiceRecord
            {
                ServiceId = endpoint.ServiceId,
                ServiceName = endpoint.ServiceName,
                Address = endpoint.EndPoint.Address.ToString(),
                Port = endpoint.EndPoint.Port,
                Tags = endpoint.Tags,
                Metadata = endpoint.Metadata,
                HealthStatus = HealthStatus.Unknown.ToString(),
                RegisterTime = DateTime.UtcNow,
                LastHealthCheck = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 将 Etcd 服务记录转换为服务端点
        /// </summary>
        private ServiceEndpoint? ConvertToServiceEndpoint(EtcdServiceRecord record)
        {
            try
            {
                if (!System.Net.IPAddress.TryParse(record.Address, out var address))
                {
                    _logger.LogWarning("无效的IP地址: {Address}", record.Address);
                    return null;
                }

                return new ServiceEndpoint
                {
                    ServiceId = record.ServiceId,
                    ServiceName = record.ServiceName,
                    EndPoint = new System.Net.IPEndPoint(address, record.Port),
                    Tags = record.Tags ?? new Dictionary<string, string>(),
                    Metadata = record.Metadata ?? new Dictionary<string, object>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "转换服务端点失败: {Record}", JsonSerializer.Serialize(record));
                return null;
            }
        }

        /// <summary>
        /// 获取服务键
        /// </summary>
        private string GetServiceKey(string serviceName, string serviceId)
        {
            return $"{ServiceKeyPrefix}{serviceName}/{serviceId}";
        }

        /// <summary>
        /// 获取服务键前缀
        /// </summary>
        private string GetServiceKeyPrefix(string serviceName)
        {
            return $"{ServiceKeyPrefix}{serviceName}/";
        }

        /// <summary>
        /// 从键中提取服务名称
        /// </summary>
        private string? ExtractServiceNameFromKey(string key)
        {
            if (key.StartsWith(ServiceKeyPrefix))
            {
                var remaining = key.Substring(ServiceKeyPrefix.Length);
                var slashIndex = remaining.IndexOf('/');
                return slashIndex > 0 ? remaining.Substring(0, slashIndex) : null;
            }
            return null;
        }

        /// <summary>
        /// 查找服务键
        /// </summary>
        private async Task<string?> FindServiceKeyAsync(string serviceId, CancellationToken cancellationToken)
        {
            var allKvPairs = await GetKeyPrefixAsync(ServiceKeyPrefix, cancellationToken);

            foreach (var kvPair in allKvPairs)
            {
                try
                {
                    var record = JsonSerializer.Deserialize<EtcdServiceRecord>(kvPair.Value);
                    if (record?.ServiceId == serviceId)
                    {
                        return kvPair.Key;
                    }
                }
                catch
                {
                    // 忽略解析错误
                }
            }

            return null;
        }

        /// <summary>
        /// 清除服务缓存
        /// </summary>
        private void InvalidateServiceCache(string serviceName)
        {
            _serviceCache.TryRemove(serviceName, out _);
            _logger.LogDebug("已清除服务缓存: {ServiceName}", serviceName);
        }

        /// <summary>
        /// 定时刷新缓存回调
        /// </summary>
        private async void RefreshCacheCallback(object? state)
        {
            if (_disposed) return;

            try
            {
                await _refreshSemaphore.WaitAsync();

                var cacheKeys = _serviceCache.Keys.ToArray();
                foreach (var serviceName in cacheKeys)
                {
                    try
                    {
                        var endpoints = await FetchServiceEndpointsAsync(serviceName, CancellationToken.None);
                        _serviceCache.TryUpdate(serviceName, endpoints.ToArray(), _serviceCache[serviceName]);

                        _logger.LogDebug("已刷新服务缓存: {ServiceName}", serviceName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "刷新服务缓存失败: {ServiceName}", serviceName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "定时刷新缓存失败");
            }
            finally
            {
                _refreshSemaphore.Release();
            }
        }

        #endregion

        #region Etcd API Methods

        /// <summary>
        /// 创建租约
        /// </summary>
        private async Task<long> CreateLeaseAsync(TimeSpan ttl, CancellationToken cancellationToken)
        {
            var request = new
            {
                TTL = (long)ttl.TotalSeconds
            };

            var endpoint = GetRandomEndpoint();
            var response = await PostJsonAsync<EtcdLeaseResponse>($"{endpoint}/v3/lease/grant", request, cancellationToken);

            if (response?.ID == null)
            {
                throw new InvalidOperationException("创建租约失败");
            }

            return response.ID.Value;
        }

        /// <summary>
        /// 保持租约活跃
        /// </summary>
        private async Task KeepAliveLeaseAsync(long leaseId, CancellationToken cancellationToken)
        {
            var keepAliveInterval = TimeSpan.FromSeconds(_options.ServiceTtl.TotalSeconds / 3);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(keepAliveInterval, cancellationToken);

                    var request = new { ID = leaseId };
                    var endpoint = GetRandomEndpoint();
                    await PostJsonAsync($"{endpoint}/v3/lease/keepalive", request, cancellationToken);

                    _logger.LogTrace("租约续约成功: {LeaseId}", leaseId);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "租约续约失败: {LeaseId}", leaseId);
                }
            }
        }

        /// <summary>
        /// 使用租约写入键值
        /// </summary>
        private async Task PutWithLeaseAsync(string key, string value, long leaseId, CancellationToken cancellationToken)
        {
            var request = new
            {
                key = Convert.ToBase64String(Encoding.UTF8.GetBytes(key)),
                value = Convert.ToBase64String(Encoding.UTF8.GetBytes(value)),
                lease = leaseId
            };

            var endpoint = GetRandomEndpoint();
            await PostJsonAsync($"{endpoint}/v3/kv/put", request, cancellationToken);
        }

        /// <summary>
        /// 写入键值
        /// </summary>
        private async Task PutAsync(string key, string value, CancellationToken cancellationToken)
        {
            var request = new
            {
                key = Convert.ToBase64String(Encoding.UTF8.GetBytes(key)),
                value = Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            };

            var endpoint = GetRandomEndpoint();
            await PostJsonAsync($"{endpoint}/v3/kv/put", request, cancellationToken);
        }

        /// <summary>
        /// 获取键值
        /// </summary>
        private async Task<string?> GetKeyAsync(string key, CancellationToken cancellationToken)
        {
            var request = new
            {
                key = Convert.ToBase64String(Encoding.UTF8.GetBytes(key))
            };

            var endpoint = GetRandomEndpoint();
            var response = await PostJsonAsync<EtcdRangeResponse>($"{endpoint}/v3/kv/range", request, cancellationToken);

            if (response?.Kvs?.Length > 0)
            {
                var valueBytes = Convert.FromBase64String(response.Kvs[0].Value);
                return Encoding.UTF8.GetString(valueBytes);
            }

            return null;
        }

        /// <summary>
        /// 获取键前缀的所有键值对
        /// </summary>
        private async Task<List<KeyValuePair<string, string>>> GetKeyPrefixAsync(string keyPrefix, CancellationToken cancellationToken)
        {
            var request = new
            {
                key = Convert.ToBase64String(Encoding.UTF8.GetBytes(keyPrefix)),
                range_end = Convert.ToBase64String(Encoding.UTF8.GetBytes(GetRangeEnd(keyPrefix)))
            };

            var endpoint = GetRandomEndpoint();
            var response = await PostJsonAsync<EtcdRangeResponse>($"{endpoint}/v3/kv/range", request, cancellationToken);

            var result = new List<KeyValuePair<string, string>>();

            if (response?.Kvs != null)
            {
                foreach (var kv in response.Kvs)
                {
                    var key = Encoding.UTF8.GetString(Convert.FromBase64String(kv.Key));
                    var value = Encoding.UTF8.GetString(Convert.FromBase64String(kv.Value));
                    result.Add(new KeyValuePair<string, string>(key, value));
                }
            }

            return result;
        }

        /// <summary>
        /// 删除键
        /// </summary>
        private async Task DeleteKeyAsync(string key, CancellationToken cancellationToken)
        {
            var request = new
            {
                key = Convert.ToBase64String(Encoding.UTF8.GetBytes(key))
            };

            var endpoint = GetRandomEndpoint();
            await PostJsonAsync($"{endpoint}/v3/kv/deleterange", request, cancellationToken);
        }

        /// <summary>
        /// 监听键前缀变化
        /// </summary>
        private async IAsyncEnumerable<EtcdWatchEvent> WatchKeyPrefixAsync(
            string keyPrefix,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var request = new
            {
                create_request = new
                {
                    key = Convert.ToBase64String(Encoding.UTF8.GetBytes(keyPrefix)),
                    range_end = Convert.ToBase64String(Encoding.UTF8.GetBytes(GetRangeEnd(keyPrefix)))
                }
            };

            var endpoint = GetRandomEndpoint();

            // 注意：这里简化了 watch 实现，实际应该使用 gRPC 或 HTTP2 流
            // 目前使用轮询模式模拟 watch
            var lastRevision = 0L;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_options.WatchPollInterval, cancellationToken);

                    // 检查是否有变化（简化实现）
                    var kvPairs = await GetKeyPrefixAsync(keyPrefix, cancellationToken);

                    // 模拟变化事件
                    foreach (var kvPair in kvPairs)
                    {
                        yield return new EtcdWatchEvent
                        {
                            Type = EtcdEventType.Put,
                            Key = kvPair.Key,
                            Value = kvPair.Value
                        };
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Watch 监听出错: {KeyPrefix}", keyPrefix);
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
        }

        /// <summary>
        /// 获取随机端点
        /// </summary>
        private string GetRandomEndpoint()
        {
            var endpoints = _options.Endpoints;
            return endpoints[Random.Shared.Next(endpoints.Length)];
        }

        /// <summary>
        /// 获取范围结束键
        /// </summary>
        private string GetRangeEnd(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return "\0";
            }

            var bytes = Encoding.UTF8.GetBytes(key);
            for (int i = bytes.Length - 1; i >= 0; i--)
            {
                if (bytes[i] < 0xFF)
                {
                    bytes[i]++;
                    return Encoding.UTF8.GetString(bytes, 0, i + 1);
                }
            }
            return "\0";
        }

        /// <summary>
        /// POST JSON 请求
        /// </summary>
        private async Task<T?> PostJsonAsync<T>(string url, object request, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<T>(responseJson);
        }

        /// <summary>
        /// POST JSON 请求（无返回值）
        /// </summary>
        private async Task PostJsonAsync(string url, object request, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            _refreshTimer?.Dispose();

            foreach (var cts in _watchCancellations.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _watchCancellations.Clear();

            _refreshSemaphore.Dispose();
            _httpClient.Dispose();

            _logger.LogInformation("EtcdServiceDiscovery 已释放资源");
        }

        #endregion
    }

    /// <summary>
    /// Etcd 配置选项
    /// </summary>
    public class EtcdOptions
    {
        /// <summary>
        /// 配置节名称
        /// </summary>
        public const string SectionName = "Etcd";

        /// <summary>
        /// Etcd 集群端点
        /// </summary>
        public string[] Endpoints { get; set; } = { "http://localhost:2379" };

        /// <summary>
        /// 用户名（可选）
        /// </summary>
        public string? Username { get; set; }

        /// <summary>
        /// 密码（可选）
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// 请求超时时间
        /// </summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// 服务TTL
        /// </summary>
        public TimeSpan ServiceTtl { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 是否启用缓存
        /// </summary>
        public bool EnableCaching { get; set; } = true;

        /// <summary>
        /// 缓存刷新间隔
        /// </summary>
        public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Watch 轮询间隔
        /// </summary>
        public TimeSpan WatchPollInterval { get; set; } = TimeSpan.FromSeconds(5);
    }

    /// <summary>
    /// Etcd 服务记录
    /// </summary>
    internal class EtcdServiceRecord
    {
        public string ServiceId { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public int Port { get; set; }
        public Dictionary<string, string>? Tags { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string HealthStatus { get; set; } = string.Empty;
        public DateTime RegisterTime { get; set; }
        public DateTime LastHealthCheck { get; set; }
    }

    /// <summary>
    /// Etcd 租约响应
    /// </summary>
    internal class EtcdLeaseResponse
    {
        public long? ID { get; set; }
        public long? TTL { get; set; }
    }

    /// <summary>
    /// Etcd 范围查询响应
    /// </summary>
    internal class EtcdRangeResponse
    {
        public EtcdKeyValue[]? Kvs { get; set; }
        public long Count { get; set; }
    }

    /// <summary>
    /// Etcd 键值对
    /// </summary>
    internal class EtcdKeyValue
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public long Version { get; set; }
    }

    /// <summary>
    /// Etcd Watch 事件
    /// </summary>
    internal class EtcdWatchEvent
    {
        public EtcdEventType Type { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    /// Etcd 事件类型
    /// </summary>
    internal enum EtcdEventType
    {
        Put,
        Delete
    }
}
