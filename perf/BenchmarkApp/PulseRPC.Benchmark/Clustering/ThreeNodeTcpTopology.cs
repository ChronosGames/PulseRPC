using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PulseRPC.Clustering;
using PulseRPC.Messaging;
using PulseRPC.Routing;
using PulseRPC.Serialization;
using PulseRPC.Server;
using PulseRPC.Server.Clustering;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Extensions;
using PulseRPC.Server.Gateway;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Server.Security;

namespace PulseRPC.Benchmark.Clustering;

/// <summary>
/// 可由集成测试和 BenchmarkApp 共同使用的真实 TCP 三节点拓扑。
/// </summary>
public sealed class ThreeNodeTcpTopology : IAsyncDisposable
{
    public const string NodeA = "node-a";
    public const string NodeB = "node-b";
    public const string NodeC = "node-c";
    public const string EntryHub = "ThreeHopEntry";
    public const string TerminalHub = "ThreeHopTerminal";
    public const ushort EntryProtocolId = 0xE301;
    public const ushort TerminalProtocolId = 0xE302;

    private const string SharedSecret = "three-hop-loopback-test-secret";

    private readonly ThreeNodeTcpTopologyOptions _options;
    private readonly InMemoryActorLeaseStore _leaseStore = new();
    private readonly InProcessBackplane _backplane = new();
    private readonly ThreeHopProbe _probe = new();
    private readonly Dictionary<string, int> _ports;
    private readonly string _entryKey;
    private readonly Dictionary<string, NodeRuntime> _hosts = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private ForwardingPause? _forwardingPause;
    private int _disposed;

    private ThreeNodeTcpTopology(ThreeNodeTcpTopologyOptions options)
    {
        _options = options;
        _ports = AllocateLoopbackPorts(NodeA, NodeB, NodeC);
        _entryKey = FindKeyOwnedBy(NodeB);
    }

    /// <summary>节点被判定不可达后，重新进入成员视图前的 TTL。</summary>
    public TimeSpan RecoveryTtl => _options.RecoveryTtl;

    /// <summary>终点 C 实际执行业务处理的次数。</summary>
    public long TerminalExecutionCount => _probe.TerminalExecutionCount;

    /// <summary>中继 B 实际执行转发处理的次数。</summary>
    public long EntryExecutionCount => _probe.EntryExecutionCount;

    /// <summary>启动 A、B、C 三个独立 PulseServer，所有端点均绑定 loopback TCP。</summary>
    public static async Task<ThreeNodeTcpTopology> StartAsync(
        ThreeNodeTcpTopologyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var topology = new ThreeNodeTcpTopology(options ?? new ThreeNodeTcpTopologyOptions());
        try
        {
            await topology.StartNodeAsync(NodeA, cancellationToken).ConfigureAwait(false);
            await topology.StartNodeAsync(NodeB, cancellationToken).ConfigureAwait(false);
            await topology.StartNodeAsync(NodeC, cancellationToken).ConfigureAwait(false);
            return topology;
        }
        catch
        {
            await topology.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>以外部用户进入 A 的 Gateway Front，请求 B，并由 B 再经真实节点 TCP 请求 C。</summary>
    public async ValueTask<string> InvokeAsync(
        string payload,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var body = MemoryPackSerializer.Serialize(payload ?? string.Empty);
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "three-hop-user"),
                new Claim(ClaimTypes.Name, "Three Hop User"),
                new Claim(ClaimTypes.Role, "benchmark-user"),
                new Claim("tenant", "cluster-e2e", ClaimValueTypes.String, "three-hop-issuer"),
            },
            "three-hop-test");
        var authentication = new AuthenticationContext("three-hop-client");
        authentication.SetClientAuthentication(
            "three-hop-user",
            "Three Hop User",
            token: "must-not-cross-node",
            principal: new ClaimsPrincipal(identity));
        using var callerScope = PulseContext.SetContext(
            PulseContextData.FromAuthenticationContext(authentication) with
            {
                ConnectionId = "three-hop-client",
                CallerId = "three-hop-user",
                UserId = "three-hop-user",
                Permissions = new HashSet<string>(StringComparer.Ordinal) { "cluster.invoke" },
                Roles = new HashSet<string>(StringComparer.Ordinal) { "benchmark-user" },
                ExpiresAt = DateTime.UtcNow.AddMinutes(5),
                CancellationToken = cancellationToken,
            });
        var gateway = GetRequiredService<IGatewayFrontHub>(NodeA);
        var response = await gateway.RelayAskAsync(
            EntryHub,
            _entryKey,
            EntryProtocolId,
            body,
            hopLimit: 3).ConfigureAwait(false);

