using Consul;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DistributedGameApp.Infrastructure.Hosting.Bootstrap;

/// <summary>
/// 阶段4: 同步例外信息（黑名单、白名单等）
/// </summary>
public class Phase4_SyncExceptionList : IBootstrapPhase
{
    private readonly ILogger<Phase4_SyncExceptionList> _logger;

    public string PhaseName => "Phase 4: Sync Exception List";

    // Consul KV 存储路径
    private const string KV_PREFIX = "distributed-game/exception-list/";
    private const string BLACKLIST_KEY = KV_PREFIX + "blacklist";
    private const string WHITELIST_KEY = KV_PREFIX + "whitelist";
    private const string BANNED_IPS_KEY = KV_PREFIX + "banned-ips";
    private const string PRIORITY_NODES_KEY = KV_PREFIX + "priority-nodes";

    public Phase4_SyncExceptionList(ILogger<Phase4_SyncExceptionList> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> ExecuteAsync(BootstrapContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("========== {PhaseName} ==========", PhaseName);

        try
        {
            // 获取 Consul 客户端
            var consulOptions = context.ServiceProvider.GetService<IOptions<DistributedGameApp.Infrastructure.Consul.ConsulOptions>>();
            if (consulOptions == null)
            {
                _logger.LogWarning("Consul 未配置，跳过例外信息同步");
                return true;
            }

            var consulClient = new ConsulClient(config =>
            {
                config.Address = new Uri(consulOptions.Value.Address);
            });

            _logger.LogInformation("正在从 Consul KV 存储同步例外信息...");

            // 同步黑名单
            await SyncBlacklistAsync(consulClient, context, cancellationToken);

            // 同步白名单
            await SyncWhitelistAsync(consulClient, context, cancellationToken);

            // 同步被封禁的 IP
            await SyncBannedIPsAsync(consulClient, context, cancellationToken);

            // 同步优先路由节点
            await SyncPriorityNodesAsync(consulClient, context, cancellationToken);

            _logger.LogInformation(
                "✓ 例外信息同步完成 (黑名单: {Blacklist}, 白名单: {Whitelist}, 封禁IP: {BannedIPs}, 优先节点: {PriorityNodes})",
                context.ExceptionList.Blacklist.Count,
                context.ExceptionList.Whitelist.Count,
                context.ExceptionList.BannedIPs.Count,
                context.ExceptionList.PriorityNodes.Count);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "例外信息同步失败");
            // 例外信息同步失败不影响启动流程
            return true;
        }
    }

    private async Task SyncBlacklistAsync(IConsulClient client, BootstrapContext context, CancellationToken cancellationToken)
    {
        try
        {
            var result = await client.KV.Get(BLACKLIST_KEY, cancellationToken);
            if (result.Response?.Value != null)
            {
                var json = System.Text.Encoding.UTF8.GetString(result.Response.Value);
                var list = JsonSerializer.Deserialize<List<string>>(json);

                if (list != null)
                {
                    foreach (var item in list)
                    {
                        context.ExceptionList.Blacklist.Add(item);
                    }

                    _logger.LogInformation("  - 黑名单: {Count} 个节点", list.Count);
                }
            }
            else
            {
                _logger.LogDebug("  - 黑名单: 未配置");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "同步黑名单失败");
        }
    }

    private async Task SyncWhitelistAsync(IConsulClient client, BootstrapContext context, CancellationToken cancellationToken)
    {
        try
        {
            var result = await client.KV.Get(WHITELIST_KEY, cancellationToken);
            if (result.Response?.Value != null)
            {
                var json = System.Text.Encoding.UTF8.GetString(result.Response.Value);
                var list = JsonSerializer.Deserialize<List<string>>(json);

                if (list != null)
                {
                    foreach (var item in list)
                    {
                        context.ExceptionList.Whitelist.Add(item);
                    }

                    _logger.LogInformation("  - 白名单: {Count} 个节点", list.Count);
                }
            }
            else
            {
                _logger.LogDebug("  - 白名单: 未配置");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "同步白名单失败");
        }
    }

    private async Task SyncBannedIPsAsync(IConsulClient client, BootstrapContext context, CancellationToken cancellationToken)
    {
        try
        {
            var result = await client.KV.Get(BANNED_IPS_KEY, cancellationToken);
            if (result.Response?.Value != null)
            {
                var json = System.Text.Encoding.UTF8.GetString(result.Response.Value);
                var list = JsonSerializer.Deserialize<List<string>>(json);

                if (list != null)
                {
                    foreach (var item in list)
                    {
                        context.ExceptionList.BannedIPs.Add(item);
                    }

                    _logger.LogInformation("  - 封禁IP: {Count} 个", list.Count);
                }
            }
            else
            {
                _logger.LogDebug("  - 封禁IP: 未配置");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "同步封禁IP失败");
        }
    }

    private async Task SyncPriorityNodesAsync(IConsulClient client, BootstrapContext context, CancellationToken cancellationToken)
    {
        try
        {
            var result = await client.KV.Get(PRIORITY_NODES_KEY, cancellationToken);
            if (result.Response?.Value != null)
            {
                var json = System.Text.Encoding.UTF8.GetString(result.Response.Value);
                var dict = JsonSerializer.Deserialize<Dictionary<string, int>>(json);

                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        context.ExceptionList.PriorityNodes[kvp.Key] = kvp.Value;
                    }

                    _logger.LogInformation("  - 优先节点: {Count} 个", dict.Count);
                }
            }
            else
            {
                _logger.LogDebug("  - 优先节点: 未配置");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "同步优先节点失败");
        }
    }
}
