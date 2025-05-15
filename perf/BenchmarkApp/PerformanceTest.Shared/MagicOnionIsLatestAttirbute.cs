namespace PerformanceTest.Shared;

public class PulseRPCIsLatestAttirbute(string isLatest) : Attribute
{
    public bool IsLatest { get; } = string.IsNullOrWhiteSpace(isLatest);
}