        return MemoryPackSerializer.Deserialize<string>(response) ?? string.Empty;
    }

    /// <summary>让下一次 B→C 转发停在真正发起网络请求之前，供故障注入使用。</summary>
    public void PauseBeforeTerminalForward()
    {
        ThrowIfDisposed();
        var pause = new ForwardingPause();
        if (Interlocked.CompareExchange(ref _forwardingPause, pause, null) is not null)
        {
            throw new InvalidOperationException("已有一次三跳调用处于故障注入暂停状态。");
        }
    }

    /// <summary>等待 B 已收到 A 的请求并即将向 C 转发。</summary>
    public Task WaitUntilTerminalForwardAsync(CancellationToken cancellationToken = default)
    {
        var pause = Volatile.Read(ref _forwardingPause)
            ?? throw new InvalidOperationException("尚未调用 PauseBeforeTerminalForward。");
        return pause.Entered.Task.WaitAsync(cancellationToken);
    }

    /// <summary>解除 B→C 转发暂停。</summary>
    public void ResumeTerminalForward()
    {
        var pause = Interlocked.Exchange(ref _forwardingPause, null);
        pause?.Release.TrySetResult();
    }

    /// <summary>停止并释放 C，关闭现有节点 TCP 连接。</summary>
    public async Task StopNodeCAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_hosts.Remove(NodeC, out var host))
            {
                return;
            }

            await StopAndDisposeAsync(host, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>在原 loopback 端口重新启动 C。</summary>
    public async Task RestartNodeCAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_hosts.ContainsKey(NodeC))
            {
                throw new InvalidOperationException("节点 C 仍在运行。");
            }

            await StartNodeCoreAsync(NodeC, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>读取某观察节点当前的静态成员存活视图。</summary>
    public IReadOnlyList<string> GetLiveNodeIds(string observerNodeId)
        => GetRequiredService<IClusterMembership>(observerNodeId).LiveNodeIds;

    /// <summary>读取节点实际使用的生产节点传输。</summary>
    public INodeTransport GetNodeTransport(string nodeId)
        => GetRequiredService<INodeTransport>(nodeId);

    /// <summary>读取节点实际使用的租约存储，用于确认三节点共享同一测试 CAS + TTL 存储。</summary>
    public IActorLeaseStore GetLeaseStore(string nodeId)
        => GetRequiredService<IActorLeaseStore>(nodeId);

    private Task StartNodeAsync(string nodeId, CancellationToken cancellationToken)
        => StartNodeCoreAsync(nodeId, cancellationToken);

    private async Task StartNodeCoreAsync(string nodeId, CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(_options.MinimumLogLevel);
        });

        services.AddSingleton<IMessageDispatcher, ThreeHopMessageDispatcher>();
        services.AddSingleton<IPulseBackplane>(_backplane);
        services.AddSingleton<IActorLeaseStore>(_leaseStore);
        services.AddPulseServer(server =>
        {
            server.UsePreset(ServerPreset.LowLatency);
            server.AddTcp($"cluster-{nodeId}", _ports[nodeId]);
        });
        services.AddPulseClustering(
            topology =>
            {
                topology.LocalNodeId = nodeId;
                foreach (var endpoint in _ports)
                {
                    topology.Members.Add(new ClusterNodeEndpoint
                    {
                        NodeId = endpoint.Key,
                        Host = IPAddress.Loopback.ToString(),
                        Port = endpoint.Value,
                    });
                }
            },
            authentication => authentication.SharedSecret = SharedSecret,
            leases =>
            {
                leases.LeaseDuration = _options.LeaseDuration;
                leases.AllowInMemoryStoreForMultiNode = true;
            });
        if (string.Equals(nodeId, NodeA, StringComparison.Ordinal))
        {
            services.AddPulseGateway();
        }
        services.Configure<StaticClusterMembershipOptions>(membership =>
        {
            membership.FailureThreshold = 1;
            membership.QuarantineDuration = _options.RecoveryTtl;
        });
        services.Configure<ActorLeaseHeartbeatOptions>(heartbeat =>
            heartbeat.Interval = TimeSpan.FromTicks(Math.Max(1, _options.LeaseDuration.Ticks / 3)));
        services.Configure<TcpNodeTransportOptions>(transport =>
        {
            transport.SecurityMode = NodeTransportSecurityMode.InsecureDevelopment;
            transport.ConnectTimeout = _options.ConnectTimeout;
            transport.RequestTimeout = _options.RequestTimeout;
            transport.NoDelay = true;
        });

        services.RemoveAll<IServiceRoutingTable>();
        services.AddSingleton<IServiceRoutingTable>(
            new ThreeHopRoutingTable(nodeId, _probe, WaitForTerminalForwardAsync));
        services.RemoveAll<IResponseSerializerRegistry>();
        services.AddSingleton<IResponseSerializerRegistry>(ThreeHopResponseSerializerRegistry.Instance);

        var provider = services.BuildServiceProvider();
        try
        {
            var server = provider.GetRequiredService<PulseServer>();
            await server.StartAsync(cancellationToken).ConfigureAwait(false);
            _hosts.Add(nodeId, new NodeRuntime(provider, server));
        }
        catch
        {
            await provider.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask WaitForTerminalForwardAsync(CancellationToken cancellationToken)
    {
        var pause = Volatile.Read(ref _forwardingPause);
        if (pause is null)
        {
            return;
        }

        pause.Entered.TrySetResult();
        await pause.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private T GetRequiredService<T>(string nodeId) where T : notnull
    {
        ThrowIfDisposed();
        if (!_hosts.TryGetValue(nodeId, out var host))
        {
            throw new InvalidOperationException($"节点 '{nodeId}' 当前未运行。");
        }

        return host.Provider.GetRequiredService<T>();
    }

    private static Dictionary<string, int> AllocateLoopbackPorts(params string[] nodeIds)
    {
        var ports = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var nodeId in nodeIds)
        {
            int port;
            do
            {
                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            while (ports.ContainsValue(port));

            ports.Add(nodeId, port);
        }

        return ports;
    }

    private static string FindKeyOwnedBy(string nodeId)
    {
        var ring = new NodeConsistentHashRing(new[] { NodeA, NodeB, NodeC });
        for (var index = 0; index < 100_000; index++)
        {
            var key = $"entry-{index}";
            if (string.Equals(
                    ring.GetOwner(HashPlacementStrategy.BuildIdentity(EntryHub, key)),
                    nodeId,
                    StringComparison.Ordinal))
            {
                return key;
            }
        }

        throw new InvalidOperationException($"无法为三跳拓扑找到属主为 '{nodeId}' 的入口 key。");
    }

    private static async Task StopAndDisposeAsync(NodeRuntime host, CancellationToken cancellationToken)
    {
        try
        {
            await host.Server.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await host.Provider.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(ThreeNodeTcpTopology));
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        ResumeTerminalForward();
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var host in _hosts.Values.Reverse().ToArray())
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await StopAndDisposeAsync(host, timeout.Token).ConfigureAwait(false);
                }
                catch
                {
                    await host.Provider.DisposeAsync().ConfigureAwait(false);
                }
            }

            _hosts.Clear();
        }
        finally
        {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
        }
    }

    private sealed class ForwardingPause
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record NodeRuntime(ServiceProvider Provider, PulseServer Server);
}

