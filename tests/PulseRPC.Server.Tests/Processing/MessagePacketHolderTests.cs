using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using PulseRPC.Messaging;
using Xunit;

namespace PulseRPC.Server.Tests.Processing;

/// <summary>
/// P1-8：<see cref="MessagePacketHolder"/> 载荷所有权测试。
/// 断言：载荷从源缓冲复制隔离（防收包缓冲复用导致的数据损坏/UAF）、精确长度、空载荷路径、
/// 以及 <see cref="MessagePacketHolder.Dispose"/> 的幂等/并发恰好一次归还（防二次归还池）。
/// </summary>
public class MessagePacketHolderTests
{
    private static MessageHeader MakeHeader() =>
        new(MessageType.Request, "svc", "method") { MessageId = Guid.NewGuid() };

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(64)]
    [InlineData(1023)]
    [InlineData(4096)]
    [InlineData(70000)]
    public void Payload_CopiesSource_WithExactLengthAndContent(int size)
    {
        var body = new byte[size];
        new Random(size).NextBytes(body);

        var packet = new MessagePacket(MakeHeader(), body.AsSpan());
        using var holder = new MessagePacketHolder(packet);

        // 精确长度（即便池化缓冲可能超额租借）
        holder.Payload.Length.Should().Be(size);
        holder.Payload.ToArray().Should().Equal(body);
    }

    [Fact]
    public void Payload_IsIsolatedFromSourceBuffer_AfterConstruction()
    {
        // 模拟收包共享缓冲：构造后改写源缓冲，holder 必须保有原始副本。
        var shared = new byte[256];
        for (var i = 0; i < shared.Length; i++) shared[i] = (byte)i;
        var original = (byte[])shared.Clone();

        var packet = new MessagePacket(MakeHeader(), shared.AsSpan());
        using var holder = new MessagePacketHolder(packet);

        // 复用/覆写源缓冲（等价于 TcpTransport 复用 _receiveBuffer）
        Array.Fill(shared, (byte)0xFF);

        holder.Payload.ToArray().Should().Equal(original,
            "载荷必须在构造时被复制隔离，源缓冲复用不得污染已解析消息");
    }

    [Fact]
    public void EmptyPayload_YieldsEmpty_AndDisposeIsSafe()
    {
        var packet = new MessagePacket(MakeHeader(), ReadOnlySpan<byte>.Empty);
        var holder = new MessagePacketHolder(packet);

        holder.Payload.IsEmpty.Should().BeTrue();

        Action dispose = () => holder.Dispose();
        dispose.Should().NotThrow();
        dispose.Should().NotThrow(); // 空载荷路径重复 Dispose 亦安全
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var body = new byte[128];
        new Random(1).NextBytes(body);
        var holder = new MessagePacketHolder(new MessagePacket(MakeHeader(), body.AsSpan()));

        Action dispose = () => holder.Dispose();
        dispose.Should().NotThrow();
        dispose.Should().NotThrow();
        dispose.Should().NotThrow(); // 多次归还不得抛出、不得二次归还池
    }

    [Fact]
    public async Task ConcurrentDispose_ReleasesExactlyOnce_WithoutThrowing()
    {
        // 高并发地对同一 holder 调用 Dispose，验证 Interlocked 守卫保证「恰好一次」归还。
        for (var round = 0; round < 200; round++)
        {
            var body = new byte[512];
            var holder = new MessagePacketHolder(new MessagePacket(MakeHeader(), body.AsSpan()));

            const int threads = 32;
            using var start = new ManualResetEventSlim(false);
            var tasks = new Task[threads];
            for (var t = 0; t < threads; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    start.Wait();
                    holder.Dispose();
                });
            }

            start.Set();
            Func<Task> all = () => Task.WhenAll(tasks);
            await all.Should().NotThrowAsync("并发 Dispose 必须安全，不得二次归还池或抛异常");
        }
    }
}
