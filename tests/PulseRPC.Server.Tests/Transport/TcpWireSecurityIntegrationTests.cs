using System.Net;
using System.Net.Sockets;
using PulseRPC.Abstractions.Transport.Security;
using PulseRPC.Client.Transport;
using PulseRPC.Server.Transport;
using PulseRPC.Shared;
using Xunit;

namespace PulseRPC.Server.Tests.Transport;

public sealed class TcpWireSecurityIntegrationTests
{
    [Fact]
    public async Task TcpV3_MustNegotiateAndRoundTripCompressedEncryptedFrames()
    {
        var port = GetAvailablePort();
        var provider = new StaticKeyProvider(
            new TransportEncryptionKey(42, Enumerable.Repeat((byte)0xA5, 32).ToArray()));
        var serverOptions = CreateOptions(provider);
        var clientOptions = CreateOptions(provider);
        using var listener = new TcpServerListener(port, serverOptions);
        using var client = new TcpClientTransport("secure-client", clientOptions);
        var accepted = new TaskCompletionSource<TcpServerTransport>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverReceived = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.ConnectionAccepted += (_, args) =>
        {
            var transport = Assert.IsType<TcpServerTransport>(args.Transport);
            transport.DataReceived += (_, data) => serverReceived.TrySetResult(data.Data.ToArray());
            accepted.TrySetResult(transport);
        };

        await listener.StartAsync();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        var server = await accepted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var payload = Enumerable.Repeat((byte)0x44, 4096).ToArray();

        Assert.True(await client.SendAsync(payload));
        Assert.Equal(payload, await serverReceived.Task.WaitAsync(TimeSpan.FromSeconds(3)));

        var clientReceived = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.DataReceived += (_, data) => clientReceived.TrySetResult(data.Data.ToArray());
        Assert.True(await server.SendAsync(payload));
        Assert.Equal(payload, await clientReceived.Task.WaitAsync(TimeSpan.FromSeconds(3)));

        await client.DisconnectAsync();
        server.Dispose();
        await listener.StopAsync();
    }

    private static TcpTransportOptions CreateOptions(ITransportEncryptionKeyProvider provider)
        => new()
        {
            UseCompression = true,
            UseEncryption = true,
            EncryptionKeyProvider = provider,
            CompressionThreshold = 256
        };

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class StaticKeyProvider : ITransportEncryptionKeyProvider
    {
        private readonly TransportEncryptionKey _key;

        public StaticKeyProvider(TransportEncryptionKey key) => _key = key;

        public TransportEncryptionKey GetCurrentKey() => _key;

        public bool TryGetKey(uint keyId, out TransportEncryptionKey key)
        {
            key = _key;
            return keyId == _key.KeyId;
        }
    }
}
