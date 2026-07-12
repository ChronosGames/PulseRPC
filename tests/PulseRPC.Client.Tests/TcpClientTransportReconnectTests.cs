using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using PulseRPC.Client.Transport;
using PulseRPC.Shared;
using PulseRPC.Shared.Tcp;
using Xunit;

namespace PulseRPC.Client.Tests;

public sealed class TcpClientTransportReconnectTests
{
    [Fact]
    public async Task AutoReconnect_MustReconnectAfterEstablishedSocketCloses()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var firstHandshake = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondHandshake = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var server = Task.Run(async () =>
        {
            using (var first = await listener.AcceptTcpClientAsync(cts.Token))
            {
                await CompleteHandshakeAsync(first.GetStream(), cts.Token);
                firstHandshake.TrySetResult(true);
                await releaseFirst.Task.WaitAsync(cts.Token);
            }

            using var second = await listener.AcceptTcpClientAsync(cts.Token);
            await CompleteHandshakeAsync(second.GetStream(), cts.Token);
            secondHandshake.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
        }, cts.Token);

        using var transport = new TcpClientTransport(
            "tcp-reconnect",
            new TcpTransportOptions
            {
                AutoReconnect = true,
                ReconnectInterval = 50,
                MaxReconnectAttempts = 2,
                ConnectionTimeout = 1000
            });

        await transport.ConnectAsync(IPAddress.Loopback.ToString(), port, cts.Token);
        await firstHandshake.Task.WaitAsync(TimeSpan.FromSeconds(3));
        releaseFirst.TrySetResult(true);

        await secondHandshake.Task.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.True(transport.IsConnected);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => server);
    }

    private static async Task CompleteHandshakeAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var requestHeader = new byte[FrameHeader.Size];
        await stream.ReadExactlyAsync(requestHeader, cancellationToken);
        var bodyLength = BinaryPrimitives.ReadInt32LittleEndian(requestHeader.AsSpan(2, 4));
        var requestBody = new byte[bodyLength];
        await stream.ReadExactlyAsync(requestBody, cancellationToken);

        Assert.Equal(ProtocolConstants.ProtocolMagic, BinaryPrimitives.ReadUInt16LittleEndian(requestHeader));
        Assert.Equal(ProtocolConstants.HandshakeMessageId, BinaryPrimitives.ReadUInt16LittleEndian(requestHeader.AsSpan(6, 2)));
        Assert.Equal(ProtocolConstants.HandshakeRequestFlag, BinaryPrimitives.ReadUInt16LittleEndian(requestHeader.AsSpan(8, 2)));
        var request = HandshakeMessage.FromBytes(requestBody);

        var response = HandshakeResponse.WithExtensions(
            accepted: true,
            serverProtocolVersion: ProtocolConstants.CurrentProtocolVersion,
            reason: null,
            extensions: request.Extensions).ToBytes();
        var responseHeader = new byte[FrameHeader.Size];
        BinaryPrimitives.WriteUInt16LittleEndian(responseHeader, ProtocolConstants.ProtocolMagic);
        BinaryPrimitives.WriteInt32LittleEndian(responseHeader.AsSpan(2, 4), response.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(responseHeader.AsSpan(6, 2), ProtocolConstants.HandshakeMessageId);
        BinaryPrimitives.WriteUInt16LittleEndian(responseHeader.AsSpan(8, 2), ProtocolConstants.HandshakeResponseFlag);
        await stream.WriteAsync(responseHeader, cancellationToken);
        await stream.WriteAsync(response, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
