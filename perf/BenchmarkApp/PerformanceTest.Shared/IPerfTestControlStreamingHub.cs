using MemoryPack;
using System.Runtime.InteropServices;
using PulseRPC;

namespace PerformanceTest.Shared;

public interface IPerfTestControlStreamingHub : IStreamingHub<IPerfTestControlStreamingHub>
{
    UnaryResult<ServerInformation> GetServerInformationAsync();
    UnaryResult<(string serverMagicOnionVersion, bool enableLatestTag)> ExchangeMagicOnionVersionTagAsync(string? clientMagicOnionVersion, bool isLatestMagicOnionVersion);

    UnaryResult SetMemoryProfilerCollectAllocationsAsync(bool enable);
    UnaryResult CreateMemoryProfilerSnapshotAsync(string name);
}

public interface IDownlink
{

}

[MemoryPackable, Packet]
public partial record EmptyRequest : IRequest;

public partial record ExchangePulseVersionTagRequest(string? ClientMagicOnionVersion, bool IsLatestMagicOnionVersion) : IRequest
{
    public static ExchangePulseVersionTagRequest Default => new(null, false);
}

[MemoryPackable, Packet]
public partial record ExchangeMagicOnionVersionTagResponse(string ServerPulseRPCVersion, bool EnableLatestTag) : IResponse;

[MemoryPackable, Packet]
public partial class ServerInformation : IResponse
{
    public string MachineName { get; set; }
    public string? BenchmarkerVersion { get; set; }
    public bool? IsLatestMagicOnion { get; }
    public string? MagicOnionVersion { get; }
    public string? GrpcNetVersion { get; }
    public string? MessagePackVersion { get; }
    public string? MemoryPackVersion { get; }
    public bool IsReleaseBuild { get; }
    public string FrameworkDescription { get; }
    public string OSDescription { get; }
    public Architecture OSArchitecture { get; }
    public Architecture ProcessArchitecture { get; }
    public string CpuModelName { get; }
    public bool IsServerGC { get; }
    public int ProcessorCount { get; }
    public bool IsAttached { get; }

    public ServerInformation(string machineName, string? benchmarkerVersion, bool? isLatestMagicOnion, string? magicOnionVersion, string? grpcNetVersion, string? messagePackVersion, string? memoryPackVersion, bool isReleaseBuild, string frameworkDescription, string osDescription, Architecture osArchitecture, Architecture processArchitecture, string cpuModelName, bool isServerGC, int processorCount, bool isAttached)
    {
        MachineName = machineName;
        BenchmarkerVersion = benchmarkerVersion;
        IsLatestMagicOnion = isLatestMagicOnion;
        MagicOnionVersion = magicOnionVersion;
        GrpcNetVersion = grpcNetVersion;
        MessagePackVersion = messagePackVersion;
        MemoryPackVersion = memoryPackVersion;
        IsReleaseBuild = isReleaseBuild;
        FrameworkDescription = frameworkDescription;
        OSDescription = osDescription;
        OSArchitecture = osArchitecture;
        ProcessArchitecture = processArchitecture;
        CpuModelName = cpuModelName;
        IsServerGC = isServerGC;
        ProcessorCount = processorCount;
        IsAttached = isAttached;
    }
}
