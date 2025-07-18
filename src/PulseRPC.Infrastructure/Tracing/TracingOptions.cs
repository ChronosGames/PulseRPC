namespace PulseRPC.Tracing;

/// <summary>
/// 链路追踪配置选项
/// </summary>
public class TracingOptions
{
    /// <summary>
    /// 是否启用链路追踪
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 采样率 (0.0 - 1.0)
    /// </summary>
    public double SamplingRate { get; set; } = 0.1;

    /// <summary>
    /// 是否强制追踪（忽略采样率）
    /// </summary>
    public bool ForceTracing { get; set; } = false;

    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; set; } = "PulseRPC.Service";

    /// <summary>
    /// 服务版本
    /// </summary>
    public string ServiceVersion { get; set; } = "1.0.0";

    /// <summary>
    /// 环境名称
    /// </summary>
    public string Environment { get; set; } = "development";

    /// <summary>
    /// 最大跨度数量
    /// </summary>
    public int MaxSpansPerTrace { get; set; } = 1000;

    /// <summary>
    /// 最大跨度属性数量
    /// </summary>
    public int MaxSpanAttributes { get; set; } = 128;

    /// <summary>
    /// 最大跨度事件数量
    /// </summary>
    public int MaxSpanEvents { get; set; } = 128;

    /// <summary>
    /// 最大跨度链接数量
    /// </summary>
    public int MaxSpanLinks { get; set; } = 128;

    /// <summary>
    /// 跨度过期时间
    /// </summary>
    public TimeSpan SpanExpirationTime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 是否跟踪RPC调用
    /// </summary>
    public bool TraceRpcCalls { get; set; } = true;

    /// <summary>
    /// 是否跟踪数据库操作
    /// </summary>
    public bool TraceDatabaseOperations { get; set; } = false;

    /// <summary>
    /// 是否跟踪HTTP请求
    /// </summary>
    public bool TraceHttpRequests { get; set; } = true;

    /// <summary>
    /// 是否跟踪消息队列操作
    /// </summary>
    public bool TraceMessageQueue { get; set; } = false;

    /// <summary>
    /// 是否记录异常
    /// </summary>
    public bool RecordExceptions { get; set; } = true;

    /// <summary>
    /// 是否记录RPC参数
    /// </summary>
    public bool RecordRpcArguments { get; set; } = false;

    /// <summary>
    /// 是否记录RPC返回值
    /// </summary>
    public bool RecordRpcReturnValues { get; set; } = false;

    /// <summary>
    /// 最大参数长度
    /// </summary>
    public int MaxArgumentLength { get; set; } = 1024;

    /// <summary>
    /// 导出器配置
    /// </summary>
    public TracingExporterOptions Exporter { get; set; } = new();

    /// <summary>
    /// 过滤器配置
    /// </summary>
    public TracingFilterOptions Filter { get; set; } = new();

    /// <summary>
    /// 资源标签
    /// </summary>
    public Dictionary<string, string> ResourceTags { get; set; } = new();

    /// <summary>
    /// 默认跨度标签
    /// </summary>
    public Dictionary<string, string> DefaultSpanTags { get; set; } = new();

    /// <summary>
    /// 忽略的操作名称
    /// </summary>
    public HashSet<string> IgnoredOperations { get; set; } = new();

    /// <summary>
    /// 忽略的用户代理
    /// </summary>
    public HashSet<string> IgnoredUserAgents { get; set; } = new();

    /// <summary>
    /// 批处理配置
    /// </summary>
    public TracingBatchOptions Batch { get; set; } = new();
}

/// <summary>
/// 追踪导出器配置选项
/// </summary>
public class TracingExporterOptions
{
    /// <summary>
    /// 导出器类型
    /// </summary>
    public TracingExporterType Type { get; set; } = TracingExporterType.Console;

    /// <summary>
    /// 导出端点
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// API密钥
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// 超时时间
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 压缩类型
    /// </summary>
    public CompressionType Compression { get; set; } = CompressionType.None;

    /// <summary>
    /// 请求头
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>
    /// Jaeger配置
    /// </summary>
    public JaegerExporterOptions? Jaeger { get; set; }

    /// <summary>
    /// Zipkin配置
    /// </summary>
    public ZipkinExporterOptions? Zipkin { get; set; }

    /// <summary>
    /// OTLP配置
    /// </summary>
    public OtlpExporterOptions? Otlp { get; set; }
}

