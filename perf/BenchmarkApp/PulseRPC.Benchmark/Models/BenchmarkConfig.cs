namespace PulseRPC.Benchmark.Models;

/// <summary>
/// 基准测试配置
/// </summary>
public class BenchmarkConfig
{
    /// <summary>
    /// 服务器主机地址
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// TCP端口
    /// </summary>
    public int TcpPort { get; set; } = 12345;

    /// <summary>
    /// 测试迭代次数
    /// </summary>
    public int Iterations { get; set; } = 1000;

    /// <summary>
    /// 测试持续时间（秒）
    /// </summary>
    public int DurationSeconds { get; set; } = 30;

    /// <summary>
    /// 并发连接数
    /// </summary>
    public int Connections { get; set; } = 1;

    /// <summary>
    /// 消息大小（字节）
    /// </summary>
    public int MessageSize { get; set; } = 1024;

    /// <summary>
    /// 预热迭代次数
    /// </summary>
    public int WarmupIterations { get; set; } = 100;

    /// <summary>
    /// 输出文件路径（JSON）
    /// </summary>
    public string? OutputFile { get; set; }
}
