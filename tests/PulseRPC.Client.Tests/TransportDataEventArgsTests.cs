using PulseRPC.Shared;
using Xunit;

namespace PulseRPC.Client.Tests;

public class TransportDataEventArgsTests
{
    [Fact]
    public void Constructor_MustCopyExternalMemory()
    {
        var buffer = new byte[] { 1, 2, 3, 4 };

        var args = new TransportDataEventArgs(new ReadOnlyMemory<byte>(buffer, 1, 2));
        buffer[1] = 9;

        Assert.Equal(new byte[] { 2, 3 }, args.Data.ToArray());
    }

    [Fact]
    public void OwnedBufferConstructor_MustExposeRequestedSegment()
    {
        var buffer = new byte[] { 1, 2, 3, 4 };

        var args = new TransportDataEventArgs(buffer, 3);

        Assert.Equal(new byte[] { 1, 2, 3 }, args.Data.ToArray());
    }
}