/// <summary>三节点拓扑的测试/基准参数。</summary>
public sealed class ThreeNodeTcpTopologyOptions
{
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan RecoveryTtl { get; set; } = TimeSpan.FromMilliseconds(300);
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(2);
    public LogLevel MinimumLogLevel { get; set; } = LogLevel.Warning;
}

internal sealed class ThreeHopProbe
{
    private long _entryExecutionCount;
    private long _terminalExecutionCount;

    public long EntryExecutionCount => Interlocked.Read(ref _entryExecutionCount);
    public long TerminalExecutionCount => Interlocked.Read(ref _terminalExecutionCount);

    public void RecordEntryExecution() => Interlocked.Increment(ref _entryExecutionCount);
    public void RecordTerminalExecution() => Interlocked.Increment(ref _terminalExecutionCount);
}

internal sealed class ThreeHopRoutingTable : IServiceRoutingTable
{
    private static readonly ushort[] ProtocolIds =
    {
        NodeWireProtocol.AuthenticateProtocolId,
        NodeWireProtocol.NegotiateProtocolId,
        NodeWireProtocol.AskActorProtocolId,
        NodeWireProtocol.AskActorV2ProtocolId,
        NodeWireProtocol.SendActorProtocolId,
        NodeWireProtocol.SendActorV2ProtocolId,
        ThreeNodeTcpTopology.EntryProtocolId,
        ThreeNodeTcpTopology.TerminalProtocolId,
    };

