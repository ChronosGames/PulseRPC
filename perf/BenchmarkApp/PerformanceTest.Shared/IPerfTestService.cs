using PulseRPC;
using MemoryPack;

namespace PerformanceTest.Shared;

public interface IPerfTestStreamingHub : IStreamingHub<IPerfTestStreamingHub>
{
    UnaryResult<string> UnaryArgDynamicArgumentTupleReturnRef(string arg1, int arg2, int arg3, int arg4);
    UnaryResult<int> UnaryArgDynamicArgumentTupleReturnValue(string arg1, int arg2, int arg3, int arg4);
    UnaryResult<(int StatusCode, byte[] Data)> UnaryLargePayloadAsync(string arg1, int arg2, int arg3, int arg4, byte[] arg5);
    UnaryResult<ComplexResponse> UnaryComplexAsync(string arg1, int arg2, int arg3, int arg4);

    // ServerStreaming
    Task<ServerStreamingResult<SimpleResponse>> ServerStreamingAsync(TimeSpan timeout);
}

[MemoryPackable, Packet]
public partial record UnaryParameterlessRequest : IRequest;

[MemoryPackable, Packet]
public partial record UnaryParameterlessResponse : IResponse;

[MemoryPackable, Packet]
public partial record UnaryArgRefReturnRefRequest(string Arg1, int Arg2, int Arg3) : IRequest
{
    public static UnaryArgRefReturnRefRequest Create(string arg1, int arg2, int arg3)
    {
        return new UnaryArgRefReturnRefRequest(arg1, arg2, arg3);
    }
}

[MemoryPackable, Packet]
public partial record UnaryArgRefReturnRefResponse(string Arg1) : IResponse
{
    public static UnaryArgRefReturnRefResponse Create(string arg1)
    {
        return new UnaryArgRefReturnRefResponse(arg1);
    }
}
