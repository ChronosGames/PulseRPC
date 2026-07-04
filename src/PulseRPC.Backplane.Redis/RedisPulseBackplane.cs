using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Clustering;
using PulseRPC.Routing;
using StackExchange.Redis;

namespace PulseRPC.Backplane.Redis;

/// <summary>
/// <see cref="IPulseBackplane"/> 的 Redis 实现（设计文档 §9.2）：模型 X 经 Redis Pub/Sub 广播
/// Fan-out 意图，模型 Y 经 Redis Hash 维护 <c>(Kind,Key) → {connectionId: nodeId}</c> 全局成员目录。
/// </summary>
/// <remarks>
/// <para>
/// <strong>模型 X</strong>：所有节点发布/订阅同一个频道（<c>{KeyPrefix}:fanout</c>）；每条广播携带
/// <c>originNodeId</c>，订阅方（<c>ClusterPulseRouter</c>）据此过滤掉自己发布的广播以避免重复本地投递。
/// </para>
/// <para>
/// <strong>模型 Y</strong>：成员归属只按 <c>(Kind, Key)</c> 存储/查询，忽略 <c>Hub</c>
/// （与 <see cref="IPulseBackplane"/> 接口约定一致，因为组/用户成员关系本身与 Hub 无关）。
/// Hash 整体设置 <see cref="RedisBackplaneOptions.MemberEntryTimeToLive"/> TTL 作为断线未清理的兜底自愈。
/// </para>
/// <para>
/// 本实现不管理 <see cref="IConnectionMultiplexer"/> 的生命周期（由调用方通过 DI 提供并负责释放），
/// 但会持有一份自己的 Redis 订阅，需通过 <see cref="DisposeAsync"/> 释放。
/// </para>
/// </remarks>
public sealed class RedisPulseBackplane : IPulseBackplane, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _connection;
    private readonly RedisBackplaneOptions _options;
    private readonly ILogger<RedisPulseBackplane> _logger;
    private readonly RedisChannel _fanoutChannel;
    private readonly ConcurrentDictionary<Guid, BackplaneMessageHandler> _localHandlers = new();
    private int _redisSubscribed;
    private bool _disposed;

    /// <summary>创建 Redis Backplane。</summary>
    public RedisPulseBackplane(
        IConnectionMultiplexer connection,
        IOptions<RedisBackplaneOptions> options,
        ILogger<RedisPulseBackplane> logger)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? new RedisBackplaneOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fanoutChannel = RedisChannel.Literal($"{_options.KeyPrefix}:fanout");
    }

    /// <inheritdoc/>
    public async ValueTask PublishAsync(
        PulseAddress fanoutAddress, ushort protocolId, ReadOnlyMemory<byte> body, string originNodeId, CancellationToken cancellationToken = default)
    {
        var envelope = (
            kind: (byte)fanoutAddress.Kind,
            hub: fanoutAddress.Hub,
            key: fanoutAddress.Key,
            protocolId,
            originNodeId,
            body: body.ToArray());
        var payload = MemoryPackSerializer.Serialize(envelope);

        await _connection.GetSubscriber().PublishAsync(_fanoutChannel, payload).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public IDisposable Subscribe(BackplaneMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var id = Guid.NewGuid();
        _localHandlers[id] = handler;
        EnsureRedisSubscribed();
        return new LocalSubscription(this, id);
    }

    private void EnsureRedisSubscribed()
    {
        if (Interlocked.CompareExchange(ref _redisSubscribed, 1, 0) != 0)
        {
            return;
        }

        _connection.GetSubscriber().Subscribe(_fanoutChannel, OnRedisFanoutMessage);
    }

    private void OnRedisFanoutMessage(RedisChannel channel, RedisValue message)
    {
        if (!message.HasValue)
        {
            return;
        }

        (byte kind, string hub, string key, ushort protocolId, string originNodeId, byte[] body) envelope;
        try
        {
            envelope = MemoryPackSerializer.Deserialize<(byte, string, string, ushort, string, byte[])>((byte[])message!);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "反序列化 Redis Backplane 广播消息失败，已丢弃该条消息。");
            return;
        }

        var address = new PulseAddress((AddressKind)envelope.kind, envelope.hub, envelope.key);
        foreach (var handler in _localHandlers.Values)
        {
            // 逐个订阅者派发，单个处理器异常不应影响其它处理器（与 StackExchange.Redis 自身对回调异常
            // "捕获并丢弃"的既定行为一致，见其官方文档）；这里额外记录日志便于排查。
            _ = DispatchSafeAsync(handler, address, envelope.protocolId, envelope.body, envelope.originNodeId);
        }
    }

    private async Task DispatchSafeAsync(BackplaneMessageHandler handler, PulseAddress address, ushort protocolId, byte[] body, string originNodeId)
    {
        try
        {
            await handler(address, protocolId, body, originNodeId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "处理 Redis Backplane 广播消息的订阅回调抛出异常（Hub={Hub}, Key={Key}, OriginNodeId={OriginNodeId}）。",
                address.Hub, address.Key, originNodeId);
        }
    }

    /// <inheritdoc/>
    public async ValueTask AddMemberAsync(string connectionId, PulseAddress membership, string ownerNodeId, CancellationToken cancellationToken = default)
    {
        var redisKey = BuildMembersKey(membership);
        var db = _connection.GetDatabase();
        await db.HashSetAsync(redisKey, connectionId, ownerNodeId).ConfigureAwait(false);
        await db.KeyExpireAsync(redisKey, _options.MemberEntryTimeToLive).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask RemoveMemberAsync(string connectionId, PulseAddress membership, string ownerNodeId, CancellationToken cancellationToken = default)
    {
        var redisKey = BuildMembersKey(membership);
        await _connection.GetDatabase().HashDeleteAsync(redisKey, connectionId).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<BackplaneMember>> ResolveAsync(PulseAddress address, CancellationToken cancellationToken = default)
    {
        var redisKey = BuildMembersKey(address);
        var entries = await _connection.GetDatabase().HashGetAllAsync(redisKey).ConfigureAwait(false);
        if (entries.Length == 0)
        {
            return Array.Empty<BackplaneMember>();
        }

        return entries
            .Select(entry => new BackplaneMember(nodeId: (string)entry.Value!, connectionId: (string)entry.Name!))
            .ToArray();
    }

    /// <summary>
    /// 按 <c>(Kind, Key)</c> 构造模型 Y 成员目录的 Redis Hash 键，<strong>忽略 Hub</strong>
    /// （成员归属与 Hub 无关，见 <see cref="IPulseBackplane"/> 接口约定）。
    /// </summary>
    private RedisKey BuildMembersKey(in PulseAddress address) => $"{_options.KeyPrefix}:members:{(byte)address.Kind}:{address.Key}";

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return default;
        }

        _disposed = true;
        if (Volatile.Read(ref _redisSubscribed) != 0)
        {
            _connection.GetSubscriber().Unsubscribe(_fanoutChannel);
        }

        _localHandlers.Clear();
        return default;
    }

    private sealed class LocalSubscription : IDisposable
    {
        private readonly RedisPulseBackplane _owner;
        private readonly Guid _id;
        private int _disposed;

        public LocalSubscription(RedisPulseBackplane owner, Guid id)
        {
            _owner = owner;
            _id = id;
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                _owner._localHandlers.TryRemove(_id, out _);
            }
        }
    }
}