    private readonly string _nodeId;
    private readonly ThreeHopProbe _probe;
    private readonly Func<CancellationToken, ValueTask> _beforeTerminalForward;

    public ThreeHopRoutingTable(
        string nodeId,
        ThreeHopProbe probe,
        Func<CancellationToken, ValueTask> beforeTerminalForward)
    {
        _nodeId = nodeId;
        _probe = probe;
        _beforeTerminalForward = beforeTerminalForward;
    }

    public bool IsProtocolIdValid(string hub, ushort protocolId)
        => (string.Equals(hub, "ClusterInternalHub", StringComparison.Ordinal)
            && protocolId is NodeWireProtocol.AuthenticateProtocolId
                or NodeWireProtocol.NegotiateProtocolId
                or NodeWireProtocol.AskActorProtocolId
                or NodeWireProtocol.AskActorV2ProtocolId
                or NodeWireProtocol.SendActorProtocolId
                or NodeWireProtocol.SendActorV2ProtocolId)
           || (string.Equals(hub, ThreeNodeTcpTopology.EntryHub, StringComparison.Ordinal)
            && protocolId == ThreeNodeTcpTopology.EntryProtocolId)
           || (string.Equals(hub, ThreeNodeTcpTopology.TerminalHub, StringComparison.Ordinal)
               && protocolId == ThreeNodeTcpTopology.TerminalProtocolId);

    public ReadOnlySpan<ushort> EnumerateProtocolIds() => ProtocolIds;

