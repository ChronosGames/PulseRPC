using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Clustering;

namespace PulseRPC.Infrastructure.Discovery;

/// <summary>
/// <see cref="DiscoveryClusterMembership"/> 的配置项。
/// </summary>
public sealed class DiscoveryOptions
{
    /// <summary>本节点标识。</summary>
    public string LocalNodeId { get; set; } = string.Empty;

    /// <summary>本节点对外公布的主机名/IP（供其它节点建立连接）。</summary>
    public string AdvertiseHost { get; set; } = string.Empty;

    /// <summary>本节点对外公布的端口。</summary>
    public int AdvertisePort { get; set; }

    /// <summary>轮询发现后端的周期（作为 watch 的兜底；纯轮询后端的收敛延迟上限）。默认 10 秒。</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// 服务发现驱动的集群成员视图 —— 同时实现 <see cref="IClusterMembership"/>（存活集）与
/// <see cref="INodeEndpointResolver"/>（端点解析），把各后端共有的
/// "自注册 + 周期拉取 + watch 触发 + 变更检测 + 端点缓存 + 健康提示" 逻辑集中承载，
/// 使具体后端只需实现薄薄的 <see cref="IDiscoveryProvider"/>（对应设计文档路线图 P8）。
/// </summary>
/// <remarks>
/// <para>
/// 作为 <see cref="IHostedService"/>：启动时向后端注册本节点并首拉一次成员，随后由后台循环
/// （<see cref="DiscoveryOptions.PollInterval"/> 周期 + <see cref="IDiscoveryProvider.Changed"/> 触发）持续刷新；
/// 停止时注销本节点。
/// </para>
/// <para>
/// 与静态 <c>StaticClusterMembership</c> 的健康语义保持一致：<see cref="ReportNodeFailure"/>/<see cref="ReportNodeSuccess"/>
/// 作为路由层的健康提示，把可疑节点即时移出/纳回存活集；但<strong>权威成员集来自发现后端</strong>——
/// 每次后端刷新都会以后端结果为准重算存活集（被后端移除的节点即下线，重新出现的节点即上线）。
/// 本节点永不被移出存活集。
/// </para>
/// </remarks>
public sealed class DiscoveryClusterMembership : IClusterMembership, INodeEndpointResolver, IHostedService, IDisposable
{
    private readonly IDiscoveryProvider _provider;
    private readonly ILogger<DiscoveryClusterMembership> _logger;
    private readonly string _localNodeId;
    private readonly DiscoveredNode _self;
    private readonly TimeSpan _pollInterval;

    private readonly object _gate = new();
    // 后端权威成员：nodeId -> endpoint。
    private readonly Dictionary<string, NodeEndpoint> _discovered = new(StringComparer.Ordinal);
    // 路由层健康提示判定为可疑（临时移出存活集）的节点。
    private readonly HashSet<string> _suspected = new(StringComparer.Ordinal);

    private volatile IReadOnlyList<string> _liveSnapshot = Array.Empty<string>();
    private volatile Dictionary<string, NodeEndpoint> _endpointSnapshot = new(StringComparer.Ordinal);

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private readonly SemaphoreSlim _refreshSignal = new(0);
    private bool _disposed;

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>创建服务发现集群成员视图。</summary>
    public DiscoveryClusterMembership(
        IDiscoveryProvider provider,
        IOptions<DiscoveryOptions> options,
        ILogger<DiscoveryClusterMembership> logger)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value ?? throw new ArgumentNullException(nameof(options));

        _localNodeId = string.IsNullOrEmpty(value.LocalNodeId)
            ? throw new InvalidOperationException("DiscoveryOptions.LocalNodeId 未配置。")
            : value.LocalNodeId;
        _self = new DiscoveredNode(_localNodeId, new NodeEndpoint(value.AdvertiseHost, value.AdvertisePort));
        _pollInterval = value.PollInterval > TimeSpan.Zero ? value.PollInterval : TimeSpan.FromSeconds(10);

        // 初始存活集至少含本节点，避免启动到首拉之间路由无属主。
        _discovered[_localNodeId] = _self.Endpoint;
        RebuildSnapshotLocked();
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> LiveNodeIds => _liveSnapshot;

    /// <inheritdoc/>
    public bool TryResolve(string nodeId, out NodeEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        return _endpointSnapshot.TryGetValue(nodeId, out endpoint);
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _provider.RegisterAsync(_self, cancellationToken).ConfigureAwait(false);
        _provider.Changed += OnProviderChanged;

        await RefreshOnceAsync(cancellationToken).ConfigureAwait(false);

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = Task.Run(() => RefreshLoopAsync(_loopCts.Token), CancellationToken.None);
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _provider.Changed -= OnProviderChanged;

        if (_loopCts is not null)
        {
            _loopCts.Cancel();
        }

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 正常停止。
            }
        }

