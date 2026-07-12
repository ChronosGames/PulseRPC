using PulseRPC.Shared;
using PulseRPC.Shared.Kcp;
using Xunit;

namespace PulseRPC.Client.Tests;

public sealed class KcpTransportSendSemanticsTests
{
    [Fact]
    public async Task SendAsync_WithPreCanceledToken_ReturnsFalseLikeTcpTransport()
    {
        using var transport = new ConnectedKcpTransport();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var sent = await transport.SendAsync(new byte[] { 1 }, cancellation.Token);

        Assert.False(sent);
    }

    private sealed class ConnectedKcpTransport : KcpTransport
    {
        public ConnectedKcpTransport()
        {
            _state = ConnectionState.Connected;
        }

        public override string Id => "kcp-send-contract";
    }
}
