using System;
using System.Text.Json.Serialization;

namespace PulseRPC.Benchmark.Server.Configuration;

/// <summary>
/// 服务端配置
/// </summary>
public class ServerConfiguration
{
    /// <summary>
    /// 服务监听端口
    /// </summary>
    [JsonPropertyName("port")]
    public int Port { get; set; } = 8080;

    /// <summary>
    /// 指标监控端口
    /// </summary>
    [JsonPropertyName("metricsPort")]
    public int MetricsPort { get; set; } = 9090;

    /// <summary>
    /// 最大连接数
    /// </summary>
    [JsonPropertyName("maxConnections")]
    public int MaxConnections { get; init; } = 1000;

    /// <summary>
    /// 启用压缩
    /// </summary>
    [JsonPropertyName("enableCompression")]
    public bool EnableCompression { get; init; } = true;

    /// <summary>
    /// 保活间隔（秒）
    /// </summary>
    [JsonPropertyName("keepAliveIntervalSeconds")]
    public int KeepAliveIntervalSeconds { get; init; } = 30;

    /// <summary>
    /// 连接超时（秒）
    /// </summary>
    [JsonPropertyName("connectionTimeoutSeconds")]
    public int ConnectionTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// 启用详细日志
    /// </summary>
    [JsonPropertyName("enableVerboseLogging")]
    public bool EnableVerboseLogging { get; init; } = false;

    /// <summary>
    /// 工作线程数（0表示自动检测）
    /// </summary>
    [JsonPropertyName("workerThreads")]
    public int WorkerThreads { get; init; } = 0;

    /// <summary>
    /// 缓冲区大小（字节）
    /// </summary>
    [JsonPropertyName("bufferSize")]
    public int BufferSize { get; init; } = 64 * 1024; // 64KB

    /// <summary>
    /// 启用性能计数器
    /// </summary>
    [JsonPropertyName("enablePerformanceCounters")]
    public bool EnablePerformanceCounters { get; init; } = true;

    /// <summary>
    /// 健康检查间隔（秒）
    /// </summary>
    [JsonPropertyName("healthCheckIntervalSeconds")]
    public int HealthCheckIntervalSeconds { get; init; } = 10;

    /// <summary>
    /// 验证配置的有效性
    /// </summary>
    public void Validate()
    {
        if (Port is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(Port), "端口号必须在1-65535范围内");

        if (MetricsPort is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(MetricsPort), "指标端口号必须在1-65535范围内");

        if (Port == MetricsPort)
            throw new ArgumentException("服务端口和指标端口不能相同");

        if (MaxConnections <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxConnections), "最大连接数必须大于0");

        if (KeepAliveIntervalSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(KeepAliveIntervalSeconds), "保活间隔必须大于0");

        if (ConnectionTimeoutSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(ConnectionTimeoutSeconds), "连接超时必须大于0");

        if (BufferSize < 1024)
            throw new ArgumentOutOfRangeException(nameof(BufferSize), "缓冲区大小不能小于1KB");

        if (HealthCheckIntervalSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(HealthCheckIntervalSeconds), "健康检查间隔必须大于0");
    }

    /// <summary>
    /// 创建默认配置
    /// </summary>
    public static ServerConfiguration CreateDefault()
    {
        return new ServerConfiguration();
    }

    /// <summary>
    /// 创建开发环境配置
    /// </summary>
    public static ServerConfiguration CreateDevelopment()
    {
        return new ServerConfiguration
        {
            Port = 8080,
            MetricsPort = 9090,
            MaxConnections = 100,
            EnableCompression = false,
            EnableVerboseLogging = true,
            HealthCheckIntervalSeconds = 5
        };
    }

    /// <summary>
    /// 创建生产环境配置
    /// </summary>
    public static ServerConfiguration CreateProduction()
    {
        return new ServerConfiguration
        {
            Port = 8080,
            MetricsPort = 9090,
            MaxConnections = 10000,
            EnableCompression = true,
            EnableVerboseLogging = false,
            WorkerThreads = Environment.ProcessorCount * 2,
            BufferSize = 128 * 1024 // 128KB
        };
    }
}