/// <summary>
/// 追踪过滤器配置选项
/// </summary>
public class TracingFilterOptions
{
    /// <summary>
    /// 是否启用过滤器
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 最小持续时间阈值
    /// </summary>
    public TimeSpan MinDurationThreshold { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// 最大持续时间阈值
    /// </summary>
    public TimeSpan MaxDurationThreshold { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 是否过滤成功的操作
    /// </summary>
    public bool FilterSuccessfulOperations { get; set; } = false;

    /// <summary>
    /// 是否过滤错误的操作
    /// </summary>
    public bool FilterErrorOperations { get; set; } = false;

    /// <summary>
    /// 白名单操作
    /// </summary>
    public HashSet<string> AllowedOperations { get; set; } = new();

    /// <summary>
    /// 黑名单操作
    /// </summary>
    public HashSet<string> BlockedOperations { get; set; } = new();

    /// <summary>
    /// 白名单标签
    /// </summary>
    public Dictionary<string, HashSet<string>> AllowedTags { get; set; } = new();

    /// <summary>
    /// 黑名单标签
    /// </summary>
    public Dictionary<string, HashSet<string>> BlockedTags { get; set; } = new();
}

/// <summary>
/// 追踪批处理配置选项
/// </summary>
public class TracingBatchOptions
{
    /// <summary>
    /// 是否启用批处理
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 批处理大小
    /// </summary>
    public int BatchSize { get; set; } = 512;

    /// <summary>
    /// 批处理超时
    /// </summary>
    public TimeSpan BatchTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 最大队列大小
    /// </summary>
    public int MaxQueueSize { get; set; } = 2048;

    /// <summary>
    /// 导出超时
    /// </summary>
    public TimeSpan ExportTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Jaeger导出器配置选项
/// </summary>
public class JaegerExporterOptions
{
    /// <summary>
    /// Agent主机
    /// </summary>
    public string AgentHost { get; set; } = "localhost";

    /// <summary>
    /// Agent端口
    /// </summary>
    public int AgentPort { get; set; } = 6831;

    /// <summary>
    /// 收集器端点
    /// </summary>
    public string? CollectorEndpoint { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// 密码
    /// </summary>
    public string? Password { get; set; }
}

/// <summary>
/// Zipkin导出器配置选项
/// </summary>
public class ZipkinExporterOptions
{
    /// <summary>
    /// 端点URL
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:9411/api/v2/spans";

    /// <summary>
    /// 是否使用短跨度名称
    /// </summary>
    public bool UseShortTraceIds { get; set; } = false;
}

/// <summary>
/// OTLP导出器配置选项
/// </summary>
public class OtlpExporterOptions
{
    /// <summary>
    /// 端点URL
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:4317";

    /// <summary>
    /// 协议类型
    /// </summary>
    public OtlpProtocol Protocol { get; set; } = OtlpProtocol.Grpc;

    /// <summary>
    /// 是否使用TLS
    /// </summary>
    public bool UseTls { get; set; } = false;

    /// <summary>
    /// 证书文件路径
    /// </summary>
    public string? CertificatePath { get; set; }
}

/// <summary>
/// 追踪导出器类型
/// </summary>
public enum TracingExporterType
{
    /// <summary>
    /// 控制台导出器
    /// </summary>
    Console,

    /// <summary>
    /// 文件导出器
    /// </summary>
    File,

    /// <summary>
    /// Jaeger导出器
    /// </summary>
    Jaeger,

    /// <summary>
    /// Zipkin导出器
    /// </summary>
    Zipkin,

    /// <summary>
    /// OTLP导出器
    /// </summary>
    Otlp,

    /// <summary>
    /// 自定义导出器
    /// </summary>
    Custom
}

/// <summary>
/// 压缩类型
/// </summary>
public enum CompressionType
{
    /// <summary>
    /// 无压缩
    /// </summary>
    None,

    /// <summary>
    /// Gzip压缩
    /// </summary>
    Gzip,

    /// <summary>
    /// Brotli压缩
    /// </summary>
    Brotli
}

/// <summary>
/// OTLP协议类型
/// </summary>
public enum OtlpProtocol
{
    /// <summary>
    /// gRPC协议
    /// </summary>
    Grpc,

    /// <summary>
    /// HTTP/protobuf协议
    /// </summary>
    HttpProtobuf,

    /// <summary>
    /// HTTP/JSON协议
    /// </summary>
    HttpJson
}
