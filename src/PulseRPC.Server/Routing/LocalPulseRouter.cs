using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Server.Processing.Pipeline;
using PulseRPC.Server.Services.Management;
using PulseRPC.Server.Transport;
using PulseRPC;
using PulseRPC.Routing;

namespace PulseRPC.Server.Routing;

/// <summary>
/// 单节点默认 <see cref="IPulseRouter"/> 实现 —— 把 <see cref="PulseAddress"/> 直接解析为本地投递。
/// </summary>
/// <remarks>
/// <para>
/// 对应《统一 IPulseHub 全链路寻址与集群架构设计》§P3：Connection/AllClients/Group/User/Except
/// 经 <see cref="IServerChannelManager"/> + <see cref="IGroupManager"/> + <see cref="IUserConnectionMapping"/>
/// 解析为本地连接集合直投；Actor 经 <see cref="PulseServiceManager"/> 解析/激活 keyed 实例后，
/// 通过与入站消息分发同一条 <see cref="IServiceRoutingTable"/> 路径在本地邮箱中执行（不经过网络序列化往返）。
/// </para>
/// <para>
/// 单节点部署下忽略 <see cref="PulseAddress.NodeId"/>；跨节点转发（<see cref="AddressKind.Node"/>）
/// 属于集群能力（见路线图 P4 <c>INodeLink</c> / P5 Gateway），本实现遇到时抛出 <see cref="NotSupportedException"/>。
/// </para>
/// </remarks>
public sealed class LocalPulseRouter : IPulseRouter
{
    private readonly IServerChannelManager _channelManager;
    private readonly IGroupManager _groupManager;
    private readonly IUserConnectionMapping _userMapping;
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceRoutingTable? _routingTable;
    private readonly IResponseSerializerRegistry? _responseSerializerRegistry;
    private readonly ILogger<LocalPulseRouter> _logger;
    private readonly MessageDeduplicationCache _deduplicationCache;
    private readonly DeliveryRetryOptions _retryOptions;

    public LocalPulseRouter(
        IServerChannelManager channelManager,
        IGroupManager groupManager,
        IUserConnectionMapping userMapping,
        IServiceProvider serviceProvider,
        ILogger<LocalPulseRouter> logger,
        IServiceRoutingTable? routingTable = null,
        IResponseSerializerRegistry? responseSerializerRegistry = null,
        MessageDeduplicationCache? deduplicationCache = null,
        DeliveryRetryOptions? retryOptions = null)
    {
        _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
        _groupManager = groupManager ?? throw new ArgumentNullException(nameof(groupManager));
        _userMapping = userMapping ?? throw new ArgumentNullException(nameof(userMapping));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _routingTable = routingTable;
        _responseSerializerRegistry = responseSerializerRegistry;
        _deduplicationCache = deduplicationCache ?? new MessageDeduplicationCache();
        _retryOptions = retryOptions ?? new DeliveryRetryOptions();
    }

    /// <inheritdoc/>
    public ValueTask SendAsync(
        in PulseAddress address,
        ushort protocolId,
        ReadOnlyMemory<byte> body,
        DeliveryMode delivery = DeliveryMode.AtMostOnce,
        CancellationToken cancellationToken = default,
        Guid messageId = default)
        // 异步方法不能使用 in/ref/out 参数：先按值捕获地址（只读结构体，值语义安全），再委派给异步实现。
        => SendCoreAsync(address, protocolId, body, delivery, cancellationToken, messageId);

