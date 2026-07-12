using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Server.Clustering;
using PulseRPC.Shared;
using PulseRPC.Shared.Tcp;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

public sealed class TcpNodeTransportConnectionTests
{
    [Fact]
    public async Task ConnectAsync_InternalHandshakeDeadline_MustSurfaceTimeoutException()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        using var client = CreateClient(TimeSpan.FromMilliseconds(100));

        var connectTask = client.ConnectAsync(endpoint.Address.ToString(), endpoint.Port, CancellationToken.None);
        using var accepted = await listener.AcceptTcpClientAsync();

        var act = async () => await connectTask;

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task ConnectAsync_CallerCancellation_MustRemainOperationCanceled()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        using var client = CreateClient(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var connectTask = client.ConnectAsync(endpoint.Address.ToString(), endpoint.Port, cancellation.Token);
        using var accepted = await listener.AcceptTcpClientAsync();
        var act = async () => await connectTask;

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReceiveFrame_ExactlyReceiveBufferSize_MustNotBeOverwrittenByNextFrame()
    {
        const int bodyLength = 128;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        using var client = CreateClient(TimeSpan.FromSeconds(2), receiveBufferSize: bodyLength);
        var firstReceived = new TaskCompletionSource<ReadOnlyMemory<byte>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReceived = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var receiveCount = 0;
        client.DataReceived += (_, args) =>
        {
            if (Interlocked.Increment(ref receiveCount) == 1)
            {
                firstReceived.TrySetResult(args.Data);
            }
            else
            {
                secondReceived.TrySetResult();
            }
        };

        var connectTask = client.ConnectAsync(endpoint.Address.ToString(), endpoint.Port, CancellationToken.None);
        using var accepted = await listener.AcceptTcpClientAsync();
        using var stream = accepted.GetStream();
        await CompleteServerHandshakeAsync(stream);
        await connectTask;

        await WriteFrameAsync(stream, Enumerable.Repeat((byte)0x11, bodyLength).ToArray());
        var first = await firstReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WriteFrameAsync(stream, Enumerable.Repeat((byte)0x22, bodyLength).ToArray());
        await secondReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));

        first.ToArray().Should().OnlyContain(value => value == 0x11);
    }

    private static TcpNodeClient CreateClient(TimeSpan connectTimeout, int receiveBufferSize = 1024)
        => new(
            $"test-{Guid.NewGuid():N}",
            new TcpTransportOptions
            {
                RecvBufferSize = receiveBufferSize,
                SendBufferSize = 1024,
                SendQueueCapacity = 4,
                MaxPacketSize = 4096,
                NoDelay = true,
            },
            connectTimeout,
            NullLogger.Instance);

    private static async Task CompleteServerHandshakeAsync(NetworkStream stream)
    {
        var requestHeader = new byte[FrameHeader.Size];
        await ReadExactlyAsync(stream, requestHeader);
        var bodyLength = BinaryPrimitives.ReadInt32LittleEndian(requestHeader.AsSpan(2, 4));
        var requestBody = new byte[bodyLength];
        await ReadExactlyAsync(stream, requestBody);
        var request = HandshakeMessage.FromBytes(requestBody);

        var response = HandshakeResponse.WithExtensions(
            accepted: true,
            serverProtocolVersion: ProtocolConstants.CurrentProtocolVersion,
            reason: null,
            extensions: request.Extensions).ToBytes();
        await WriteFrameAsync(
            stream,
            response,
            ProtocolConstants.HandshakeMessageId,
            ProtocolConstants.HandshakeResponseFlag);
    }

    private static async Task WriteFrameAsync(
        NetworkStream stream,
        byte[] body,
        ushort messageId = 0,
        ushort flags = 0)
    {
        var frame = new byte[FrameHeader.Size + body.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, ProtocolConstants.ProtocolMagic);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(2), body.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6), messageId);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(8), flags);
        body.CopyTo(frame, FrameHeader.Size);
        await stream.WriteAsync(frame);
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset));
            if (read == 0)
            {
                throw new IOException("测试连接在读取完整帧前关闭。");
            }

            offset += read;
        }
    }
}
