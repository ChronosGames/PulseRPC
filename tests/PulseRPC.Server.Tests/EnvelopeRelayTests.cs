using System;
using FluentAssertions;
using PulseRPC.Messaging;
using Xunit;

namespace PulseRPC.Server.Tests;

/// <summary>
/// 验证 P-5「只读信封头地址中转」原语（<see cref="EnvelopeRelay"/> / <see cref="ReadOnlyEnvelopeHeader"/>）：
/// <list type="bullet">
/// <item>网关可仅凭信封头读出寻址三元组 Hub / Key / MethodId；</item>
/// <item>消息体（body）保持 opaque —— 即使是非法 MemoryPack 字节也能原样中转，不被反序列化；</item>
/// <item>可改写信封头（如实例键）后重新组帧，body 原样拼接、内容不变；</item>
/// <item>损坏 / 截断帧安全返回 false；缓冲区不足时 <see cref="EnvelopeRelay.TryWriteFrame"/> 返回 false 且不写入。</item>
/// </list>
/// </summary>
public class EnvelopeRelayTests
{
    private static byte[] BuildFrame(MessageHeader header, ReadOnlySpan<byte> body)
    {
        var packet = new MessagePacket(header, body);
        var buffer = new byte[packet.EstimateSize() + 256];
        var written = packet.WriteTo(buffer);
        return buffer.AsSpan(0, written).ToArray();
    }

    private static MessageHeader NewHeader(string hub, string key, ushort methodId, Guid messageId)
        => new(MessageType.Request, hub, "MoveAsync")
        {
            MessageId = messageId,
            ProtocolId = methodId,
            ServiceKey = key,
            Flags = MessageFlags.RequireResponse,
        };

    [Fact]
    public void TryReadHeader_Span_ReadsAddressingTuple_WithoutTouchingBody()
    {
        var messageId = Guid.NewGuid();
        var body = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x7F };
        var frame = BuildFrame(NewHeader("PlayerHub", "player-42", 0x1234, messageId), body);

        EnvelopeRelay.TryReadHeader(frame, out var header, out var readBody).Should().BeTrue();

