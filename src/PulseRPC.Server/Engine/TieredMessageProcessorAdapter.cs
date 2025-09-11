using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Memory;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Transport;
using PulseRPC.Transport;

namespace PulseRPC.Server.Engine;

// MessageResponse定义已移至HighPerformanceMessageEngine.cs

/// <summary>
/// 适配器统计信息
/// </summary>
public class AdapterStatistics
{
    public string ConnectionId { get; set; } = "";

    // 适配器统计
    public long TotalAdapterMessages { get; set; }
    public long TotalConversions { get; set; }

    // TieredProcessor统计
    public PerformanceSummary? TieredProcessorSummary { get; set; }

    // 性能指标
    public double CurrentThroughput { get; set; }
    public TimeSpan AverageBatchProcessingTime { get; set; }
    public TimeSpan P95BatchProcessingTime { get; set; }
    public double L1BackpressureRate { get; set; }
    public double MessageErrorRate { get; set; }
}
