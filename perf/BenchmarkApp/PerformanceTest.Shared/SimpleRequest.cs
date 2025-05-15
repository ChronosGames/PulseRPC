using MemoryPack;

namespace PerformanceTest.Shared;

[MemoryPackable]
public partial class SimpleRequest
{
    public static SimpleRequest Cached { get; } = new SimpleRequest
    {
        Payload = [],
        ResponseSize = 0,
        UseCache = true,
    };

    public byte[] Payload { get; set; } = default!;

    public int ResponseSize { get; set; }

    public bool UseCache { get; set; }
}

[MemoryPackable]
public partial class SimpleResponse
{
    public static SimpleResponse Cached { get; } = new SimpleResponse
    {
        Payload = [],
    };

    public byte[] Payload { get; set; } = default!;
}
