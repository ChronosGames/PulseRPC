using System.Buffers;
using MemoryPack;
using PulseRPC.Gateway;
using PulseRPC.Messaging;
using PulseRPC.Shared;

namespace PulseRPC.Client;

/// <summary>
/// Gateway Actor 客户端扩展。
/// </summary>
public static class GatewayClientChannelExtensions
{
    /// <summary>
    /// 创建一个经 Gateway 寻址到指定 Actor 的通道视图。
    /// </summary>
    /// <typeparam name="THub">Actor 实现的 Hub 契约。</typeparam>
    /// <param name="channel">连接到 Gateway 的真实客户端通道。</param>
    /// <param name="key">Actor 实例键。</param>
    /// <returns>可继续传给生成的 <c>GetHub&lt;THub&gt;()</c> 的通道视图。</returns>
    /// <remarks>
    /// 该视图不拥有底层连接；释放视图不会断开 <paramref name="channel"/>。
    /// </remarks>
    public static IClientChannel ForGatewayActor<THub>(this IClientChannel channel, string key)
        where THub : class, IPulseHub
    {
        if (channel is null)
        {
            throw new ArgumentNullException(nameof(channel));
        }
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Actor key cannot be null or whitespace.", nameof(key));
        }

        // 与服务端生成路由表使用同一 canonical Hub 名，确保 Gateway placement、
        // 服务端主动寻址和 keyed Actor 激活落到同一个 (Hub, Key)。
        return new GatewayActorChannel(channel, typeof(THub).Name.TrimStart('I'), key);
    }
}

/// <summary>
/// 把普通业务 Stub 的原始调用包装为 Gateway Front Hub 调用的轻量通道视图。
/// </summary>
internal sealed class GatewayActorChannel : IClientChannel
{
    private const byte DefaultHopLimit = 4;
    private const string GatewayFrontHub = "GatewayFrontHub";

    private readonly IClientChannel _inner;
    private readonly string _hub;
    private readonly string _key;

    public GatewayActorChannel(IClientChannel inner, string hub, string key)
    {
        _inner = inner;
        _hub = hub;
        _key = key;
    }

    public string Id => _inner.Id;
    public ConnectionDescriptor Descriptor => _inner.Descriptor;
    public ExtendedConnectionState State => _inner.State;
    public ConnectionStatistics Statistics => _inner.Statistics;
    public Dictionary<string, string> Tags => _inner.Tags;
    public bool IsConnected => _inner.IsConnected;

    public event EventHandler<TransportStateEventArgs>? ConnectionStateChanged
    {
        add => _inner.ConnectionStateChanged += value;
        remove => _inner.ConnectionStateChanged -= value;
    }

    public Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        => _inner.ConnectAsync(host, port, cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
        => _inner.DisconnectAsync(cancellationToken);

    public async ValueTask<ReadOnlyMemory<byte>> InvokeRawAsync(
        ushort protocolId,
        ReadOnlyMemory<byte> serializedRequest,
        CancellationToken cancellationToken = default)
    {
        var request = (_hub, _key, protocolId, serializedRequest.ToArray(), DefaultHopLimit);
        var envelope = MemoryPackSerializer.Serialize(request);
        var response = _inner is IHubAddressedClientChannel addressed
            ? await addressed.InvokeHubRawAsync(
                GatewayFrontHub,
                GatewayProtocolIds.FrontRelayAsk,
                envelope,
                cancellationToken).ConfigureAwait(false)
            : await _inner.InvokeRawAsync(
                GatewayProtocolIds.FrontRelayAsk,
                envelope,
                cancellationToken).ConfigureAwait(false);

        return MemoryPackSerializer.Deserialize<byte[]>(response.Span) ?? Array.Empty<byte>();
    }

    public ValueTask SendCommandAsync(
        ushort protocolId,
        ReadOnlyMemory<byte> serializedCommand,
        CancellationToken cancellationToken = default)
    {
        var command = (_hub, _key, protocolId, serializedCommand.ToArray(), DefaultHopLimit);
        var envelope = MemoryPackSerializer.Serialize(command);
        return _inner is IHubAddressedClientChannel addressed
            ? addressed.SendHubCommandAsync(
                GatewayFrontHub,
                GatewayProtocolIds.FrontRelaySend,
                envelope,
                cancellationToken)
            : _inner.SendCommandAsync(
                GatewayProtocolIds.FrontRelaySend,
                envelope,
                cancellationToken);
    }

    public ISubscriptionToken RegisterEventHandler(
        ushort protocolId,
        Action<ReadOnlyMemory<byte>> deserializeAndInvoke)
        => _inner.RegisterEventHandler(protocolId, deserializeAndInvoke);

    public ISubscriptionToken RegisterRequestHandler(
        ushort protocolId,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<ReadOnlyMemory<byte>>> handler)
        => _inner.RegisterRequestHandler(protocolId, handler);

    public IBufferWriter<byte> RentSerializationBuffer(int estimatedSize = 256)
        => _inner.RentSerializationBuffer(estimatedSize);

    public void ReturnSerializationBuffer(IBufferWriter<byte> buffer)
        => _inner.ReturnSerializationBuffer(buffer);

    public void Dispose()
    {
        // 这是不拥有底层连接的寻址视图。
    }
}
