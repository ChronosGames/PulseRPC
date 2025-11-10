using Consul;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.Infrastructure.ServicePatterns.Examples;

/// <summary>
/// Consul 服务示例 - 使用 IOBoundServiceBase
///
/// 配置: 4个连接 + 8个Worker
/// 场景: 服务发现、KV存储、配置管理
/// </summary>
public class ConsulServiceExample : IOBoundServiceBase<IConsulClient>
{
    private readonly string _consulAddress;

    public ConsulServiceExample(
        string consulAddress,
        ILogger<ConsulServiceExample>? logger = null)
        : base(new IOBoundServiceOptions
        {
            MinConnections = 2,
            MaxConnections = 4,
            WorkerCount = 8
        }, logger)
    {
        _consulAddress = consulAddress;
    }

    /// <summary>
    /// 创建 Consul 连接
    /// </summary>
    protected override Task<IConsulClient> CreateConnectionAsync()
    {
        var client = new ConsulClient(config =>
        {
            config.Address = new Uri(_consulAddress);
            config.WaitTime = TimeSpan.FromSeconds(30);
        });

        return Task.FromResult<IConsulClient>(client);
    }

    /// <summary>
    /// 验证连接是否有效
    /// </summary>
    protected override async Task<bool> ValidateConnectionAsync(IConsulClient connection)
    {
        try
        {
            // 尝试获取 leader 状态来验证连接
            var result = await connection.Status.Leader();
            return !string.IsNullOrEmpty(result);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 释放连接
    /// </summary>
    protected override void DisposeConnection(IConsulClient connection)
    {
        connection?.Dispose();
    }

    // ==================== 业务方法 ====================

    /// <summary>
    /// 获取服务列表 - 自动负载均衡
    /// </summary>
    public async Task<List<ServiceEntry>> GetServicesAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async (client, ct) =>
        {
            var result = await client.Health.Service(serviceName, null, true, ct);
            return result.Response.ToList();
        }, cancellationToken);
    }

    /// <summary>
    /// 获取 KV 值 - 基于 Key 哈希分配到固定 Worker
    /// 优势: 同一个 Key 总是在同一个 Worker 中处理，提高缓存命中率
    /// </summary>
    public async Task<string?> GetKeyValueAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(key, async (client, ct) =>
        {
            var result = await client.KV.Get(key, ct);
            if (result.Response == null)
                return null;

            return System.Text.Encoding.UTF8.GetString(result.Response.Value);
        }, cancellationToken);
    }

    /// <summary>
    /// 设置 KV 值 - 基于 Key 哈希
    /// </summary>
    public async Task<bool> SetKeyValueAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(key, async (client, ct) =>
        {
            var kvPair = new KVPair(key)
            {
                Value = System.Text.Encoding.UTF8.GetBytes(value)
            };

            var result = await client.KV.Put(kvPair, ct);
            return result.Response;
        }, cancellationToken);
    }

    /// <summary>
    /// 批量获取 KV 值 - 利用分片优化
    /// </summary>
    public async Task<Dictionary<string, string?>> BatchGetKeyValuesAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        var tasks = keys.Select(key =>
            GetKeyValueAsync(key, cancellationToken)
                .ContinueWith(t => new KeyValuePair<string, string?>(key, t.Result), cancellationToken));

        var results = await Task.WhenAll(tasks);
        return results.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// 发现最佳服务 - 基于负载
    /// </summary>
    public async Task<ServiceEntry?> DiscoverBestServiceAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async (client, ct) =>
        {
            var result = await client.Health.Service(serviceName, null, true, ct);
            var services = result.Response.ToList();

            if (services.Count == 0)
                return null;

            // 选择负载最低的服务
            return services
                .Where(s => s.Checks.All(c => c.Status == HealthStatus.Passing))
                .OrderBy(s => GetServiceLoad(s))
                .FirstOrDefault();
        }, cancellationToken);
    }

    private static int GetServiceLoad(ServiceEntry entry)
    {
        if (entry.Service.Meta?.TryGetValue("CurrentLoad", out var loadStr) == true
            && int.TryParse(loadStr, out var load))
        {
            return load;
        }

        return 0;
    }
}
