namespace PulseRPC.Shared;

/// <summary>
/// PulseRPC 传输层协议常量
/// </summary>
public static class ProtocolConstants
{
    /// <summary>
    /// 协议魔数 - 'PR' (PulseRPC)
    /// 用于快速识别和拒绝非协议数据
    /// </summary>
    public const ushort ProtocolMagic = 0x5052; // ASCII: 'PR'

    /// <summary>
    /// 当前协议版本
    /// </summary>
    public const byte CurrentProtocolVersion = 2;

    /// <summary>
    /// 支持的最小协议版本
    /// </summary>
    public const byte MinSupportedProtocolVersion = 2;

    /// <summary>
    /// 当前 <see cref="PulseRPC.Messaging.MessageHeader"/> 对象线格式版本。
    /// v2 在首字段显式携带此值，且不读取 v1 object layout。
    /// </summary>
    public const byte MessageHeaderWireVersion = 2;

    /// <summary>
    /// 握手超时时间（毫秒）
    /// </summary>
    public const int HandshakeTimeoutMs = 5000;

    /// <summary>
    /// 握手消息的特殊 MessageId
    /// </summary>
    public const ushort HandshakeMessageId = 0xFFFF;

    /// <summary>
    /// 握手请求标志
    /// </summary>
    public const ushort HandshakeRequestFlag = 0x8000;

    /// <summary>
    /// 握手响应标志
    /// </summary>
    public const ushort HandshakeResponseFlag = 0x8001;
}

/// <summary>
/// 握手消息
/// </summary>
public readonly struct HandshakeMessage
{
    /// <summary>
    /// 协议版本
    /// </summary>
    public readonly byte ProtocolVersion;

    /// <summary>
    /// 客户端/服务端名称
    /// </summary>
    public readonly string ClientName;

    /// <summary>
    /// 扩展信息（JSON格式）
    /// </summary>
    public readonly string Extensions;

    public HandshakeMessage(byte protocolVersion, string clientName, string? extensions = null)
    {
        ProtocolVersion = protocolVersion;
        ClientName = clientName ?? "Unknown";
        Extensions = extensions ?? "{}";
    }

    /// <summary>
    /// 序列化为字节数组
    /// </summary>
    public byte[] ToBytes()
    {
        var clientNameBytes = System.Text.Encoding.UTF8.GetBytes(ClientName);
        var extensionsBytes = System.Text.Encoding.UTF8.GetBytes(Extensions);

        var buffer = new byte[1 + 2 + clientNameBytes.Length + 2 + extensionsBytes.Length];
        var offset = 0;

        // Protocol version
        buffer[offset++] = ProtocolVersion;

        // Client name length + data
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
            buffer.AsSpan(offset, 2), (ushort)clientNameBytes.Length);
        offset += 2;
        clientNameBytes.CopyTo(buffer, offset);
        offset += clientNameBytes.Length;

        // Extensions length + data
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
            buffer.AsSpan(offset, 2), (ushort)extensionsBytes.Length);
        offset += 2;
        extensionsBytes.CopyTo(buffer, offset);

        return buffer;
    }

    /// <summary>
    /// 从字节数组反序列化
    /// </summary>
    public static HandshakeMessage FromBytes(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 5) // 最小长度：1 + 2 + 0 + 2 + 0
            throw new ArgumentException("Invalid handshake message: buffer too small");

        var offset = 0;

        // Protocol version
        var protocolVersion = buffer[offset++];

        // Client name
        var clientNameLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(
            buffer.Slice(offset, 2));
        offset += 2;

        if (buffer.Length < offset + clientNameLength + 2)
            throw new ArgumentException("Invalid handshake message: incomplete client name");

        var clientName = System.Text.Encoding.UTF8.GetString(buffer.Slice(offset, clientNameLength));
        offset += clientNameLength;

        // Extensions
        var extensionsLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(
            buffer.Slice(offset, 2));
        offset += 2;

        if (buffer.Length < offset + extensionsLength)
            throw new ArgumentException("Invalid handshake message: incomplete extensions");

        var extensions = System.Text.Encoding.UTF8.GetString(buffer.Slice(offset, extensionsLength));

        return new HandshakeMessage(protocolVersion, clientName, extensions);
    }
}

/// <summary>
/// 握手响应
/// </summary>
public readonly struct HandshakeResponse
{
    /// <summary>
    /// 是否接受握手
    /// </summary>
    public readonly bool Accepted;

    /// <summary>
    /// 服务端协议版本
    /// </summary>
    public readonly byte ServerProtocolVersion;

    /// <summary>
    /// 拒绝原因（如果 Accepted = false）
    /// </summary>
    public readonly string Reason;

    public HandshakeResponse(bool accepted, byte serverProtocolVersion, string? reason = null)
    {
        Accepted = accepted;
        ServerProtocolVersion = serverProtocolVersion;
        Reason = reason ?? string.Empty;
    }

    /// <summary>
    /// 序列化为字节数组
    /// </summary>
    public byte[] ToBytes()
    {
        var reasonBytes = System.Text.Encoding.UTF8.GetBytes(Reason);
        var buffer = new byte[1 + 1 + 2 + reasonBytes.Length];
        var offset = 0;

        // Accepted
        buffer[offset++] = (byte)(Accepted ? 1 : 0);

        // Server protocol version
        buffer[offset++] = ServerProtocolVersion;

        // Reason length + data
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
            buffer.AsSpan(offset, 2), (ushort)reasonBytes.Length);
        offset += 2;
        reasonBytes.CopyTo(buffer, offset);

        return buffer;
    }

    /// <summary>
    /// 从字节数组反序列化
    /// </summary>
    public static HandshakeResponse FromBytes(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 4) // 最小长度：1 + 1 + 2 + 0
            throw new ArgumentException("Invalid handshake response: buffer too small");

        var offset = 0;

        // Accepted
        var accepted = buffer[offset++] == 1;

        // Server protocol version
        var serverProtocolVersion = buffer[offset++];

        // Reason
        var reasonLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(
            buffer.Slice(offset, 2));
        offset += 2;

        if (buffer.Length < offset + reasonLength)
            throw new ArgumentException("Invalid handshake response: incomplete reason");

        var reason = System.Text.Encoding.UTF8.GetString(buffer.Slice(offset, reasonLength));

        return new HandshakeResponse(accepted, serverProtocolVersion, reason);
    }
}