        header.Hub.Should().Be("PlayerHub");
        header.Key.Should().Be("player-42");
        header.MethodId.Should().Be(0x1234);
        header.Type.Should().Be(MessageType.Request);
        header.MessageId.Should().Be(messageId);
        header.Flags.Should().Be(MessageFlags.RequireResponse);
        readBody.ToArray().Should().Equal(body);
    }

    [Fact]
    public void TryReadHeader_Memory_ReadsAddressingTuple_WithoutTouchingBody()
    {
        var messageId = Guid.NewGuid();
        var body = new byte[] { 1, 2, 3, 4, 5 };
        ReadOnlyMemory<byte> frame = BuildFrame(NewHeader("RoomHub", "room-1", 0x00AB, messageId), body);

        EnvelopeRelay.TryReadHeader(frame, out var header, out var readBody).Should().BeTrue();

        header.Hub.Should().Be("RoomHub");
        header.Key.Should().Be("room-1");
        header.MethodId.Should().Be(0x00AB);
        header.MessageId.Should().Be(messageId);
        readBody.ToArray().Should().Equal(body);
    }

    [Fact]
    public void TryReadHeader_TreatsBodyAsOpaque_EvenWhenNotValidMemoryPack()
    {
        // body 是一段任意/非法的字节序列；中转原语绝不应尝试反序列化它。
        var body = new byte[512];
        new Random(12345).NextBytes(body);
        var frame = BuildFrame(NewHeader("Hub", "k", 0xFFFF, Guid.NewGuid()), body);

        EnvelopeRelay.TryReadHeader(frame, out var header, out var readBody).Should().BeTrue();

        header.MethodId.Should().Be(0xFFFF);
        readBody.ToArray().Should().Equal(body);
    }

    [Fact]
    public void TryReadHeader_EmptyServiceKey_YieldsEmptyKey()
    {
        var frame = BuildFrame(NewHeader("Hub", string.Empty, 0x0007, Guid.NewGuid()), new byte[] { 9 });

        EnvelopeRelay.TryReadHeader(frame, out var header, out _).Should().BeTrue();

        header.Key.Should().BeEmpty();
    }

    [Fact]
    public void WriteFrame_RewritesKey_KeepsBodyByteForByte()
    {
        var body = new byte[] { 10, 20, 30, 40 };
        var frame = BuildFrame(NewHeader("PlayerHub", "player-42", 0x1234, Guid.NewGuid()), body);

        EnvelopeRelay.TryReadHeader(frame, out var header, out var readBody).Should().BeTrue();

        var rerouted = header.WithKey("player-99");
        var newFrame = EnvelopeRelay.WriteFrame(rerouted, readBody);

        EnvelopeRelay.TryReadHeader(newFrame, out var header2, out var body2).Should().BeTrue();
        header2.Key.Should().Be("player-99");
        header2.Hub.Should().Be("PlayerHub");
        header2.MethodId.Should().Be(0x1234);
        header2.MessageId.Should().Be(header.MessageId);
        body2.ToArray().Should().Equal(body);
    }

    [Fact]
    public void TryWriteFrame_WritesExactlyGetFrameSizeBytes()
    {
        var body = new byte[] { 7, 7, 7 };
        var header = new ReadOnlyEnvelopeHeader(MessageType.OneWay, Guid.NewGuid(), "Hub", "key", 0x0042, MessageFlags.None);

        var expectedSize = EnvelopeRelay.GetFrameSize(header, body.Length);
        var dest = new byte[expectedSize];

        EnvelopeRelay.TryWriteFrame(header, body, dest, out var written).Should().BeTrue();
        written.Should().Be(expectedSize);

        EnvelopeRelay.TryReadHeader(dest.AsSpan(0, written), out var roundTripped, out var roundBody).Should().BeTrue();
        roundTripped.Should().Be(header);
        roundBody.ToArray().Should().Equal(body);
    }

    [Fact]
    public void TryWriteFrame_ReturnsFalse_WhenDestinationTooSmall()
    {
        var body = new byte[] { 1, 2, 3 };
        var header = new ReadOnlyEnvelopeHeader(MessageType.Request, Guid.NewGuid(), "Hub", "key", 0x0001, MessageFlags.None);
        var tooSmall = new byte[2];

        EnvelopeRelay.TryWriteFrame(header, body, tooSmall, out var written).Should().BeFalse();
        written.Should().Be(0);
    }

    [Fact]
    public void TryReadHeader_ReturnsFalse_ForTruncatedFrame()
    {
        EnvelopeRelay.TryReadHeader(new byte[] { 0x01, 0x02 }, out _, out ReadOnlySpan<byte> _).Should().BeFalse();
    }

    [Fact]
    public void TryReadHeader_ReturnsFalse_ForBogusHeaderLength()
    {
        // headerLength 声称远大于剩余缓冲 → 必须拒绝。
        var bogus = new byte[] { 0xFF, 0xFF, 0x00, 0x00, 0x01, 0x02 };
        EnvelopeRelay.TryReadHeader(bogus, out _, out ReadOnlySpan<byte> _).Should().BeFalse();
    }

    [Fact]
    public void FromHeader_Throws_OnNull()
    {
        var act = () => ReadOnlyEnvelopeHeader.FromHeader(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithHub_And_WithKey_AreImmutableProjections()
    {
        var original = new ReadOnlyEnvelopeHeader(MessageType.Event, Guid.NewGuid(), "HubA", "k1", 0x0009, MessageFlags.HighPriority);

        var changedHub = original.WithHub("HubB");
        var changedKey = original.WithKey("k2");

        original.Hub.Should().Be("HubA");
        original.Key.Should().Be("k1");
        changedHub.Hub.Should().Be("HubB");
        changedHub.Key.Should().Be("k1");
        changedKey.Hub.Should().Be("HubA");
        changedKey.Key.Should().Be("k2");
        changedHub.MethodId.Should().Be(0x0009);
        changedHub.Flags.Should().Be(MessageFlags.HighPriority);
    }
}
