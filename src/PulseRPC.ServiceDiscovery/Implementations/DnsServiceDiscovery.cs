using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using PulseRPC.Client.ServiceDiscovery;

namespace PulseRPC.ServiceDiscovery.Implementations
{
    /// <summary>
    /// DNS 服务发现实现
    /// 基于 DNS 查询实现服务发现，支持 A 记录、SRV 记录等
    /// </summary>
    public class DnsServiceDiscovery : IServiceDiscovery, IDisposable
    {
        private readonly DnsOptions _options;
        private readonly ILogger<DnsServiceDiscovery> _logger;
        private readonly ConcurrentDictionary<string, ServiceEndpoint[]> _serviceCache;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _watchCancellations;
        private readonly Timer _refreshTimer;
        private readonly SemaphoreSlim _refreshSemaphore;
        private volatile bool _disposed;

        public DnsServiceDiscovery(
            IOptions<DnsOptions> options,
            ILogger<DnsServiceDiscovery> logger)
        {
            _options = options.Value;
            _logger = logger;
            _serviceCache = new ConcurrentDictionary<string, ServiceEndpoint[]>();
            _watchCancellations = new ConcurrentDictionary<string, CancellationTokenSource>();
            _refreshSemaphore = new SemaphoreSlim(1, 1);

            // 启动定时刷新缓存
            if (_options.RefreshInterval > TimeSpan.Zero && _options.EnableCaching)
            {
                _refreshTimer = new Timer(RefreshCacheCallback, null, _options.RefreshInterval, _options.RefreshInterval);
            }

            _logger.LogInformation("DnsServiceDiscovery 已初始化，DNS服务器: {DnsServers}, 查询类型: {QueryType}",
                string.Join(", ", _options.DnsServers ?? ["系统默认"]),
                _options.QueryType);
        }

        #region IServiceDiscovery Implementation

