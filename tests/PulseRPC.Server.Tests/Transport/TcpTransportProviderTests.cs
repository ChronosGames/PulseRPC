using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Extensions;
using PulseRPC.Server.Transport;
using PulseRPC.Shared;
using PulseRPC.Shared.Tcp;
using Xunit;

namespace PulseRPC.Server.Tests.Transport;

public sealed class TcpTransportProviderTests
{
    [Fact]
    public async Task DiRegisteredProvider_MustRejectLegacyV1Handshake()
    {
        var port = GetAvailableTcpPort();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPulseServer(options => options.AddTcp("v2-only", port));

        await using var serviceProvider = services.BuildServiceProvider();
        var serverOptions = serviceProvider.GetRequiredService<IOptions<PulseServerOptions>>().Value;
        var transportConfig = serverOptions.Transports.Single(transport => transport.Name == "v2-only");
        var tcpProvider = serviceProvider.GetServices<ITransportProvider>()
            .Single(provider => provider.TransportType == TransportType.TCP.ToString());
        using var listener = tcpProvider.CreateServerListener(transportConfig, NullLoggerFactory.Instance);
        var accepted = new TaskCompletionSource<TcpServerTransport>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.ConnectionAccepted += (_, args) =>
            accepted.TrySetResult(Assert.IsType<TcpServerTransport>(args.Transport));

        await listener.StartAsync();
        TcpServerTransport? serverTransport = null;
        try
        {
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, port);
            serverTransport = await accepted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await using var stream = client.GetStream();

            var legacyHandshake = new HandshakeMessage(1, "legacy-v1").ToBytes();
            await WriteFrameAsync(
                stream,
                legacyHandshake,
                ProtocolConstants.HandshakeMessageId,
                ProtocolConstants.HandshakeRequestFlag);

            var responseHeader = await ReadFrameHeaderAsync(stream);
            var response = HandshakeResponse.FromBytes(await ReadExactAsync(stream, responseHeader.Length));

            response.Accepted.Should().BeFalse();
            response.ServerProtocolVersion.Should().Be(3);
            response.Reason.Should().Contain("支持的版本范围: 3-3");
        }
        finally
        {
            serverTransport?.Dispose();
            await listener.StopAsync();
        }
    }

    [Fact]
    public async Task DiRegisteredProvider_MustHonorConfiguredMaxPacketSizeOnReceive()
    {
        const int configuredMaxPacketSize = 256;
        var port = GetAvailableTcpPort();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPulseServer(options => options.AddTcp(
            "bounded-tcp",
            port,
            configure: tcp => tcp.MaxPacketSize = configuredMaxPacketSize));

        await using var serviceProvider = services.BuildServiceProvider();
        var serverOptions = serviceProvider.GetRequiredService<IOptions<PulseServerOptions>>().Value;
        var transportConfig = serverOptions.Transports.Single(transport => transport.Name == "bounded-tcp");
        var tcpProvider = serviceProvider.GetServices<ITransportProvider>()
            .Single(provider => provider.TransportType == TransportType.TCP.ToString());

        using var listener = tcpProvider.CreateServerListener(transportConfig, NullLoggerFactory.Instance);
        var accepted = new TaskCompletionSource<TcpServerTransport>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.ConnectionAccepted += (_, args) =>
        {
            var transport = Assert.IsType<TcpServerTransport>(args.Transport);
            transport.StateChanged += (_, state) =>
            {
                if (state.CurrentState == ConnectionState.Disconnected)
                {
                    disconnected.TrySetResult(true);
                }
            };
            accepted.TrySetResult(transport);
        };

        await listener.StartAsync();
        TcpServerTransport? serverTransport = null;
        try
        {
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, port);
            serverTransport = await accepted.Task.WaitAsync(TimeSpan.FromSeconds(3));

            await using var stream = client.GetStream();
            var handshake = new HandshakeMessage(
                ProtocolConstants.CurrentProtocolVersion,
                "test",
                "prpc-wire-v3|0|1024|0|").ToBytes();
            await WriteFrameAsync(
                stream,
                handshake,
                ProtocolConstants.HandshakeMessageId,
                ProtocolConstants.HandshakeRequestFlag);

            var responseHeader = await ReadFrameHeaderAsync(stream);
            Assert.Equal(ProtocolConstants.HandshakeMessageId, responseHeader.MessageId);
            _ = await ReadExactAsync(stream, responseHeader.Length);

            await WriteFrameHeaderAsync(
                stream,
                length: configuredMaxPacketSize + 1,
                messageId: 0,
                flags: FrameHeader.FlagNone);

            await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            serverTransport?.Dispose();
            await listener.StopAsync();
        }
    }

    private static int GetAvailableTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task WriteFrameAsync(
        NetworkStream stream,
        byte[] body,
        ushort messageId,
        ushort flags)
    {
        await WriteFrameHeaderAsync(stream, body.Length, messageId, flags);
        await stream.WriteAsync(body);
    }

    private static async Task WriteFrameHeaderAsync(
        NetworkStream stream,
        int length,
        ushort messageId,
        ushort flags)
    {
        var header = new byte[FrameHeader.Size];
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0, 2), ProtocolConstants.ProtocolMagic);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(2, 4), length);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6, 2), messageId);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8, 2), flags);
        await stream.WriteAsync(header);
    }

    private static async Task<FrameHeader> ReadFrameHeaderAsync(NetworkStream stream)
    {
        var bytes = await ReadExactAsync(stream, FrameHeader.Size);
        return new FrameHeader(
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0, 2)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(2, 4)),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8, 2)));
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int length)
    {
        var bytes = new byte[length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(offset));
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }

        return bytes;
    }
}
