using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Consul;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Clustering;
using PulseRPC.Infrastructure.Discovery;

namespace PulseRPC.Infrastructure.Consul;

/// <summary>
/// 基于 Consul 的 <see cref="IDiscoveryProvider"/> 实现（§P8）。
/// </summary>
/// <remarks>
/// <para>
/// 每个节点以同一 <see cref="ConsulDiscoveryOptions.ServiceName"/> 注册一个服务实例，实例的
/// <c>Address:Port</c> 即节点对外端点，节点标识存放在 Meta（<see cref="ConsulDiscoveryOptions.NodeIdMetaKey"/>）。
/// 注册时附带一个 TCP 健康检查指向节点自身端口，令 Consul 自动探活并在持续失败后按
/// <see cref="ConsulDiscoveryOptions.DeregisterCriticalAfter"/> 注销——因此节点崩溃后无需本进程参与即可被清理。
/// </para>
/// <para>
/// <see cref="FetchNodesAsync"/> 只返回健康（passing）实例；启用 watch 时用 Consul 阻塞查询在成员变更时
/// 即时触发 <see cref="Changed"/>，否则由上层 <see cref="DiscoveryClusterMembership"/> 的轮询兜底。
/// </para>
/// <para>
/// 说明：与真实 Consul agent 的交互无法离线单元测试；本类逻辑经集成环境（Testcontainers/真实 agent）验证，
/// 通用的成员/端点/变更检测逻辑由 <see cref="DiscoveryClusterMembership"/> 的单元测试覆盖。
/// </para>
/// </remarks>
public sealed class ConsulDiscoveryProvider : IDiscoveryProvider, IDisposable
{
    private readonly ConsulDiscoveryOptions _options;
    private readonly ILogger<ConsulDiscoveryProvider> _logger;
    private readonly IConsulClient _client;

    private CancellationTokenSource? _watchCts;
    private Task? _watchTask;
    private string _serviceInstanceId = string.Empty;
    private bool _disposed;

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>创建 Consul 发现后端。</summary>
    public ConsulDiscoveryProvider(IOptions<ConsulDiscoveryOptions> options, ILogger<ConsulDiscoveryProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _client = new ConsulClient(cfg =>
        {
            cfg.Address = new Uri(_options.Address);
            if (!string.IsNullOrEmpty(_options.Token))
            {
                cfg.Token = _options.Token;
            }
        });
    }

    /// <summary>测试可见构造：注入自定义 <see cref="IConsulClient"/>。</summary>
    internal ConsulDiscoveryProvider(IConsulClient client, ConsulDiscoveryOptions options, ILogger<ConsulDiscoveryProvider> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task RegisterAsync(DiscoveredNode self, CancellationToken cancellationToken = default)
    {
        _serviceInstanceId = $"{_options.ServiceName}-{self.NodeId}";

        var registration = new AgentServiceRegistration
        {
            ID = _serviceInstanceId,
            Name = _options.ServiceName,
            Address = self.Endpoint.Host,
            Port = self.Endpoint.Port,
            Meta = new Dictionary<string, string> { [_options.NodeIdMetaKey] = self.NodeId },
            Check = new AgentServiceCheck
            {
                TCP = $"{self.Endpoint.Host}:{self.Endpoint.Port}",
                Interval = _options.HealthCheckInterval,
                DeregisterCriticalServiceAfter = _options.DeregisterCriticalAfter,
            },
        };

        await _client.Agent.ServiceRegister(registration, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "已向 Consul 注册节点 '{NodeId}'（服务实例 '{InstanceId}' @ {Host}:{Port}）",
            self.NodeId, _serviceInstanceId, self.Endpoint.Host, self.Endpoint.Port);

        if (_options.EnableWatch)
        {
            _watchCts = new CancellationTokenSource();
            _watchTask = Task.Run(() => WatchLoopAsync(_watchCts.Token), CancellationToken.None);
        }
    }

    /// <inheritdoc/>
    public async Task DeregisterAsync(DiscoveredNode self, CancellationToken cancellationToken = default)
    {
        _watchCts?.Cancel();

        if (!string.IsNullOrEmpty(_serviceInstanceId))
        {
            await _client.Agent.ServiceDeregister(_serviceInstanceId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DiscoveredNode>> FetchNodesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _client.Health.Service(_options.ServiceName, tag: string.Empty, passingOnly: true, cancellationToken).ConfigureAwait(false);
        return MapEntries(result.Response);
    }

    private List<DiscoveredNode> MapEntries(ServiceEntry[]? entries)
    {
        var nodes = new List<DiscoveredNode>();
        if (entries is null)
        {
            return nodes;
        }

        foreach (var entry in entries)
        {
            var service = entry.Service;
            if (service is null)
            {
                continue;
            }

            var nodeId = service.Meta != null && service.Meta.TryGetValue(_options.NodeIdMetaKey, out var id) && !string.IsNullOrEmpty(id)
                ? id
                : service.ID; // 回退：没有 Meta 时用服务实例 ID。

            if (string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(service.Address) || service.Port <= 0)
            {
                continue;
            }

            nodes.Add(new DiscoveredNode(nodeId, new NodeEndpoint(service.Address, service.Port)));
        }

        return nodes;
    }

    private async Task WatchLoopAsync(CancellationToken cancellationToken)
    {
        ulong lastIndex = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var queryOptions = new QueryOptions { WaitIndex = lastIndex, WaitTime = TimeSpan.FromMinutes(5) };
                var result = await _client.Health.Service(_options.ServiceName, string.Empty, true, queryOptions, cancellationToken).ConfigureAwait(false);

                // LastIndex 变化即表示成员/健康状态可能变化：通知上层立即拉取一次。
                if (result.LastIndex != lastIndex)
                {
                    lastIndex = result.LastIndex;
                    Changed?.Invoke();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Consul watch 阻塞查询失败，短暂退避后重试");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
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
        _watchCts?.Cancel();
        _watchCts?.Dispose();
        _client.Dispose();
    }
}
