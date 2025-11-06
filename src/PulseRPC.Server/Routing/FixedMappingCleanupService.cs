using dotnet_etcd;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace PulseRPC.Server.Routing;

/// <summary>
/// 固定映射清理服务
/// 定期清理过期的固定映射（兜底机制，Etcd lease已经自动处理大部分情况）
/// </summary>
public sealed class FixedMappingCleanupService : BackgroundService
{
    private readonly EtcdClient _etcdClient;
    private readonly ILogger<FixedMappingCleanupService> _logger;
    private readonly ClusterRoutingOptions _options;

    private string FixedMappingPrefix => $"{_options.EtcdKeyPrefix}/fixed/";

    public FixedMappingCleanupService(
        EtcdClient etcdClient,
        ILogger<FixedMappingCleanupService> logger,
        IOptions<ClusterRoutingOptions> options)
    {
        _etcdClient = etcdClient ?? throw new ArgumentNullException(nameof(etcdClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "固定映射清理服务已启动，检查间隔: {Interval}分钟",
            _options.CleanupInterval.TotalMinutes);

        // 延迟启动，避免启动时立即执行
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredMappingsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理过期映射时发生错误");
            }

            try
            {
                await Task.Delay(_options.CleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("固定映射清理服务已停止");
    }

    /// <summary>
    /// 清理过期的映射
    /// </summary>
    private async Task CleanupExpiredMappingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("开始清理过期的固定映射...");

            // 获取所有固定映射
            var response = await _etcdClient.GetRangeAsync(
                FixedMappingPrefix,
                cancellationToken: cancellationToken);

            var now = DateTime.UtcNow;
            var cleanedCount = 0;
            var totalCount = response.Kvs.Count;

            foreach (var kv in response.Kvs)
            {
                try
                {
                    var json = kv.Value.ToStringUtf8();
                    var location = JsonSerializer.Deserialize<ServiceFixedLocation>(json);

                    if (location != null && location.IsExpired())
                    {
                        // 删除过期映射
                        await _etcdClient.DeleteAsync(kv.Key.ToString(), cancellationToken: cancellationToken);
                        cleanedCount++;

                        _logger.LogInformation(
                            "清理过期映射: ServiceIdHash={Hash:X16}, 节点={NodeId}, 过期时间={Expires}, 已过期={Elapsed}",
                            location.ServiceIdHash,
                            location.NodeId,
                            location.ExpiresAt,
                            now - location.ExpiresAt);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "处理单个固定映射时发生错误");
                }
            }

            if (cleanedCount > 0)
            {
                _logger.LogInformation(
                    "清理了 {CleanedCount}/{TotalCount} 个过期的固定映射",
                    cleanedCount, totalCount);
            }
            else if (totalCount > 0)
            {
                _logger.LogDebug(
                    "检查了 {TotalCount} 个固定映射，未发现过期项",
                    totalCount);
            }

            // 记录统计信息
            await LogMappingStatisticsAsync(response.Kvs.Count - cleanedCount, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理过期映射失败");
        }
    }

    /// <summary>
    /// 记录映射统计信息
    /// </summary>
    private async Task LogMappingStatisticsAsync(int activeCount, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _etcdClient.GetRangeAsync(
                FixedMappingPrefix,
                cancellationToken: cancellationToken);

            var now = DateTime.UtcNow;
            var expiringIn1Hour = 0;
            var expiringIn6Hours = 0;
            var expiringIn24Hours = 0;

            foreach (var kv in response.Kvs)
            {
                try
                {
                    var json = kv.Value.ToStringUtf8();
                    var location = JsonSerializer.Deserialize<ServiceFixedLocation>(json);

                    if (location != null && !location.IsExpired())
                    {
                        var remaining = location.RemainingTime();

                        if (remaining.TotalHours <= 1)
                        {
                            expiringIn1Hour++;
                        }
                        else if (remaining.TotalHours <= 6)
                        {
                            expiringIn6Hours++;
                        }
                        else if (remaining.TotalHours <= 24)
                        {
                            expiringIn24Hours++;
                        }
                    }
                }
                catch
                {
                    // 忽略反序列化错误
                }
            }

            _logger.LogInformation(
                "固定映射统计: 总数={Total}, 1小时内过期={In1H}, 6小时内过期={In6H}, 24小时内过期={In24H}",
                activeCount, expiringIn1Hour, expiringIn6Hours, expiringIn24Hours);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "记录映射统计信息失败");
        }
    }

    /// <summary>
    /// 获取范围查询的结束键
    /// </summary>
    private static string GetRangeEnd(string key)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(key);
        var lastByte = bytes[^1];

        if (lastByte < 0xFF)
        {
            bytes[^1] = (byte)(lastByte + 1);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        return key + "~";
    }
}
