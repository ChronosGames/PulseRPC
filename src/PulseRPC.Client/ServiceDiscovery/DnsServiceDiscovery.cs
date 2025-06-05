using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.ServiceDiscovery;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Text;

namespace PulseRPC.Client.ServiceDiscovery;

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
    private readonly Timer? _refreshTimer;
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

    public Task<IReadOnlyList<string>> GetServiceNamesAsync(CancellationToken cancellationToken = default)
    {
        // DNS 服务发现无法枚举所有服务名称
        return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

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
        Dictionary<string, string> tags,
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
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
                    if (currentEndpointsHash == lastEndpointsHash)
                    {
                        continue;
                    }

                    _logger.LogDebug("检测到 DNS 服务变化: {ServiceName}, 当前端点数: {Count}",
                        serviceName, updatedEndpoints.Count);

                    // 清除缓存
                    InvalidateServiceCache(serviceName);

                    // 更新缓存
                    if (_options.EnableCaching)
                    {
                        _serviceCache.TryUpdate(serviceName, updatedEndpoints.ToArray(), 
                            _serviceCache.GetValueOrDefault(serviceName, Array.Empty<ServiceEndpoint>()));
                    }

                    yield return updatedEndpoints.ToArray();
                    lastEndpointsHash = currentEndpointsHash;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "DNS 监听过程中发生异常: {ServiceName}", serviceName);
                    await Task.Delay(TimeSpan.FromSeconds(5), watchCts.Token);
                }
            }
        }
        finally
        {
            _watchCancellations.TryRemove(serviceName, out _);
            watchCts.Dispose();
        }
    }

    #region Private Methods

    private async Task<List<ServiceEndpoint>> QueryServiceEndpointsAsync(string serviceName, CancellationToken cancellationToken)
    {
        var endpoints = new List<ServiceEndpoint>();

        switch (_options.QueryType)
        {
            case DnsQueryType.A:
                endpoints.AddRange(await QueryARecordsAsync(serviceName, cancellationToken));
                break;
            case DnsQueryType.SRV:
                endpoints.AddRange(await QuerySrvRecordsAsync(serviceName, cancellationToken));
                break;
            case DnsQueryType.Auto:
                // 优先尝试 SRV 记录
                try
                {
                    var srvEndpoints = await QuerySrvRecordsAsync(serviceName, cancellationToken);
                    if (srvEndpoints.Count > 0)
                    {
                        endpoints.AddRange(srvEndpoints);
                    }
                    else
                    {
                        // 降级到 A 记录
                        endpoints.AddRange(await QueryARecordsAsync(serviceName, cancellationToken));
                    }
                }
                catch
                {
                    // SRV 查询失败，降级到 A 记录
                    endpoints.AddRange(await QueryARecordsAsync(serviceName, cancellationToken));
                }
                break;
        }

        return endpoints;
    }

    private async Task<List<ServiceEndpoint>> QueryARecordsAsync(string serviceName, CancellationToken cancellationToken)
    {
        var endpoints = new List<ServiceEndpoint>();
        var hostname = BuildHostname(serviceName);

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(hostname);

            foreach (var address in addresses)
            {
                // 根据配置过滤IPv6地址
                if (!_options.EnableIPv6 && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    continue;
                }

                var endpoint = new ServiceEndpoint
                {
                    ServiceId = $"{serviceName}-{address}",
                    ServiceName = serviceName,
                    EndPoint = new IPEndPoint(address, _options.DefaultPort),
                    HealthStatus = HealthStatus.Unknown,
                    Tags = new Dictionary<string, string>
                    {
                        ["source"] = "dns-a",
                        ["hostname"] = hostname
                    }
                };

                endpoints.Add(endpoint);
            }

            _logger.LogDebug("DNS A记录查询成功: {Hostname}, 地址数量: {Count}", hostname, addresses.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DNS A记录查询失败: {Hostname}", hostname);
        }

        return endpoints;
    }

    private async Task<List<ServiceEndpoint>> QuerySrvRecordsAsync(string serviceName, CancellationToken cancellationToken)
    {
        var endpoints = new List<ServiceEndpoint>();
        var srvName = BuildSrvName(serviceName);

        try
        {
            var srvRecords = await QuerySrvRecordsUsingNslookup(srvName, cancellationToken);

            foreach (var srv in srvRecords)
            {
                try
                {
                    var addresses = await Dns.GetHostAddressesAsync(srv.Target);

                    foreach (var address in addresses)
                    {
                        if (!_options.EnableIPv6 && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                        {
                            continue;
                        }

                        var endpoint = new ServiceEndpoint
                        {
                            ServiceId = $"{serviceName}-{srv.Target}-{srv.Port}",
                            ServiceName = serviceName,
                            EndPoint = new IPEndPoint(address, srv.Port),
                            HealthStatus = HealthStatus.Unknown,
                            Tags = new Dictionary<string, string>
                            {
                                ["source"] = "dns-srv",
                                ["target"] = srv.Target,
                                ["priority"] = srv.Priority.ToString(),
                                ["weight"] = srv.Weight.ToString()
                            }
                        };

                        endpoints.Add(endpoint);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "解析SRV记录目标失败: {Target}", srv.Target);
                }
            }

            _logger.LogDebug("DNS SRV记录查询成功: {SrvName}, 记录数量: {Count}", srvName, srvRecords.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DNS SRV记录查询失败: {SrvName}", srvName);
        }

        return endpoints;
    }

    private async Task<List<string>> QueryTxtRecordsAsync(string serviceName, CancellationToken cancellationToken)
    {
        // 简化实现，实际可以通过更复杂的DNS查询获取TXT记录
        await Task.Delay(1, cancellationToken);
        return new List<string>();
    }

    private async Task<List<SrvRecord>> QuerySrvRecordsUsingNslookup(string srvName, CancellationToken cancellationToken)
    {
        // 简化实现，实际应该使用DNS库进行查询
        await Task.Delay(1, cancellationToken);
        return new List<SrvRecord>();
    }

    private string BuildHostname(string serviceName)
    {
        if (!string.IsNullOrEmpty(_options.DnsDomain))
        {
            return $"{serviceName}.{_options.DnsDomain}";
        }
        return serviceName;
    }

    private string BuildSrvName(string serviceName)
    {
        var protocol = _options.Protocol ?? "tcp";
        if (!string.IsNullOrEmpty(_options.DnsDomain))
        {
            return $"_{serviceName}._{protocol}.{_options.DnsDomain}";
        }
        return $"_{serviceName}._{protocol}";
    }

    private Dictionary<string, string> ParseTagsFromTxtRecords(List<string> txtRecords)
    {
        var tags = new Dictionary<string, string>();

        foreach (var record in txtRecords)
        {
            // 解析 key=value 格式的标签
            var parts = record.Split('=', 2);
            if (parts.Length == 2)
            {
                tags[parts[0].Trim()] = parts[1].Trim();
            }
        }

        return tags;
    }

    private string GetEndpointsHash(List<ServiceEndpoint> endpoints)
    {
        var combined = string.Join("|", endpoints.Select(e => $"{e.EndPoint}"));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(combined));
    }

    private void InvalidateServiceCache(string serviceName)
    {
        _serviceCache.TryRemove(serviceName, out _);
        _logger.LogDebug("已清除服务缓存: {ServiceName}", serviceName);
    }

    private async void RefreshCacheCallback(object? state)
    {
        if (_disposed) return;

        try
        {
            await _refreshSemaphore.WaitAsync();

            var servicesToRefresh = _serviceCache.Keys.ToList();
            foreach (var serviceName in servicesToRefresh)
            {
                try
                {
                    var endpoints = await QueryServiceEndpointsAsync(serviceName, CancellationToken.None);
                    _serviceCache.TryUpdate(serviceName, endpoints.ToArray(), 
                        _serviceCache.GetValueOrDefault(serviceName, Array.Empty<ServiceEndpoint>()));

                    _logger.LogDebug("已刷新服务缓存: {ServiceName}, 端点数量: {Count}", serviceName, endpoints.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "刷新服务缓存失败: {ServiceName}", serviceName);
                }
            }
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        // 停止所有监听
        foreach (var cts in _watchCancellations.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _watchCancellations.Clear();

        // 清理资源
        _refreshTimer?.Dispose();
        _refreshSemaphore.Dispose();

        _logger.LogInformation("DnsServiceDiscovery 已释放资源");
    }
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
    /// DNS 服务器列表
    /// </summary>
    public string[]? DnsServers { get; set; }

    /// <summary>
    /// DNS 域名
    /// </summary>
    public string? DnsDomain { get; set; }

    /// <summary>
    /// DNS 查询类型
    /// </summary>
    public DnsQueryType QueryType { get; set; } = DnsQueryType.Auto;

    /// <summary>
    /// 协议类型
    /// </summary>
    public string? Protocol { get; set; } = "tcp";

    /// <summary>
    /// 默认端口
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
    /// 监听轮询间隔
    /// </summary>
    public TimeSpan WatchPollInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 查询超时时间
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