        try
        {
            await _provider.DeregisterAsync(_self, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "从服务发现后端注销节点 '{NodeId}' 失败（下线时忽略）", _localNodeId);
        }
    }

    private void OnProviderChanged()
    {
        // 后端 watch 触发：唤醒刷新循环立即拉取一次。Release 可能超过初值，用 try/catch 兜底幂等。
        try
        {
            _refreshSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // 已有待处理的刷新信号，忽略。
        }
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // 等待"轮询周期到期"或"watch 触发信号"二者之一。
                await WaitForNextRefreshAsync(cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await RefreshOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "服务发现刷新循环出现异常，将在下个周期重试");
            }
        }
    }

    private async Task WaitForNextRefreshAsync(CancellationToken cancellationToken)
    {
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var signalTask = _refreshSignal.WaitAsync(delayCts.Token);
        var delayTask = Task.Delay(_pollInterval, delayCts.Token);

        var completed = await Task.WhenAny(signalTask, delayTask).ConfigureAwait(false);
        delayCts.Cancel(); // 取消另一个等待，避免泄漏。

        // 吞掉被取消的那个任务的异常。
        try { await signalTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        try { await delayTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _ = completed;
    }

    private async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DiscoveredNode> nodes;
        try
        {
            nodes = await _provider.FetchNodesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "从服务发现后端拉取节点列表失败，保留上一份成员视图");
            return;
        }

        var changed = false;
        lock (_gate)
        {
            var next = new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal);
            foreach (var node in nodes)
            {
                if (!string.IsNullOrEmpty(node.NodeId))
                {
                    next[node.NodeId] = node.Endpoint;
                }
            }

            // 本节点始终在册（即便后端因短暂不一致漏掉了自己）。
            next[_localNodeId] = _self.Endpoint;

            if (!DiscoveredEquals(_discovered, next))
            {
                _discovered.Clear();
                foreach (var kvp in next)
                {
                    _discovered[kvp.Key] = kvp.Value;
                }

                // 后端权威刷新后，清理"已不在后端成员集中的可疑标记"（这些节点已被后端判定下线，
                // 无需再由健康提示单独维持）。仍在成员集中的可疑标记保留，直到成功上报或后端移除。
                _suspected.RemoveWhere(id => !_discovered.ContainsKey(id));

                changed = RebuildSnapshotLocked();
            }
        }

        if (changed)
        {
            RaiseChanged();
        }
    }

    /// <inheritdoc/>
    public void ReportNodeFailure(string nodeId)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        if (string.Equals(nodeId, _localNodeId, StringComparison.Ordinal))
        {
            return;
        }

        var changed = false;
        lock (_gate)
        {
            // 只对"后端仍认为在册"的节点做临时可疑移除；未知节点忽略。
            if (_discovered.ContainsKey(nodeId) && _suspected.Add(nodeId))
            {
                changed = RebuildSnapshotLocked();
            }
        }

        if (changed)
        {
            RaiseChanged();
        }
    }

    /// <inheritdoc/>
    public void ReportNodeSuccess(string nodeId)
    {
        ArgumentNullException.ThrowIfNull(nodeId);

        var changed = false;
        lock (_gate)
        {
            if (_suspected.Remove(nodeId))
            {
                changed = RebuildSnapshotLocked();
            }
        }

        if (changed)
        {
            RaiseChanged();
        }
    }

    /// <summary>在锁内重建存活集与端点快照；返回存活集是否变化。</summary>
    private bool RebuildSnapshotLocked()
    {
        var endpoints = new Dictionary<string, NodeEndpoint>(_discovered, StringComparer.Ordinal);

        var live = _discovered.Keys
            .Where(id => !_suspected.Contains(id) || string.Equals(id, _localNodeId, StringComparison.Ordinal))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        if (live.Length == 0)
        {
            live = new[] { _localNodeId };
        }

        _endpointSnapshot = endpoints;

        var previous = _liveSnapshot;
        if (previous.Count == live.Length && previous.SequenceEqual(live, StringComparer.Ordinal))
        {
            return false;
        }

        _liveSnapshot = live;
        return true;
    }

    private void RaiseChanged()
    {
        var handlers = Changed;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch
            {
                // 订阅者（环重建）异常不应影响发现刷新。
            }
        }
    }

    private static bool DiscoveredEquals(Dictionary<string, NodeEndpoint> a, Dictionary<string, NodeEndpoint> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (var kvp in a)
        {
            if (!b.TryGetValue(kvp.Key, out var endpoint) || !endpoint.Equals(kvp.Value))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loopCts?.Dispose();
        _refreshSignal.Dispose();
        (_provider as IDisposable)?.Dispose();
    }
}
