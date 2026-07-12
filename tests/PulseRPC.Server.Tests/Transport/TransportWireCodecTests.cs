using System.Security.Cryptography;
using PulseRPC.Abstractions.Transport.Security;
using PulseRPC.Shared;
using PulseRPC.Shared.Security;
using Xunit;

namespace PulseRPC.Server.Tests.Transport;

public sealed class TransportWireCodecTests
{
    [Fact]
    public void NegotiatedCompressionAndEncryption_MustRoundTripBothDirections()
    {
        var provider = new TestKeyProvider(Key(7, 0x37));
        var clientOptions = SecureOptions(provider);
        var serverOptions = SecureOptions(provider);
        var (client, server) = Negotiate(clientOptions, serverOptions);
        var payload = Enumerable.Repeat((byte)0x41, 4096).ToArray();

        var outbound = client.Encode(payload);
        Assert.Equal(
            TransportWireSession.CompressedFlag | TransportWireSession.EncryptedFlag,
            outbound.Flags);
        Assert.Equal(payload, server.Decode(outbound.Data, outbound.Flags));

        var response = server.Encode(payload);
        Assert.Equal(payload, client.Decode(response.Data, response.Flags));
    }

    [Fact]
    public void Encryption_MustRejectTamperingAndReplay()
    {
        var provider = new TestKeyProvider(Key(11, 0x5A));
        var (client, server) = Negotiate(SecureOptions(provider), SecureOptions(provider));
        var outbound = client.Encode(Enumerable.Repeat((byte)0x22, 2048).ToArray());
        var tampered = outbound.Data.ToArray();
        tampered[^1] ^= 0x80;

        Assert.ThrowsAny<CryptographicException>(() => server.Decode(tampered, outbound.Flags));
        var decoded = server.Decode(outbound.Data, outbound.Flags);
        Assert.Equal(2048, decoded.Length);
        Assert.Throws<InvalidDataException>(() => server.Decode(outbound.Data, outbound.Flags));
    }

    [Fact]
    public void CompressionThreshold_MustLeaveSmallPayloadUncompressedButEncrypted()
    {
        var provider = new TestKeyProvider(Key(13, 0x6B));
        var options = SecureOptions(provider);
        options.CompressionThreshold = 1024;
        var (client, server) = Negotiate(options, SecureOptions(provider));
        var payload = Enumerable.Repeat((byte)0x33, 128).ToArray();

        var outbound = client.Encode(payload);

        Assert.Equal(TransportWireSession.EncryptedFlag, outbound.Flags);
        Assert.Equal(payload, server.Decode(outbound.Data, outbound.Flags));
    }

    [Fact]
    public void CapabilityMismatch_MustFailInsteadOfDowngrading()
    {
        var clientOptions = new TcpTransportOptions { UseCompression = true };
        var serverOptions = new TcpTransportOptions();
        var offer = TransportWireNegotiator.CreateClientOffer(clientOptions);

        var accepted = TransportWireNegotiator.TryAcceptServerOffer(
            serverOptions,
            TransportWireNegotiator.SerializeOffer(offer),
            out _,
            out _,
            out var reason);

        Assert.False(accepted);
        Assert.Contains("能力不匹配", reason);
    }

    [Fact]
    public void RotationWindow_MustAcceptRetainedOldKeyAndUseNewKeyForNewOffers()
    {
        var oldKey = Key(21, 0x21);
        var newKey = Key(22, 0x22);
        var clientProvider = new TestKeyProvider(oldKey);
        var serverProvider = new TestKeyProvider(newKey, oldKey);
        var (oldClient, oldServer) = Negotiate(
            SecureOptions(clientProvider),
            SecureOptions(serverProvider));
        var oldPayload = oldClient.Encode(new byte[2048]);
        Assert.Equal(new byte[2048], oldServer.Decode(oldPayload.Data, oldPayload.Flags));

        clientProvider.SetCurrent(newKey);
        clientProvider.Add(newKey);
        var (newClient, newServer) = Negotiate(
            SecureOptions(clientProvider),
            SecureOptions(serverProvider));
        var newPayload = newClient.Encode(new byte[2048]);
        Assert.Equal(new byte[2048], newServer.Decode(newPayload.Data, newPayload.Flags));
    }

    private static (TransportWireSession Client, TransportWireSession Server) Negotiate(
        TransportOptions clientOptions,
        TransportOptions serverOptions)
    {
        var offer = TransportWireNegotiator.CreateClientOffer(clientOptions);
        Assert.True(TransportWireNegotiator.TryAcceptServerOffer(
            serverOptions,
            TransportWireNegotiator.SerializeOffer(offer),
            out var response,
            out var server,
            out var serverReason), serverReason);
        Assert.True(TransportWireNegotiator.TryCompleteClient(
            clientOptions,
            offer,
            response,
            out var client,
            out var clientReason), clientReason);
        return (client!, server!);
    }

    private static TcpTransportOptions SecureOptions(ITransportEncryptionKeyProvider provider)
        => new()
        {
            UseCompression = true,
            UseEncryption = true,
            EncryptionKeyProvider = provider,
            CompressionThreshold = 256
        };

    private static TransportEncryptionKey Key(uint id, byte fill)
        => new(id, Enumerable.Repeat(fill, 32).ToArray());

    private sealed class TestKeyProvider : ITransportEncryptionKeyProvider
    {
        private readonly Dictionary<uint, TransportEncryptionKey> _keys;
        private TransportEncryptionKey _current;

        public TestKeyProvider(TransportEncryptionKey current, params TransportEncryptionKey[] retained)
        {
            _current = current;
            _keys = retained.Append(current).ToDictionary(key => key.KeyId);
        }

        public TransportEncryptionKey GetCurrentKey() => _current;

        public bool TryGetKey(uint keyId, out TransportEncryptionKey key)
            => _keys.TryGetValue(keyId, out key);

        public void SetCurrent(TransportEncryptionKey key) => _current = key;

        public void Add(TransportEncryptionKey key) => _keys[key.KeyId] = key;
    }
}
