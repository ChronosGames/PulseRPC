using System.Buffers.Binary;
using FluentAssertions;
using MemoryPack;
using PulseRPC.Messaging;
using PulseRPC.Shared;
using Xunit;

namespace PulseRPC.Server.Tests.Processing;

public sealed class MessageHeaderWireVersionTests
{
    [Fact]
    public void BreakingV2Upgrade_MustRejectAllV1TransportHandshakes()
    {
        ProtocolConstants.CurrentProtocolVersion.Should().Be(2);
        ProtocolConstants.MinSupportedProtocolVersion.Should().Be(2);
        ProtocolConstants.MessageHeaderWireVersion.Should().Be(2);
    }

    [Fact]
    public void MessagePacket_MustRoundTripCurrentWireVersion()
    {
        var header = CreateHeader();
        var frame = WriteFrame(header, new byte[] { 1, 2, 3 });

        MessagePacket.TryReadFrom(frame, out var packet).Should().BeTrue();
        packet.Header.WireVersion.Should().Be(ProtocolConstants.MessageHeaderWireVersion);
        packet.Header.ServiceName.Should().Be("H");
        packet.Payload.ToArray().Should().Equal(1, 2, 3);
    }

    [Fact]
    public void MessagePacket_MustRejectExplicitUnsupportedWireVersion()
    {
        var header = CreateHeader();
        header.WireVersion = 1;

        MessagePacket.TryReadFrom(WriteSerializedFrame(header, []), out _).Should().BeFalse();
    }

    [Fact]
    public void MessagePacket_MustRejectLegacyV1ObjectLayout()
    {
        var legacy = new LegacyMessageHeaderV1
        {
            Type = MessageType.Request,
            MessageId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
            ServiceName = "H",
            MethodName = "M",
            ProtocolId = 0x1234,
            Flags = MessageFlags.RequireResponse,
            Timestamp = 1,
            SequenceNumber = 2,
            ServiceKey = "K",
            TimeoutMs = 3,
            SourceNodeId = "S",
            ReplyTo = "R",
            HopLimit = 4,
        };
        var headerBytes = MemoryPackSerializer.Serialize(legacy);
        var frame = new byte[4 + headerBytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, headerBytes.Length);
        headerBytes.CopyTo(frame.AsSpan(4));

        MessagePacket.TryReadFrom(frame, out _).Should().BeFalse();
    }

    [Fact]
    public void CurrentHeaderLayout_MustMatchV2GoldenBytes()
    {
        var bytes = MemoryPackSerializer.Serialize(CreateHeader());

        Convert.ToHexString(bytes).Should().Be(
            "0E020133221100554477668899AABBCCDDEEFFFEFFFFFF0100000048FEFFFFFF010000004D34120401000000000000000200FEFFFFFF010000004B03000000FEFFFFFF0100000053FEFFFFFF010000005204");
    }

    private static MessageHeader CreateHeader()
        => new(MessageType.Request, "H", "M")
        {
            MessageId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
            ProtocolId = 0x1234,
            Flags = MessageFlags.RequireResponse,
            Timestamp = 1,
            SequenceNumber = 2,
            ServiceKey = "K",
            TimeoutMs = 3,
            SourceNodeId = "S",
            ReplyTo = "R",
            HopLimit = 4,
        };

    private static byte[] WriteFrame(MessageHeader header, ReadOnlySpan<byte> payload)
    {
        var buffer = new byte[1024 + payload.Length];
        var written = new MessagePacket(header, payload).WriteTo(buffer);
        return buffer.AsSpan(0, written).ToArray();
    }

    private static byte[] WriteSerializedFrame(MessageHeader header, ReadOnlySpan<byte> payload)
    {
        var headerBytes = MemoryPackSerializer.Serialize(header);
        var frame = new byte[4 + headerBytes.Length + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, headerBytes.Length);
        headerBytes.CopyTo(frame.AsSpan(4));
        payload.CopyTo(frame.AsSpan(4 + headerBytes.Length));
        return frame;
    }
}

[MemoryPackable]
public partial class LegacyMessageHeaderV1
{
    [MemoryPackOrder(0)] public MessageType Type { get; set; }
    [MemoryPackOrder(1)] public Guid MessageId { get; set; }
    [MemoryPackOrder(2)] public string ServiceName { get; set; } = string.Empty;
    [MemoryPackOrder(3)] public string MethodName { get; set; } = string.Empty;
    [MemoryPackOrder(4)] public ushort ProtocolId { get; set; }
    [MemoryPackOrder(5)] public MessageFlags Flags { get; set; }
    [MemoryPackOrder(6)] public long Timestamp { get; set; }
    [MemoryPackOrder(7)] public ushort SequenceNumber { get; set; }
    [MemoryPackOrder(8)] public string ServiceKey { get; set; } = string.Empty;
    [MemoryPackOrder(9)] public int TimeoutMs { get; set; }
    [MemoryPackOrder(10)] public string SourceNodeId { get; set; } = string.Empty;
    [MemoryPackOrder(11)] public string ReplyTo { get; set; } = string.Empty;
    [MemoryPackOrder(12)] public byte HopLimit { get; set; }
}
