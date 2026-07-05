using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dotnet_etcd;
using Etcdserverpb;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Clustering;
using PulseRPC.Infrastructure.Discovery;

namespace PulseRPC.Infrastructure.Etcd;

/// <summary>
/// 基于 etcd 的 <see cref="IDiscoveryProvider"/> 实现（§P8）。
/// </summary>
/// <remarks>
/// <para>
/// 每个节点以租约（lease）方式在 <c>{KeyPrefix}{nodeId}</c> 写入其端点 <c>host:port</c>，并在进程存活期间
/// 持续 keepalive 续租；进程崩溃/退出后租约到期，etcd 自动删除该键——因此下线检测无需本进程参与。
/// </para>
/// <para>
/// <see cref="FetchNodesAsync"/> 读取前缀下全部键即为当前存活成员；启用 watch 时对前缀做 etcd 原生
/// watch，成员变更即时触发 <see cref="Changed"/>，否则由上层 <see cref="DiscoveryClusterMembership"/> 轮询兜底。
/// </para>
/// <para>
/// 说明：与真实 etcd 的交互无法离线单元测试；通用成员/端点/变更检测逻辑由
/// <see cref="DiscoveryClusterMembership"/> 的单元测试覆盖。
/// </para>
/// </remarks>
public sealed class EtcdDiscoveryProvider : IDiscoveryProvider, IDisposable
{
    private readonly EtcdDiscoveryOptions _options;
    private readonly ILogger<EtcdDiscoveryProvider> _logger;
    private readonly EtcdClient _client;

    private CancellationTokenSource? _keepAliveCts;
    private CancellationTokenSource? _watchCts;
    private long _leaseId;
    private bool _disposed;

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>创建 etcd 发现后端。</summary>
    public EtcdDiscoveryProvider(IOptions<EtcdDiscoveryOptions> options, ILogger<EtcdDiscoveryProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _client = new EtcdClient(_options.ConnectionString);
    }

    private string KeyFor(string nodeId) => _options.KeyPrefix + nodeId;

    /// <inheritdoc/>
    public async Task RegisterAsync(DiscoveredNode self, CancellationToken cancellationToken = default)
    {
        var grant = await _client.LeaseGrantAsync(
            new LeaseGrantRequest { TTL = _options.LeaseTtlSeconds }, cancellationToken: cancellationToken).ConfigureAwait(false);
        _leaseId = grant.ID;

        await _client.PutAsync(
            new PutRequest
            {
                Key = ByteString.CopyFromUtf8(KeyFor(self.NodeId)),
                Value = ByteString.CopyFromUtf8($"{self.Endpoint.Host}:{self.Endpoint.Port}"),
                Lease = _leaseId,
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // 后台续租：保持本节点键存活，直至优雅下线（cancel）或进程死亡（租约到期，etcd 自动删除）。
        _keepAliveCts = new CancellationTokenSource();
        _ = Task.Run(() => KeepAliveAsync(_leaseId, _keepAliveCts.Token), CancellationToken.None);

        _logger.LogInformation(
            "已向 etcd 注册节点 '{NodeId}'（键 '{Key}' = {Host}:{Port}，租约 {LeaseId} TTL={Ttl}s）",
            self.NodeId, KeyFor(self.NodeId), self.Endpoint.Host, self.Endpoint.Port, _leaseId, _options.LeaseTtlSeconds);

        if (_options.EnableWatch)
        {
            _watchCts = new CancellationTokenSource();
            StartWatch(_watchCts.Token);
        }
    }

    private async Task KeepAliveAsync(long leaseId, CancellationToken cancellationToken)
    {
        try
        {
            // 该重载在 token 取消前持续按 TTL/3 间隔发送 keepalive。
            await _client.LeaseKeepAlive(leaseId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 正常停止。
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "etcd 租约 {LeaseId} keepalive 中断；租约到期后本节点键将被自动删除", leaseId);
        }
    }

    private void StartWatch(CancellationToken cancellationToken)
    {
        _ = Task.Run(() =>
        {
            try
            {
                _client.WatchRange(_options.KeyPrefix, (WatchResponse _) => Changed?.Invoke(), cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 正常停止。
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "etcd 前缀 watch 中断，将由轮询兜底成员刷新");
            }
        }, CancellationToken.None);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DiscoveredNode>> FetchNodesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _client.GetRangeAsync(_options.KeyPrefix, cancellationToken: cancellationToken).ConfigureAwait(false);
        var nodes = new List<DiscoveredNode>();

        foreach (var kv in response.Kvs)
        {
            var key = kv.Key.ToStringUtf8();
            var nodeId = key.Length > _options.KeyPrefix.Length ? key.Substring(_options.KeyPrefix.Length) : key;
            if (string.IsNullOrEmpty(nodeId))
            {
                continue;
            }

            if (TryParseEndpoint(kv.Value.ToStringUtf8(), out var endpoint))
            {
                nodes.Add(new DiscoveredNode(nodeId, endpoint));
            }
        }

        return nodes;
    }

    /// <summary>解析 <c>host:port</c>（按最后一个冒号切分，兼容 IPv6 字面量中的冒号）。</summary>
    public static bool TryParseEndpoint(string value, out NodeEndpoint endpoint)
    {
        endpoint = default;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var idx = value.LastIndexOf(':');
        if (idx <= 0 || idx == value.Length - 1)
        {
            return false;
        }

        var host = value.Substring(0, idx);
        if (!int.TryParse(value.Substring(idx + 1), out var port) || port is <= 0 or > 65535)
        {
            return false;
        }

        endpoint = new NodeEndpoint(host, port);
        return true;
    }

    /// <inheritdoc/>
    public async Task DeregisterAsync(DiscoveredNode self, CancellationToken cancellationToken = default)
    {
        _keepAliveCts?.Cancel();
        _watchCts?.Cancel();

        if (_leaseId != 0)
        {
            try
            {
                await _client.LeaseRevokeAsync(new LeaseRevokeRequest { ID = _leaseId }, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "撤销 etcd 租约 {LeaseId} 失败（下线时忽略，租约会自动过期）", _leaseId);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _keepAliveCts?.Cancel();
        _keepAliveCts?.Dispose();
        _watchCts?.Cancel();
        _watchCts?.Dispose();
        _client.Dispose();
    }
}
