using System.Buffers.Binary;
using System.Text;

namespace PulseRPC.Shared.Security;

internal static class KcpWireHandshake
{
    private const byte RequestType = 1;
    private const byte ResponseType = 2;
    private const int PrefixSize = sizeof(uint) + sizeof(byte) + sizeof(byte);

    public static byte[] CreateRequest(uint conversationId, string extensions)
    {
        var extensionBytes = Encoding.UTF8.GetBytes(extensions);
        var packet = new byte[PrefixSize + sizeof(ushort) + extensionBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), conversationId);
        packet[4] = ProtocolConstants.CurrentProtocolVersion;
        packet[5] = RequestType;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6, 2), checked((ushort)extensionBytes.Length));
        extensionBytes.CopyTo(packet, 8);
        return packet;
    }

    public static bool TryParseRequest(
        ReadOnlySpan<byte> packet,
        out uint conversationId,
        out string extensions)
    {
        conversationId = 0;
        extensions = string.Empty;
        if (packet.Length < PrefixSize + sizeof(ushort) ||
            packet[4] != ProtocolConstants.CurrentProtocolVersion ||
            packet[5] != RequestType)
            return false;

        var length = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(6, 2));
        if (packet.Length != 8 + length)
            return false;
        conversationId = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(0, 4));
        extensions = Encoding.UTF8.GetString(packet.Slice(8, length));
        return true;
    }

    public static byte[] CreateResponse(
        uint conversationId,
        bool accepted,
        string? reason,
        string? extensions)
    {
        var reasonBytes = Encoding.UTF8.GetBytes(reason ?? string.Empty);
        var extensionBytes = Encoding.UTF8.GetBytes(extensions ?? "{}");
        var packet = new byte[
            PrefixSize + 1 + sizeof(ushort) + reasonBytes.Length + sizeof(ushort) + extensionBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), conversationId);
        packet[4] = ProtocolConstants.CurrentProtocolVersion;
        packet[5] = ResponseType;
        packet[6] = accepted ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(7, 2), checked((ushort)reasonBytes.Length));
        reasonBytes.CopyTo(packet, 9);
        var offset = 9 + reasonBytes.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(offset, 2), checked((ushort)extensionBytes.Length));
        extensionBytes.CopyTo(packet, offset + 2);
        return packet;
    }

    public static bool TryParseResponse(
        ReadOnlySpan<byte> packet,
        out uint conversationId,
        out bool accepted,
        out string reason,
        out string extensions)
    {
        conversationId = 0;
        accepted = false;
        reason = string.Empty;
        extensions = string.Empty;
        if (packet.Length < PrefixSize + 1 + sizeof(ushort) + sizeof(ushort) ||
            packet[4] != ProtocolConstants.CurrentProtocolVersion ||
            packet[5] != ResponseType)
            return false;

        conversationId = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(0, 4));
        accepted = packet[6] == 1;
        var reasonLength = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(7, 2));
        var offset = 9;
        if (packet.Length < offset + reasonLength + 2)
            return false;
        reason = Encoding.UTF8.GetString(packet.Slice(offset, reasonLength));
        offset += reasonLength;
        var extensionLength = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(offset, 2));
        offset += 2;
        if (packet.Length != offset + extensionLength)
            return false;
        extensions = Encoding.UTF8.GetString(packet.Slice(offset, extensionLength));
        return true;
    }
}
