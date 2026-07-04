using System;
using System.Buffers;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC.Client;

namespace PulseRPC.Server.Gateway;

/// <summary>
/// <see cref="IGatewayRelayHub"/> 的手写客户端调用桩（<c>PulseNodeLink</c> 出站连接使用）。
/// </summary>
/// <remarks>
/// 与 <see cref="PulseRPC.Server.Clustering.ClusterInternalHubClientStub"/> 完全同构（同样的
/// 手写原因见 <see cref="IGatewayRelayHub"/> 备注）：零拷贝租借缓冲区 → MemoryPack 元组序列化 →
/// <see cref="IClientChannel.InvokeRawAsync"/>/<see cref="IClientChannel.SendCommandAsync"/> → 反序列化响应。
/// </remarks>
public sealed class GatewayRelayHubClientStub : IGatewayRelayHub
{
    private const ushort ProtocolId_PushRawFrameAsync = 0x9E31;
    private const ushort ProtocolId_AskConnectionAsync = 0x9E32;

    private readonly IClientChannel _connection;

    /// <summary>创建客户端调用桩。</summary>
    public GatewayRelayHubClientStub(IClientChannel connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    /// <inheritdoc/>
    public async Task PushRawFrameAsync(string connectionId, byte[] framedPacket)
    {
        var buffer = _connection.RentSerializationBuffer(256);
        try
        {
            var command = (connectionId, framedPacket);
            MemoryPackSerializer.Serialize(buffer, command);

            var serializedCommand = buffer is ArrayBufferWriter<byte> writer
                ? writer.WrittenMemory
                : ReadOnlyMemory<byte>.Empty;

            await _connection.SendCommandAsync(
                protocolId: ProtocolId_PushRawFrameAsync,
                serializedCommand: serializedCommand,
                cancellationToken: default).ConfigureAwait(false);
        }
        finally
        {
            _connection.ReturnSerializationBuffer(buffer);
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]> AskConnectionAsync(string connectionId, ushort protocolId, byte[] payload, int timeoutMs)
    {
        var buffer = _connection.RentSerializationBuffer(256);
        try
        {
            var request = (connectionId, protocolId, payload, timeoutMs);
            MemoryPackSerializer.Serialize(buffer, request);

            var serializedRequest = buffer is ArrayBufferWriter<byte> writer
                ? writer.WrittenMemory
                : ReadOnlyMemory<byte>.Empty;

            var responseBytes = await _connection.InvokeRawAsync(
                protocolId: ProtocolId_AskConnectionAsync,
                serializedRequest: serializedRequest,
                cancellationToken: default).ConfigureAwait(false);

            return MemoryPackSerializer.Deserialize<byte[]>(responseBytes.Span) ?? Array.Empty<byte>();
        }
        finally
        {
            _connection.ReturnSerializationBuffer(buffer);
        }
    }
}
