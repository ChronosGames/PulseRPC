using System.Net;
using PulseRPC.Abstractions.Transport.Security;
using PulseRPC.Client.Transport;
using PulseRPC.Server.Transport;
using PulseRPC.Shared;
using Xunit;

namespace PulseRPC.Server.Tests.Transport;

public sealed class KcpWireSecurityIntegrationTests
{
    [Fact]
    public async Task KcpV3_MustNegotiateAndRoundTripCompressedEncryptedMessages()
    {
        const uint conversationId = 0x31415926;
        var provider = new StaticKeyProvider(
            new TransportEncryptionKey(77, Enumerable.Repeat((byte)0x7C, 32).ToArray()));
        var serverOptions = CreateOptions(provider, conversationId);
        var clientOptions = CreateOptions(provider, conversationId);
        using var listener = new KcpServerListener(0, serverOptions);
        using var client = new KcpClientTransport("secure-kcp-client", clientOptions);
        var accepted = new TaskCompletionSource<KcpServerTransport>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverReceived = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.ConnectionAccepted += (_, args) =>
        {
            var transport = Assert.IsType<KcpServerTransport>(args.Transport);
            transport.DataReceived += (_, data) => serverReceived.TrySetResult(data.Data.ToArray());
            accepted.TrySetResult(transport);
        };

        await listener.StartAsync();
        var port = ((IPEndPoint)listener.LocalEndPoint).Port;
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        var server = await accepted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var payload = Enumerable.Repeat((byte)0x51, 4096).ToArray();

        Assert.True(await client.SendAsync(payload));
        Assert.Equal(payload, await serverReceived.Task.WaitAsync(TimeSpan.FromSeconds(3)));

        var clientReceived = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.DataReceived += (_, data) => clientReceived.TrySetResult(data.Data.ToArray());
        Assert.True(await server.SendAsync(payload));
        Assert.Equal(payload, await clientReceived.Task.WaitAsync(TimeSpan.FromSeconds(3)));

        await client.DisconnectAsync();
        await listener.StopAsync();
    }

    private static KcpTransportOptions CreateOptions(
        ITransportEncryptionKeyProvider provider,
        uint conversationId)
        => new()
        {
            ConversationId = conversationId,
            Interval = 5,
            UseCompression = true,
            UseEncryption = true,
            EncryptionKeyProvider = provider,
            CompressionThreshold = 256,
            HandshakeRetryCount = 1,
            EnableNetworkDiagnostics = false
        };

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
