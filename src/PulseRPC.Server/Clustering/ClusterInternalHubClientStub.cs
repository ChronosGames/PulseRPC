using System;
using System.Buffers;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC.Client;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// <see cref="IClusterInternalHub"/> 的手写客户端调用桩（<see cref="PulseNodeLink"/> 出站连接使用）。
/// </summary>
/// <remarks>
/// <para>
/// 与 <c>PulseRPC.Client.SourceGenerator</c> 为普通业务 Hub 生成的 <c>{Interface}Stub</c> 类型逐字段对应
/// （零拷贝租借缓冲区 → MemoryPack 元组序列化 → <see cref="IClientChannel.InvokeRawAsync"/> /
/// <see cref="IClientChannel.SendCommandAsync"/> → 反序列化响应），仅为避免在 <c>PulseRPC.Server</c>
/// 这个被广泛引用的库中启用客户端生成器（见 <see cref="IClusterInternalHub"/> 的备注）而手写。
/// 三个方法的协议号与接口上 <see cref="PulseRPC.Protocol.ProtocolAttribute"/> 的显式取值一致。
/// </para>
/// </remarks>
public sealed class ClusterInternalHubClientStub : IClusterInternalHub
{
    private const ushort ProtocolId_AuthenticateAsync = 0xD524;
    private const ushort ProtocolId_AskActorAsync = 0xFD7F;
    private const ushort ProtocolId_SendActorAsync = 0x33A0;

    private readonly IClientChannel _connection;

    /// <summary>创建客户端调用桩。</summary>
    public ClusterInternalHubClientStub(IClientChannel connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    /// <inheritdoc/>
    public async Task<bool> AuthenticateAsync(string nodeId, byte[] credential)
    {
        var buffer = _connection.RentSerializationBuffer(256);
        try
        {
            var request = (nodeId, credential);
            MemoryPackSerializer.Serialize(buffer, request);

            var serializedRequest = buffer is ArrayBufferWriter<byte> writer
                ? writer.WrittenMemory
                : ReadOnlyMemory<byte>.Empty;

            var responseBytes = await _connection.InvokeRawAsync(
                protocolId: ProtocolId_AuthenticateAsync,
                serializedRequest: serializedRequest,
                cancellationToken: default).ConfigureAwait(false);

            return MemoryPackSerializer.Deserialize<bool>(responseBytes.Span);
        }
        finally
        {
            _connection.ReturnSerializationBuffer(buffer);
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]> AskActorAsync(string hub, string key, ushort protocolId, byte[] body, string sourceNodeId = "", string replyTo = "")
    {
        var buffer = _connection.RentSerializationBuffer(256);
        try
        {
            var request = (hub, key, protocolId, body, sourceNodeId, replyTo);
            MemoryPackSerializer.Serialize(buffer, request);

            var serializedRequest = buffer is ArrayBufferWriter<byte> writer
                ? writer.WrittenMemory
                : ReadOnlyMemory<byte>.Empty;

            var responseBytes = await _connection.InvokeRawAsync(
                protocolId: ProtocolId_AskActorAsync,
                serializedRequest: serializedRequest,
                cancellationToken: default).ConfigureAwait(false);

            return MemoryPackSerializer.Deserialize<byte[]>(responseBytes.Span) ?? Array.Empty<byte>();
        }
        finally
        {
            _connection.ReturnSerializationBuffer(buffer);
        }
    }

    /// <inheritdoc/>
    public async Task SendActorAsync(string hub, string key, ushort protocolId, byte[] body, string sourceNodeId = "", string replyTo = "", Guid messageId = default)
    {
        var buffer = _connection.RentSerializationBuffer(256);
        try
        {
            var command = (hub, key, protocolId, body, sourceNodeId, replyTo, messageId);
            MemoryPackSerializer.Serialize(buffer, command);

            var serializedCommand = buffer is ArrayBufferWriter<byte> writer
                ? writer.WrittenMemory
                : ReadOnlyMemory<byte>.Empty;

            await _connection.SendCommandAsync(
                protocolId: ProtocolId_SendActorAsync,
                serializedCommand: serializedCommand,
                cancellationToken: default).ConfigureAwait(false);
        }
        finally
        {
            _connection.ReturnSerializationBuffer(buffer);
        }
    }
}