        /// <summary>
        /// 发现指定名称的所有服务端点
        /// </summary>
        public async Task<IReadOnlyList<ServiceEndpoint>> DiscoverAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DnsServiceDiscovery));

            try
            {
                // 尝试从缓存获取
                if (_options.EnableCaching && _serviceCache.TryGetValue(serviceName, out var cachedEndpoints))
                {
                    _logger.LogDebug("从缓存返回服务端点: {ServiceName}, 数量: {Count}", serviceName, cachedEndpoints.Length);
                    return cachedEndpoints;
                }

                // 从 DNS 查询
                var endpoints = await QueryServiceEndpointsAsync(serviceName, cancellationToken);

                // 更新缓存
                if (_options.EnableCaching)
                {
                    _serviceCache.TryAdd(serviceName, endpoints.ToArray());
                }

                _logger.LogDebug("从 DNS 发现服务端点: {ServiceName}, 数量: {Count}", serviceName, endpoints.Count);
                return endpoints;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DNS 服务发现失败: {ServiceName}", serviceName);
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
            // DNS 服务发现不直接支持标签过滤，可以通过 TXT 记录实现
            var allEndpoints = await DiscoverAsync(serviceName, cancellationToken);

            if (tags.Count == 0)
            {
                return allEndpoints;
            }

            // 尝试从 TXT 记录获取标签信息并过滤
            var filteredEndpoints = new List<ServiceEndpoint>();

            foreach (var endpoint in allEndpoints)
            {
                try
                {
                    // 查询对应的 TXT 记录获取标签信息
                    var txtRecords = await QueryTxtRecordsAsync(serviceName, cancellationToken);
                    var endpointTags = ParseTagsFromTxtRecords(txtRecords);

                    // 检查是否匹配所有指定的标签
                    var matches = tags.All(tag =>
                        endpointTags.TryGetValue(tag.Key, out var value) && value == tag.Value);

                    if (matches)
                    {
                        // 更新端点的标签信息
                        foreach (var tag in endpointTags)
                        {
                            endpoint.Tags[tag.Key] = tag.Value;
                        }
                        filteredEndpoints.Add(endpoint);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "查询 TXT 记录失败: {ServiceName}", serviceName);
                    // 如果无法获取标签信息，默认包含该端点
                    filteredEndpoints.Add(endpoint);
                }
            }

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
            if (_disposed) throw new ObjectDisposedException(nameof(DnsServiceDiscovery));

            var watchCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _watchCancellations.TryAdd(serviceName, watchCts);

            try
            {
                _logger.LogInformation("开始监听 DNS 服务变化: {ServiceName}", serviceName);

                // 先返回当前的服务端点
                var currentEndpoints = await QueryServiceEndpointsAsync(serviceName, cancellationToken);
                yield return currentEndpoints.ToArray();

                var lastEndpointsHash = GetEndpointsHash(currentEndpoints);

                // 定期轮询检查变化
                while (!watchCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(_options.WatchPollInterval, watchCts.Token);

                        var updatedEndpoints = await QueryServiceEndpointsAsync(serviceName, cancellationToken);
                        var currentEndpointsHash = GetEndpointsHash(updatedEndpoints);

                        // 检查是否有变化
                        if (currentEndpointsHash != lastEndpointsHash)
                        {
                            _logger.LogDebug("检测到 DNS 服务变化: {ServiceName}, 当前端点数: {Count}",
                                serviceName, updatedEndpoints.Count);

                            // 清除缓存
                            InvalidateServiceCache(serviceName);

                            // 更新缓存
                            if (_options.EnableCaching)
                            {
                                _serviceCache.TryUpdate(serviceName, updatedEndpoints.ToArray(), _serviceCache.GetValueOrDefault(serviceName, Array.Empty<ServiceEndpoint>()));
                            }

                            yield return updatedEndpoints.ToArray();
                            lastEndpointsHash = currentEndpointsHash;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "DNS Watch 监听出错: {ServiceName}", serviceName);
                        await Task.Delay(TimeSpan.FromSeconds(5), watchCts.Token);
                    }
                }
            }
            finally
            {
                _watchCancellations.TryRemove(serviceName, out _);
                watchCts.Dispose();
                _logger.LogInformation("停止监听 DNS 服务变化: {ServiceName}", serviceName);
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 查询服务端点
        /// </summary>
        private async Task<List<ServiceEndpoint>> QueryServiceEndpointsAsync(string serviceName, CancellationToken cancellationToken)
        {
            var endpoints = new List<ServiceEndpoint>();

            switch (_options.QueryType)
            {
                case DnsQueryType.A:
                    endpoints = await QueryARecordsAsync(serviceName, cancellationToken);
                    break;

                case DnsQueryType.SRV:
                    endpoints = await QuerySrvRecordsAsync(serviceName, cancellationToken);
                    break;

                case DnsQueryType.Auto:
                    // 先尝试 SRV，失败则尝试 A 记录
                    try
                    {
                        endpoints = await QuerySrvRecordsAsync(serviceName, cancellationToken);
                        if (endpoints.Count == 0)
                        {
                            endpoints = await QueryARecordsAsync(serviceName, cancellationToken);
                        }
                    }
                    catch
                    {
                        endpoints = await QueryARecordsAsync(serviceName, cancellationToken);
                    }
                    break;

                default:
                    throw new NotSupportedException($"不支持的 DNS 查询类型: {_options.QueryType}");
            }

            return endpoints;
        }

        /// <summary>
        /// 查询 A 记录
        /// </summary>
        private async Task<List<ServiceEndpoint>> QueryARecordsAsync(string serviceName, CancellationToken cancellationToken)
        {
            var endpoints = new List<ServiceEndpoint>();

            try
            {
                var hostname = BuildHostname(serviceName);
                var addresses = await Dns.GetHostAddressesAsync(hostname);

                foreach (var address in addresses)
                {
                    // 过滤 IPv4 地址（根据配置）
                    if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ||
                        (_options.EnableIPv6 && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6))
                    {
                        var endpoint = new ServiceEndpoint
                        {
                            ServiceId = $"{serviceName}-{address}",
                            ServiceName = serviceName,
                            EndPoint = new IPEndPoint(address, _options.DefaultPort),
                            Tags = new Dictionary<string, string>
                            {
                                ["dns_query_type"] = "A",
                                ["hostname"] = hostname
                            }
                        };
                        endpoints.Add(endpoint);
                    }
                }

                _logger.LogDebug("DNS A 记录查询: {Hostname} -> {Count} 个地址", hostname, endpoints.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DNS A 记录查询失败: {ServiceName}", serviceName);
            }

            return endpoints;
        }

        /// <summary>
        /// 查询 SRV 记录
        /// </summary>
        private async Task<List<ServiceEndpoint>> QuerySrvRecordsAsync(string serviceName, CancellationToken cancellationToken)
        {
            var endpoints = new List<ServiceEndpoint>();

            try
            {
                // 构建 SRV 查询名称: _service._protocol.domain
                var srvName = BuildSrvName(serviceName);

                // 注意：.NET 标准库不直接支持 SRV 查询，这里使用简化实现
                // 实际生产环境中建议使用 DnsClient.NET 或类似的第三方库
                var srvRecords = await QuerySrvRecordsUsingNslookup(srvName, cancellationToken);

                foreach (var srvRecord in srvRecords)
                {
                    try
                    {
                        // 解析 SRV 记录中的目标主机名
                        var addresses = await Dns.GetHostAddressesAsync(srvRecord.Target);

                        foreach (var address in addresses)
                        {
                            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ||
                                (_options.EnableIPv6 && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6))
                            {
                                var endpoint = new ServiceEndpoint
                                {
                                    ServiceId = $"{serviceName}-{address}-{srvRecord.Port}",
                                    ServiceName = serviceName,
                                    EndPoint = new IPEndPoint(address, srvRecord.Port),
                                    Tags = new Dictionary<string, string>
                                    {
                                        ["dns_query_type"] = "SRV",
                                        ["srv_name"] = srvName,
                                        ["target"] = srvRecord.Target,
                                        ["priority"] = srvRecord.Priority.ToString(),
                                        ["weight"] = srvRecord.Weight.ToString()
                                    }
                                };
                                endpoints.Add(endpoint);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "解析 SRV 目标主机失败: {Target}", srvRecord.Target);
                    }
                }

                _logger.LogDebug("DNS SRV 记录查询: {SrvName} -> {Count} 个端点", srvName, endpoints.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DNS SRV 记录查询失败: {ServiceName}", serviceName);
            }

            return endpoints;
        }

        /// <summary>
        /// 查询 TXT 记录
        /// </summary>
        private async Task<List<string>> QueryTxtRecordsAsync(string serviceName, CancellationToken cancellationToken)
        {
            // 简化实现，实际生产环境中建议使用专门的 DNS 库
            return await Task.FromResult(new List<string>());
        }

        /// <summary>
        /// 使用 nslookup 查询 SRV 记录（简化实现）
        /// </summary>
        private async Task<List<SrvRecord>> QuerySrvRecordsUsingNslookup(string srvName, CancellationToken cancellationToken)
        {
            // 注意：这是一个简化的实现，实际生产环境中建议使用专门的 DNS 库
            // 如 DnsClient.NET、ARSoft.Tools.Net 等
            return await Task.FromResult(new List<SrvRecord>());
        }

        /// <summary>
        /// 构建主机名
        /// </summary>
        private string BuildHostname(string serviceName)
        {
            if (string.IsNullOrEmpty(_options.DnsDomain))
            {
                return serviceName;
            }

            return $"{serviceName}.{_options.DnsDomain}";
        }

        /// <summary>
        /// 构建 SRV 查询名称
        /// </summary>
        private string BuildSrvName(string serviceName)
        {
            var protocol = _options.Protocol ?? "tcp";
            var domain = _options.DnsDomain ?? "local";

            return $"_{serviceName}._{protocol}.{domain}";
        }

        /// <summary>
        /// 从 TXT 记录解析标签
        /// </summary>
        private Dictionary<string, string> ParseTagsFromTxtRecords(List<string> txtRecords)
        {
            var tags = new Dictionary<string, string>();

            foreach (var record in txtRecords)
            {
                try
                {
                    // 解析 key=value 格式的 TXT 记录
                    var parts = record.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        tags[parts[0].Trim()] = parts[1].Trim();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "解析 TXT 记录失败: {Record}", record);
                }
            }

            return tags;
        }

        /// <summary>
        /// 获取端点列表的哈希值
        /// </summary>
        private string GetEndpointsHash(List<ServiceEndpoint> endpoints)
        {
            var hashInput = string.Join("|", endpoints.OrderBy(e => e.ServiceId).Select(e => $"{e.ServiceId}:{e.EndPoint}"));
            return hashInput.GetHashCode().ToString();
        }

        /// <summary>
        /// 清除服务缓存
        /// </summary>
        private void InvalidateServiceCache(string serviceName)
        {
            _serviceCache.TryRemove(serviceName, out _);
            _logger.LogDebug("已清除 DNS 服务缓存: {ServiceName}", serviceName);
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
                        var endpoints = await QueryServiceEndpointsAsync(serviceName, CancellationToken.None);
                        _serviceCache.TryUpdate(serviceName, endpoints.ToArray(), _serviceCache[serviceName]);

                        _logger.LogDebug("已刷新 DNS 服务缓存: {ServiceName}", serviceName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "刷新 DNS 服务缓存失败: {ServiceName}", serviceName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "定时刷新 DNS 缓存失败");
            }
            finally
            {
                _refreshSemaphore.Release();
            }
        }

        /// <summary>
        /// 健康检查端点
        /// </summary>
        private async Task<bool> IsEndpointHealthyAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(endpoint.EndPoint.Address, (int)_options.HealthCheckTimeout.TotalMilliseconds);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
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

            _logger.LogInformation("DnsServiceDiscovery 已释放资源");
        }

        #endregion
    }

    /// <summary>
    /// DNS 配置选项
    /// </summary>
    public class DnsOptions
    {
        /// <summary>
        /// 配置节名称
        /// </summary>
        public const string SectionName = "Dns";

        /// <summary>
        /// DNS 服务器列表（为空则使用系统默认）
        /// </summary>
        public string[]? DnsServers { get; set; }

        /// <summary>
        /// DNS 域名
        /// </summary>
        public string? DnsDomain { get; set; }

        /// <summary>
        /// 查询类型
        /// </summary>
        public DnsQueryType QueryType { get; set; } = DnsQueryType.Auto;

        /// <summary>
        /// 协议类型（用于 SRV 查询）
        /// </summary>
        public string? Protocol { get; set; } = "tcp";

        /// <summary>
        /// 默认端口（用于 A 记录查询）
        /// </summary>
        public int DefaultPort { get; set; } = 80;

        /// <summary>
        /// 是否启用 IPv6
        /// </summary>
        public bool EnableIPv6 { get; set; } = false;

        /// <summary>
        /// 是否启用缓存
        /// </summary>
        public bool EnableCaching { get; set; } = true;

        /// <summary>
        /// 缓存刷新间隔
        /// </summary>
        public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Watch 轮询间隔
        /// </summary>
        public TimeSpan WatchPollInterval { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// DNS 查询超时时间
        /// </summary>
        public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 健康检查超时时间
        /// </summary>
        public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>
        /// 是否启用健康检查
        /// </summary>
        public bool EnableHealthCheck { get; set; } = false;
    }

    /// <summary>
    /// DNS 查询类型
    /// </summary>
    public enum DnsQueryType
    {
        /// <summary>
        /// A 记录查询
        /// </summary>
        A,

        /// <summary>
        /// SRV 记录查询
        /// </summary>
        SRV,

        /// <summary>
        /// 自动选择（优先 SRV，降级到 A）
        /// </summary>
        Auto
    }

    /// <summary>
    /// SRV 记录
    /// </summary>
    internal class SrvRecord
    {
        public int Priority { get; set; }
        public int Weight { get; set; }
        public int Port { get; set; }
        public string Target { get; set; } = string.Empty;
    }
}