    public ValueTask<object?> RouteByProtocolIdAsync(
        IServiceProvider serviceProvider,
        ushort protocolId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
        => RouteCoreAsync(serviceProvider, protocolId, data, cancellationToken);

    public ValueTask<object?> RouteByProtocolIdAsync(
        IServiceProvider serviceProvider,
        string hub,
        ushort protocolId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        EnsureProtocol(hub, protocolId);
        return RouteCoreAsync(serviceProvider, protocolId, data, cancellationToken);
    }

    public ValueTask<object?> RouteByProtocolIdAsync(
        IServiceProvider serviceProvider,
        ushort protocolId,
        string serviceKey,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
        => RouteCoreAsync(serviceProvider, protocolId, data, cancellationToken);

    public ValueTask<object?> RouteByProtocolIdAsync(
        IServiceProvider serviceProvider,
        string hub,
        ushort protocolId,
        string serviceKey,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        EnsureProtocol(hub, protocolId);
        return RouteCoreAsync(serviceProvider, protocolId, data, cancellationToken);
    }

    private ValueTask<object?> RouteCoreAsync(
        IServiceProvider serviceProvider,
        ushort protocolId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
        => protocolId switch
        {
            NodeWireProtocol.AuthenticateProtocolId => RouteAuthenticateAsync(serviceProvider, data),
            NodeWireProtocol.NegotiateProtocolId => RouteNegotiateAsync(serviceProvider, data),
            NodeWireProtocol.AskActorProtocolId => RouteAskActorAsync(serviceProvider, data),
            NodeWireProtocol.AskActorV2ProtocolId => RouteAskActorV2Async(serviceProvider, data),
            NodeWireProtocol.SendActorProtocolId => RouteSendActorAsync(serviceProvider, data),
            NodeWireProtocol.SendActorV2ProtocolId => RouteSendActorV2Async(serviceProvider, data),
            ThreeNodeTcpTopology.EntryProtocolId => RouteEntryAsync(serviceProvider, data, cancellationToken),
            ThreeNodeTcpTopology.TerminalProtocolId => RouteTerminalAsync(data),
            _ => ValueTask.FromException<object?>(
                new InvalidOperationException($"未知三跳协议号 0x{protocolId:X4}。")),
        };

    private static async ValueTask<object?> RouteAuthenticateAsync(
        IServiceProvider serviceProvider,
        ReadOnlyMemory<byte> data)
    {
        var request = MemoryPackSerializer.Deserialize<(string NodeId, byte[] Credential)>(data.Span);
        return await serviceProvider.GetRequiredService<IClusterInternalHub>()
            .AuthenticateAsync(request.NodeId, request.Credential).ConfigureAwait(false);
    }

    private static async ValueTask<object?> RouteNegotiateAsync(
        IServiceProvider serviceProvider,
        ReadOnlyMemory<byte> data)
    {
        var request = MemoryPackSerializer.Deserialize<byte[]>(data.Span) ?? Array.Empty<byte>();
        return await serviceProvider.GetRequiredService<IClusterInternalHub>()
            .NegotiateAsync(request).ConfigureAwait(false);
    }

    private static async ValueTask<object?> RouteAskActorAsync(
        IServiceProvider serviceProvider,
        ReadOnlyMemory<byte> data)
    {
        var request = MemoryPackSerializer.Deserialize<(
            string Hub,
            string Key,
            ushort ProtocolId,
            byte[] Body,
            string SourceNodeId,
            string ReplyTo)>(data.Span);
        return await serviceProvider.GetRequiredService<IClusterInternalHub>()
            .AskActorAsync(
                request.Hub,
                request.Key,
                request.ProtocolId,
                request.Body,
                request.SourceNodeId,
                request.ReplyTo).ConfigureAwait(false);
    }

    private static async ValueTask<object?> RouteAskActorV2Async(
        IServiceProvider serviceProvider,
        ReadOnlyMemory<byte> data)
    {
        var request = MemoryPackSerializer.Deserialize<byte[]>(data.Span) ?? Array.Empty<byte>();
        return await serviceProvider.GetRequiredService<IClusterInternalHub>()
            .AskActorV2Async(request).ConfigureAwait(false);
    }

    private static async ValueTask<object?> RouteSendActorAsync(
        IServiceProvider serviceProvider,
        ReadOnlyMemory<byte> data)
    {
        var request = MemoryPackSerializer.Deserialize<(
            string Hub,
            string Key,
            ushort ProtocolId,
            byte[] Body,
            string SourceNodeId,
            string ReplyTo,
            Guid MessageId)>(data.Span);
        await serviceProvider.GetRequiredService<IClusterInternalHub>()
            .SendActorAsync(
                request.Hub,
                request.Key,
                request.ProtocolId,
                request.Body,
                request.SourceNodeId,
                request.ReplyTo,
                request.MessageId).ConfigureAwait(false);
        return null;
    }

    private static async ValueTask<object?> RouteSendActorV2Async(
        IServiceProvider serviceProvider,
        ReadOnlyMemory<byte> data)
    {
        var request = MemoryPackSerializer.Deserialize<byte[]>(data.Span) ?? Array.Empty<byte>();
        await serviceProvider.GetRequiredService<IClusterInternalHub>()
            .SendActorV2Async(request).ConfigureAwait(false);
        return null;
    }

    private async ValueTask<object?> RouteEntryAsync(
        IServiceProvider serviceProvider,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(_nodeId, ThreeNodeTcpTopology.NodeB, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("三跳入口只能在节点 B 执行。");
        }

        _probe.RecordEntryExecution();
        await _beforeTerminalForward(cancellationToken).ConfigureAwait(false);

        var router = serviceProvider.GetRequiredService<IPulseRouter>();
        var response = await router.AskAsync(
            PulseAddress.Actor(
                ThreeNodeTcpTopology.TerminalHub,
                "terminal",
                ThreeNodeTcpTopology.NodeC),
            ThreeNodeTcpTopology.TerminalProtocolId,
            data,
            cancellationToken).ConfigureAwait(false);
        return MemoryPackSerializer.Deserialize<string>(response.Span) ?? string.Empty;
    }

    private ValueTask<object?> RouteTerminalAsync(ReadOnlyMemory<byte> data)
    {
        if (!string.Equals(_nodeId, ThreeNodeTcpTopology.NodeC, StringComparison.Ordinal))
        {
            return ValueTask.FromException<object?>(
                new InvalidOperationException("三跳终点只能在节点 C 执行。"));
        }

        var caller = PulseContext.Current
            ?? throw new UnauthorizedAccessException("三跳终点缺少外部调用者上下文。");
        if (caller.SourceType != CallSourceType.ExternalUser ||
            !string.Equals(caller.UserId, "three-hop-user", StringComparison.Ordinal) ||
            caller.Token is not null ||
            !caller.HasPermission("cluster.invoke") ||
            !caller.HasRole("benchmark-user") ||
            caller.User?.Claims.Any(claim =>
                claim.Type == "tenant" &&
                claim.Value == "cluster-e2e" &&
                claim.Issuer == "three-hop-issuer") != true)
        {
            throw new UnauthorizedAccessException("三跳终点收到的 claims/角色/权限快照不完整或携带了 bearer token。");
        }

        _probe.RecordTerminalExecution();
        var payload = MemoryPackSerializer.Deserialize<string>(data.Span) ?? string.Empty;
        return new ValueTask<object?>(
            $"{payload}|{ThreeNodeTcpTopology.NodeA}>{ThreeNodeTcpTopology.NodeB}>{ThreeNodeTcpTopology.NodeC}");
    }

    private void EnsureProtocol(string hub, ushort protocolId)
    {
        if (!IsProtocolIdValid(hub, protocolId))
        {
            throw new InvalidOperationException(
                $"协议号 0x{protocolId:X4} 不属于 canonical Hub '{hub}'。 ");
        }
    }
}

internal sealed class ThreeHopResponseSerializerRegistry : IResponseSerializerRegistry
{
    public static readonly ThreeHopResponseSerializerRegistry Instance = new();

    private readonly IResponseSerializer[] _serializers =
    {
        new MemoryPackResponseSerializer<bool>(NodeWireProtocol.AuthenticateProtocolId),
        new MemoryPackResponseSerializer<byte[]>(NodeWireProtocol.NegotiateProtocolId),
        new MemoryPackResponseSerializer<byte[]>(NodeWireProtocol.AskActorProtocolId),
        new MemoryPackResponseSerializer<byte[]>(NodeWireProtocol.AskActorV2ProtocolId),
        new MemoryPackResponseSerializer<byte[]>(NodeWireProtocol.SendActorProtocolId),
        new MemoryPackResponseSerializer<byte[]>(NodeWireProtocol.SendActorV2ProtocolId),
        new MemoryPackResponseSerializer<string>(ThreeNodeTcpTopology.EntryProtocolId),
        new MemoryPackResponseSerializer<string>(ThreeNodeTcpTopology.TerminalProtocolId),
    };

    private ThreeHopResponseSerializerRegistry()
    {
    }

    public bool TryGetSerializer(
        ushort protocolId,
        [NotNullWhen(true)] out IResponseSerializer? serializer)
    {
        serializer = _serializers.FirstOrDefault(candidate => candidate.ProtocolId == protocolId);
        return serializer is not null;
    }

    public ReadOnlySpan<IResponseSerializer> EnumerateSerializers() => _serializers;

    private sealed class MemoryPackResponseSerializer<TValue> : IResponseSerializer
    {
        public MemoryPackResponseSerializer(ushort protocolId)
        {
            ProtocolId = protocolId;
        }

        public ushort ProtocolId { get; }

        public void Serialize(object response, IBufferWriter<byte> writer)
            => MemoryPackSerializer.Serialize(writer, (TValue)response);

        public ValueTask SerializeAsync(
            object response,
            IBufferWriter<byte> writer,
            CancellationToken cancellationToken = default)
        {
            Serialize(response, writer);
            return ValueTask.CompletedTask;
        }

        public bool TryGetTypedSerializer<T>(out Action<T, IBufferWriter<byte>> serializer)
        {
            serializer = null!;
            return false;
        }
    }
}

internal sealed class ThreeHopMessageDispatcher : IMessageDispatcher
{
    private readonly IServiceRoutingTable _routingTable;
    private int _running;

    public ThreeHopMessageDispatcher(IServiceRoutingTable routingTable)
    {
        _routingTable = routingTable;
    }

    public event EventHandler<MessageProcessedEventArgs>? MessageProcessed
    {
        add { }
        remove { }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref _running, 1);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Volatile.Write(ref _running, 0);
        return Task.CompletedTask;
    }

    public ValueTask<object?> DispatchAsync(
        MessageEnvelope message,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _running) == 0)
        {
            return ValueTask.FromException<object?>(new InvalidOperationException("三跳消息调度器尚未启动。"));
        }

        var header = message.Header ?? throw new ArgumentException("消息头不能为空。", nameof(message));
        return _routingTable.RouteByProtocolIdAsync(
            serviceProvider,
            header.ServiceName ?? string.Empty,
            header.ProtocolId,
            header.ServiceKey ?? string.Empty,
            message.Payload,
            cancellationToken);
    }

    public void Dispose()
    {
        Volatile.Write(ref _running, 0);
    }
}
