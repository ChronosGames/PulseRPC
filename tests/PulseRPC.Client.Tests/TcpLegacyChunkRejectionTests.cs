using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using PulseRPC.Client.Transport;
using PulseRPC.Shared;
using PulseRPC.Shared.Tcp;
using Xunit;

namespace PulseRPC.Client.Tests;

public sealed class TcpLegacyChunkRejectionTests
{
    [Fact]
    public async Task LegacyChunkFrame_MustDisconnectWithoutCreatingPacketState()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var releaseServer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = RunServerAsync(listener, releaseServer.Task, testTimeout.Token);

        using var transport = new InspectableTcpClientTransport(
            "legacy-chunk-test",
            new TcpTransportOptions
            {
                AutoReconnect = false,
                ConnectionTimeout = 2_000,
                MaxPacketSize = 64 * 1024,
            });

        var disconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.StateChanged += (_, args) =>
        {
            if (args.CurrentState == ConnectionState.Disconnected)
            {
                disconnected.TrySetResult(true);
            }
        };

        try
        {
            await transport.ConnectAsync(IPAddress.Loopback.ToString(), port, testTimeout.Token);
            await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(0, transport.PacketStatistics.ActivePackets);
        }
        finally
        {
            releaseServer.TrySetResult(true);
            await serverTask;
        }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(2, -1)]
    [InlineData(2, 2)]
    [InlineData(4097, 0)]
    [InlineData(int.MaxValue, 0)]
    public void LargePacketHandler_InvalidChunkMetadata_MustRejectWithoutCreatingState(
        int totalChunks,
        int chunkIndex)
    {
        using var handler = new LargePacketHandler();
        var header = new ChunkHeader(
            chunkId: 42,
            chunkIndex,
            totalChunks,
            chunkSize: 1);

        var accepted = handler.ProcessChunk(header, new byte[] { 0x2A }, out var completed);

        Assert.False(accepted);
        Assert.True(completed.IsEmpty);
        Assert.Equal(0, handler.GetStatistics().ActivePackets);
    }

    [Fact]
    public void LargePacketHandler_ValidOutOfOrderChunks_MustReassembleWithinLimit()
    {
        using var handler = new LargePacketHandler();

        Assert.False(handler.ProcessChunk(
            new ChunkHeader(42, chunkIndex: 1, totalChunks: 2, chunkSize: 2),
            new byte[] { 3, 4 },
            out _));

        Assert.True(handler.ProcessChunk(
            new ChunkHeader(42, chunkIndex: 0, totalChunks: 2, chunkSize: 2),
            new byte[] { 1, 2 },
            out var completed));

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, completed.ToArray());
        Assert.Equal(0, handler.GetStatistics().ActivePackets);
    }

    private static async Task RunServerAsync(
        TcpListener listener,
        Task releaseTask,
        CancellationToken cancellationToken)
    {
        using var socket = await listener.AcceptSocketAsync(cancellationToken);
        await using var stream = new NetworkStream(socket, ownsSocket: false);

        var handshakeHeader = await ReadFrameHeaderAsync(stream, cancellationToken);
        Assert.Equal(ProtocolConstants.HandshakeMessageId, handshakeHeader.MessageId);
        _ = await ReadExactAsync(stream, handshakeHeader.Length, cancellationToken);

        var handshakeResponse = new HandshakeResponse(
            accepted: true,
            ProtocolConstants.CurrentProtocolVersion).ToBytes();
        await WriteFrameAsync(
            stream,
            handshakeResponse,
            ProtocolConstants.HandshakeMessageId,
            ProtocolConstants.HandshakeResponseFlag,
            cancellationToken);

        var chunkBody = new byte[ChunkHeader.Size + 1];
        BinaryPrimitives.WriteInt32LittleEndian(chunkBody.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteInt32LittleEndian(chunkBody.AsSpan(4, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(chunkBody.AsSpan(8, 4), int.MaxValue);
        BinaryPrimitives.WriteInt32LittleEndian(chunkBody.AsSpan(12, 4), 1);
        chunkBody[^1] = 0x7F;

        await WriteFrameAsync(
            stream,
            chunkBody,
            messageId: 0,
            FrameHeader.FlagChunked,
            cancellationToken);

        await releaseTask.WaitAsync(cancellationToken);
    }

    private static async Task<FrameHeader> ReadFrameHeaderAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var bytes = await ReadExactAsync(stream, FrameHeader.Size, cancellationToken);
        return new FrameHeader(
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0, 2)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(2, 4)),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8, 2)));
    }

    private static async Task<byte[]> ReadExactAsync(
        Stream stream,
        int length,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }

        return buffer;
    }

    private static async Task WriteFrameAsync(
        Stream stream,
        byte[] body,
        ushort messageId,
        ushort flags,
        CancellationToken cancellationToken)
    {
        var frame = new byte[FrameHeader.Size + body.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(0, 2), ProtocolConstants.ProtocolMagic);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(2, 4), body.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6, 2), messageId);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(8, 2), flags);
        body.CopyTo(frame, FrameHeader.Size);
        await stream.WriteAsync(frame, cancellationToken);
    }

    private sealed class InspectableTcpClientTransport : TcpClientTransport
    {
        public InspectableTcpClientTransport(string id, TcpTransportOptions options)
            : base(id, options)
        {
        }

        public PacketHandlerStatistics PacketStatistics => _packetHandler.GetStatistics();
    }
}