    private async ValueTask SendCoreAsync(
        PulseAddress address,
        ushort protocolId,
        ReadOnlyMemory<byte> body,
        DeliveryMode delivery,
        CancellationToken cancellationToken,
        Guid messageId)
    {
        if (address.Kind == AddressKind.Actor)
        {
            await SendToLocalActorAsync(address, protocolId, body, delivery, cancellationToken, messageId).ConfigureAwait(false);
            return;
        }

        var targets = ResolveTargets(address);
        if (targets.Count == 0)
        {
            return;
        }

        var estimatedSize = 4 + 128 + body.Length;
        var buffer = ArrayPool<byte>.Shared.Rent(estimatedSize);
        try
        {
            var header = new MessageHeader(MessageType.Event, string.Empty, string.Empty)
            {
                ProtocolId = protocolId,
            };
            var packet = new MessagePacket(header, body.Span);
            var bytesWritten = packet.WriteTo(buffer);
            var payload = buffer.AsMemory(0, bytesWritten);

            var tasks = new Task[targets.Count];
            for (var i = 0; i < targets.Count; i++)
            {
                tasks[i] = SendToTargetAsync(targets[i], payload, delivery, cancellationToken);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <inheritdoc/>
    public ValueTask<ReadOnlyMemory<byte>> AskAsync(
        in PulseAddress address,
        ushort protocolId,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default)
        // 异步方法不能使用 in/ref/out 参数：先按值捕获地址（只读结构体，值语义安全），再委派给异步实现。
        => AskCoreAsync(address, protocolId, body, cancellationToken);

    private async ValueTask<ReadOnlyMemory<byte>> AskCoreAsync(
        PulseAddress address,
        ushort protocolId,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        switch (address.Kind)
        {
            case AddressKind.Connection:
                var channel = _channelManager.GetChannel(address.Key)
                    ?? throw new InvalidOperationException($"AskAsync 目标连接不存在或已断开：ConnectionId='{address.Key}'");
                return await channel.InvokeClientAsync(protocolId, body, TimeSpan.Zero, cancellationToken).ConfigureAwait(false);

            case AddressKind.Actor:
                return await AskLocalActorAsync(address, protocolId, body, cancellationToken).ConfigureAwait(false);

            default:
                throw new NotSupportedException(
                    $"AskAsync 要求地址解析为单一目标；Kind={address.Kind} 不受支持" +
                    "（Group/User/AllClients/Except 属于 Fan-out 语义，请使用 SendAsync）。");
        }
    }

    private IReadOnlyList<IServerChannel> ResolveTargets(in PulseAddress address)
    {
        switch (address.Kind)
        {
            case AddressKind.Connection:
                var single = _channelManager.GetChannel(address.Key);
                return single is null ? Array.Empty<IServerChannel>() : new[] { single };

            case AddressKind.AllClients:
                return _channelManager.GetAuthenticatedChannels().ToArray();

            case AddressKind.Group:
                return ResolveByConnectionIds(_groupManager.GetGroupConnections(address.Key));

            case AddressKind.User:
                return ResolveByConnectionIds(_userMapping.GetConnections(address.Key));

            case AddressKind.Except:
                var excludedConnectionId = address.Key;
                return _channelManager.GetAuthenticatedChannels()
                    .Where(c => !string.Equals(c.Id, excludedConnectionId, StringComparison.Ordinal))
                    .ToArray();

            case AddressKind.Node:
                throw new NotSupportedException(
                    "单节点 LocalPulseRouter 不支持跨节点寻址（AddressKind.Node）；跨节点转发属于集群能力（见路线图 P4/P5）。");

            default:
                throw new NotSupportedException($"未知的 AddressKind：{address.Kind}");
        }
    }

    private IReadOnlyList<IServerChannel> ResolveByConnectionIds(IReadOnlyCollection<string> connectionIds)
    {
        if (connectionIds.Count == 0)
        {
            return Array.Empty<IServerChannel>();
        }

        var result = new List<IServerChannel>(connectionIds.Count);
        foreach (var connectionId in connectionIds)
        {
            var channel = _channelManager.GetChannel(connectionId);
            if (channel != null)
            {
                result.Add(channel);
            }
        }

        return result;
    }

    private async Task SendToTargetAsync(IServerChannel target, ReadOnlyMemory<byte> packet, DeliveryMode delivery, CancellationToken cancellationToken)
    {
        try
        {
            await DeliveryRetryExecutor.ExecuteAsync(
                delivery, _retryOptions,
                async ct =>
                {
                    // IServerChannel.SendAsync 用返回值（而非异常）表达"连接已断开/已释放"等失败：
                    // 显式转译为异常，使 AtLeastOnce/ExactlyOnce 的重试判定能统一按异常捕获。
                    var sent = await target.SendAsync(packet, ct).ConfigureAwait(false);
                    if (!sent)
                    {
                        throw new InvalidOperationException($"向连接 '{target.Id}' 发送失败（连接可能已断开或释放）。");
                    }
                },
                _logger, $"Fan-out 投递到连接 '{target.Id}'", cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // 忽略单个目标发送失败（连接可能已断开，或 AtLeastOnce/ExactlyOnce 重试耗尽），
            // 与生成的 Fan-out 代理保持一致的容错语义：不因单个目标失败拖垮整批广播。
        }
    }

    /// <summary>
    /// 单向路由到本地 keyed Actor：<see cref="DeliveryMode.ExactlyOnce"/> 时先按 <c>(Hub,Key,MessageId)</c>
    /// 去重（重复消息直接跳过，不执行）；否则（含去重放行后）经 <see cref="IServiceRoutingTable"/> 的
    /// 5 参数重载解析/激活实例并执行，丢弃返回值；<see cref="DeliveryMode.AtLeastOnce"/>/
    /// <see cref="DeliveryMode.ExactlyOnce"/> 失败时按 <see cref="DeliveryRetryExecutor"/> 重试。
    /// </summary>
    private async ValueTask SendToLocalActorAsync(
        PulseAddress address, ushort protocolId, ReadOnlyMemory<byte> body, DeliveryMode delivery, CancellationToken cancellationToken, Guid messageId)
    {
        var routingTable = RequireRoutingTable();

        if (delivery != DeliveryMode.ExactlyOnce)
        {
            await DeliveryRetryExecutor.ExecuteAsync(
                delivery, _retryOptions,
                async ct => await routingTable.RouteByProtocolIdAsync(_serviceProvider, protocolId, address.Key, body, ct).ConfigureAwait(false),
                _logger, $"Actor Send '{address.Hub}:{address.Key}'", cancellationToken).ConfigureAwait(false);
            return;
        }

        var scopeKey = BuildActorScopeKey(address);
        var effectiveMessageId = messageId == Guid.Empty ? Guid.NewGuid() : messageId;
        if (!_deduplicationCache.TryReserve(scopeKey, effectiveMessageId))
        {
            _logger.LogDebug(
                "Actor '{ScopeKey}' 收到重复消息 MessageId={MessageId}，按精确一次语义跳过执行。", scopeKey, effectiveMessageId);
            return;
        }

        try
        {
            await DeliveryRetryExecutor.ExecuteAsync(
                delivery, _retryOptions,
                async ct => await routingTable.RouteByProtocolIdAsync(_serviceProvider, protocolId, address.Key, body, ct).ConfigureAwait(false),
                _logger, $"Actor Send '{scopeKey}' (MessageId={effectiveMessageId})", cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // 重试耗尽仍失败：释放去重预占，使调用方之后携带同一 MessageId 的合法重试不会被误判为重复。
            _deduplicationCache.Release(scopeKey, effectiveMessageId);
            throw;
        }
    }

    private static string BuildActorScopeKey(in PulseAddress address) => $"{address.Hub}:{address.Key}";

    /// <summary>
    /// 请求/响应路由到本地 keyed Actor：复用入站分发同一条路由表路径获得已执行结果（装箱对象），
    /// 再经 <see cref="IResponseSerializerRegistry"/>（与常规 RPC 响应管道相同的序列化器）序列化为字节返回。
    /// </summary>
    private async ValueTask<ReadOnlyMemory<byte>> AskLocalActorAsync(PulseAddress address, ushort protocolId, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        var routingTable = RequireRoutingTable();
        var result = await routingTable.RouteByProtocolIdAsync(_serviceProvider, protocolId, address.Key, body, cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        var registry = _responseSerializerRegistry ?? ResponseSerializerRegistry.Instance;
        if (registry != null && registry.TryGetSerializer(protocolId, out var serializer))
        {
            var writer = new ArrayBufferWriter<byte>();
            serializer.Serialize(result, writer);
            return writer.WrittenMemory;
        }

        throw new InvalidOperationException(
            $"未找到协议号 0x{protocolId:X4} 对应的响应序列化器（IResponseSerializerRegistry），无法完成 Actor Ask 调用的结果序列化。");
    }

    private IServiceRoutingTable RequireRoutingTable()
    {
        return _routingTable
            ?? throw new InvalidOperationException(
                "IServiceRoutingTable 未注册，无法解析 Actor 地址。请确认已引用带 [Channel] Hub 接口的程序集" +
                "（由服务端源生成器在程序集加载时通过 ModuleInitializer 自动注册 ServiceRoutingTableRegistry.Instance）。");
    }
}
