using System;
using FluentAssertions;
using MemoryPack;
using PulseRPC.Messaging;
using PulseRPC.Server.Contexts;
using Xunit;

namespace PulseRPC.Server.Tests;

/// <summary>
/// 验证 P-5 中 <see cref="MessageHeader.ServiceKey"/> 新增字段的行为，及其在请求上下文管线中的暴露：
/// <list type="bullet">
/// <item><see cref="MessageHeader.ServiceKey"/> 通过 MemoryPack 往返保值；</item>
/// <item>未设置时默认为空字符串（不会是 <c>null</c>）；</item>
/// <item>作为 MemoryPack 尾部新增成员，序列化/反序列化自洽（帧长与消费长度一致）；</item>
/// <item><see cref="IPulseContext.ServiceKey"/> / <see cref="PulseContext.CurrentServiceKey"/> 能读到路由实例键。</item>
/// </list>
/// </summary>
public class MessageHeaderServiceKeyTests
{
    [Fact]
    public void ServiceKey_RoundTrips_ThroughMemoryPack()
    {
        var header = new MessageHeader(MessageType.OneWay, "Hub", "M")
        {
            ProtocolId = 0x0007,
            ServiceKey = "player-77",
        };

        var bytes = MemoryPackSerializer.Serialize(header);
        var restored = MemoryPackSerializer.Deserialize<MessageHeader>(bytes);

        restored.Should().NotBeNull();
        restored!.ServiceKey.Should().Be("player-77");
        restored.ServiceName.Should().Be("Hub");
        restored.ProtocolId.Should().Be(0x0007);
    }

    [Fact]
    public void ServiceKey_DefaultsToEmpty_NotNull()
    {
        var header = new MessageHeader();

        header.ServiceKey.Should().NotBeNull();
        header.ServiceKey.Should().BeEmpty();

        var bytes = MemoryPackSerializer.Serialize(header);
        var restored = MemoryPackSerializer.Deserialize<MessageHeader>(bytes);

        restored!.ServiceKey.Should().BeEmpty();
    }

    [Fact]
    public void MessagePacket_RoundTrip_PreservesServiceKey()
    {
        var header = new MessageHeader(MessageType.Request, "PlayerHub", "MoveAsync")
        {
            ProtocolId = 0x1234,
            ServiceKey = "player-42",
        };
        var body = new byte[] { 1, 2, 3 };
        var packet = new MessagePacket(header, body);
        var buffer = new byte[packet.EstimateSize() + 128];
        var written = packet.WriteTo(buffer);

        MessagePacket.TryReadFrom(buffer.AsSpan(0, written), out var parsed).Should().BeTrue();

        parsed.Header.ServiceKey.Should().Be("player-42");
        parsed.Header.ProtocolId.Should().Be(0x1234);
        parsed.Payload.ToArray().Should().Equal(body);
    }

    [Fact]
    public void PulseContext_SurfacesRoutedServiceKey()
    {
        var context = new PulseContextData
        {
            ServiceName = "PlayerHub",
            ServiceKey = "player-42",
            MethodName = "MoveAsync",
        };

        using (PulseContext.SetContext(context))
        {
            PulseContext.Current!.ServiceKey.Should().Be("player-42");
            PulseContext.CurrentServiceKey.Should().Be("player-42");
        }
    }

    [Fact]
    public void PulseContextData_ServiceKey_DefaultsToEmpty()
    {
        new PulseContextData().ServiceKey.Should().BeEmpty();
    }

    [Fact]
    public void PulseContext_ServiceKey_InterfaceDefault_IsEmpty()
    {
        // IPulseContext.ServiceKey 提供默认接口实现（空字符串），保证既有实现无需改动即可编译。
        IPulseContext context = new PulseContextData { ServiceName = "Hub", MethodName = "M" };
        context.ServiceKey.Should().BeEmpty();
    }
}
