using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace PulseRPC.Tracing.Extensions;

/// <summary>
/// 链路追踪系统依赖注入扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加 PulseRPC 链路追踪
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcTracing(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 注册配置选项
        services.Configure<TracingOptions>(configuration.GetSection("PulseRPC:Tracing"));
        return AddTracingCore(services);
    }

    /// <summary>
    /// 添加 PulseRPC 链路追踪 (使用配置回调)
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcTracing(
        this IServiceCollection services,
        Action<TracingOptions> configureOptions)
    {
        services.Configure(configureOptions);
        return AddTracingCore(services);
    }

    /// <summary>
    /// 添加自定义追踪器
    /// </summary>
    /// <typeparam name="TTracer">追踪器类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="lifetime">服务生命周期</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddCustomTracer<TTracer>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TTracer : class, ITracer
    {
        services.Add(new ServiceDescriptor(typeof(ITracer), typeof(TTracer), lifetime));
        return services;
    }

    /// <summary>
    /// 配置采样率
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="samplingRate">采样率 (0.0 - 1.0)</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureTracingSampling(
        this IServiceCollection services,
        double samplingRate)
    {
        services.Configure<TracingOptions>(options =>
        {
            options.SamplingRate = Math.Clamp(samplingRate, 0.0, 1.0);
        });

        return services;
    }

    /// <summary>
    /// 启用强制追踪
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection EnableForceTracing(this IServiceCollection services)
    {
        services.Configure<TracingOptions>(options =>
        {
            options.ForceTracing = true;
            options.SamplingRate = 1.0;
        });

        return services;
    }

    /// <summary>
    /// 配置服务信息
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="serviceName">服务名称</param>
    /// <param name="serviceVersion">服务版本</param>
    /// <param name="environment">环境名称</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureServiceInfo(
        this IServiceCollection services,
        string serviceName,
        string? serviceVersion = null,
        string? environment = null)
    {
        services.Configure<TracingOptions>(options =>
        {
            options.ServiceName = serviceName;
            if (serviceVersion != null)
                options.ServiceVersion = serviceVersion;
            if (environment != null)
                options.Environment = environment;
        });

        return services;
    }

    /// <summary>
    /// 配置控制台导出器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection UseConsoleExporter(this IServiceCollection services)
    {
        services.Configure<TracingOptions>(options =>
        {
            options.Exporter.Type = TracingExporterType.Console;
        });

        return services;
    }

    /// <summary>
    /// 配置Jaeger导出器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="agentHost">Agent主机</param>
    /// <param name="agentPort">Agent端口</param>
    /// <param name="collectorEndpoint">收集器端点</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection UseJaegerExporter(
        this IServiceCollection services,
        string agentHost = "localhost",
        int agentPort = 6831,
        string? collectorEndpoint = null)
    {
        services.Configure<TracingOptions>(options =>
        {
            options.Exporter.Type = TracingExporterType.Jaeger;
            options.Exporter.Jaeger = new JaegerExporterOptions
            {
                AgentHost = agentHost,
                AgentPort = agentPort,
                CollectorEndpoint = collectorEndpoint
            };
        });

        return services;
    }

    /// <summary>
    /// 配置Zipkin导出器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="endpoint">端点URL</param>
    /// <param name="useShortTraceIds">是否使用短跟踪ID</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection UseZipkinExporter(
        this IServiceCollection services,
        string endpoint = "http://localhost:9411/api/v2/spans",
        bool useShortTraceIds = false)
    {
        services.Configure<TracingOptions>(options =>
        {
            options.Exporter.Type = TracingExporterType.Zipkin;
            options.Exporter.Zipkin = new ZipkinExporterOptions
            {
                Endpoint = endpoint,
                UseShortTraceIds = useShortTraceIds
            };
        });

        return services;
    }

    /// <summary>
    /// 配置OTLP导出器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="endpoint">端点URL</param>
    /// <param name="protocol">协议类型</param>
    /// <param name="useTls">是否使用TLS</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection UseOtlpExporter(
        this IServiceCollection services,
        string endpoint = "http://localhost:4317",
        OtlpProtocol protocol = OtlpProtocol.Grpc,
        bool useTls = false)
    {
        services.Configure<TracingOptions>(options =>
        {
            options.Exporter.Type = TracingExporterType.Otlp;
            options.Exporter.Otlp = new OtlpExporterOptions
            {
                Endpoint = endpoint,
                Protocol = protocol,
                UseTls = useTls
            };
        });

        return services;
    }

    /// <summary>
    /// 配置跟踪范围
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="traceRpcCalls">是否跟踪RPC调用</param>
    /// <param name="traceHttpRequests">是否跟踪HTTP请求</param>
    /// <param name="traceDatabaseOperations">是否跟踪数据库操作</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureTracingScope(
        this IServiceCollection services,
        bool traceRpcCalls = true,
        bool traceHttpRequests = true,
        bool traceDatabaseOperations = false)
    {
        services.Configure<TracingOptions>(options =>
        {
            options.TraceRpcCalls = traceRpcCalls;
            options.TraceHttpRequests = traceHttpRequests;
            options.TraceDatabaseOperations = traceDatabaseOperations;
        });

        return services;
    }

    /// <summary>
    /// 配置记录选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="recordExceptions">是否记录异常</param>
    /// <param name="recordArguments">是否记录参数</param>
    /// <param name="recordReturnValues">是否记录返回值</param>
    /// <param name="maxArgumentLength">最大参数长度</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureRecording(
        this IServiceCollection services,
        bool recordExceptions = true,
        bool recordArguments = false,
        bool recordReturnValues = false,
        int maxArgumentLength = 1024)
    {
        services.Configure<TracingOptions>(options =>
        {
            options.RecordExceptions = recordExceptions;
            options.RecordRpcArguments = recordArguments;
            options.RecordRpcReturnValues = recordReturnValues;
            options.MaxArgumentLength = maxArgumentLength;
        });

        return services;
    }

    /// <summary>
    /// 添加资源标签
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="tags">标签</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddResourceTags(
        this IServiceCollection services,
        IDictionary<string, string> tags)
    {
        services.Configure<TracingOptions>(options =>
        {
            foreach (var tag in tags)
            {
                options.ResourceTags[tag.Key] = tag.Value;
            }
        });

        return services;
    }

    /// <summary>
    /// 添加默认跨度标签
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="tags">标签</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddDefaultSpanTags(
        this IServiceCollection services,
        IDictionary<string, string> tags)
    {
        services.Configure<TracingOptions>(options =>
        {
            foreach (var tag in tags)
            {
                options.DefaultSpanTags[tag.Key] = tag.Value;
            }
        });

        return services;
    }

    /// <summary>
    /// 忽略操作
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="operations">操作名称</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection IgnoreOperations(
        this IServiceCollection services,
        params string[] operations)
    {
        services.Configure<TracingOptions>(options =>
        {
            foreach (var operation in operations)
            {
                options.IgnoredOperations.Add(operation);
            }
        });

        return services;
    }

    /// <summary>
    /// 配置批处理
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="batchSize">批处理大小</param>
    /// <param name="batchTimeout">批处理超时</param>
    /// <param name="maxQueueSize">最大队列大小</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureBatching(
        this IServiceCollection services,
        int batchSize = 512,
        TimeSpan? batchTimeout = null,
        int maxQueueSize = 2048)
    {
        services.Configure<TracingOptions>(options =>
        {
            options.Batch.BatchSize = batchSize;
            if (batchTimeout.HasValue)
                options.Batch.BatchTimeout = batchTimeout.Value;
            options.Batch.MaxQueueSize = maxQueueSize;
        });

        return services;
    }

    /// <summary>
    /// 获取追踪器
    /// </summary>
    /// <param name="serviceProvider">服务提供者</param>
    /// <returns>追踪器</returns>
    public static ITracer GetTracer(this IServiceProvider serviceProvider)
    {
        return serviceProvider.GetRequiredService<ITracer>();
    }

    /// <summary>
    /// 添加追踪核心服务
    /// </summary>
    private static IServiceCollection AddTracingCore(IServiceCollection services)
    {
        // 注册追踪器
        services.TryAddSingleton<ITracer, Tracer>();

        // 注册活动源
        services.TryAddSingleton(_ => new ActivitySource("PulseRPC.Tracing"));

        return services;
    }
}

/// <summary>
/// 追踪扩展方法
/// </summary>
public static class TracingExtensions
{
    /// <summary>
    /// 开始RPC跨度
    /// </summary>
    /// <param name="tracer">追踪器</param>
    /// <param name="serviceName">服务名称</param>
    /// <param name="methodName">方法名称</param>
    /// <param name="parentSpan">父跨度</param>
    /// <returns>跨度</returns>
    public static ISpan StartRpcSpan(
        this ITracer tracer,
        string serviceName,
        string methodName,
        ISpan? parentSpan = null)
    {
        var operationName = $"{serviceName}/{methodName}";
        var tags = new Dictionary<string, object>
        {
            ["rpc.service"] = serviceName,
            ["rpc.method"] = methodName,
            ["rpc.system"] = "pulserpc"
        };

        return tracer.StartSpan(operationName, parentSpan, tags);
    }

    /// <summary>
    /// 开始HTTP跨度
    /// </summary>
    /// <param name="tracer">追踪器</param>
    /// <param name="method">HTTP方法</param>
    /// <param name="url">URL</param>
    /// <param name="parentSpan">父跨度</param>
    /// <returns>跨度</returns>
    public static ISpan StartHttpSpan(
        this ITracer tracer,
        string method,
        string url,
        ISpan? parentSpan = null)
    {
        var operationName = $"HTTP {method}";
        var tags = new Dictionary<string, object>
        {
            ["http.method"] = method,
            ["http.url"] = url,
            ["component"] = "http-client"
        };

        return tracer.StartSpan(operationName, parentSpan, tags);
    }

    /// <summary>
    /// 开始数据库跨度
    /// </summary>
    /// <param name="tracer">追踪器</param>
    /// <param name="operation">操作类型</param>
    /// <param name="table">表名</param>
    /// <param name="database">数据库名</param>
    /// <param name="parentSpan">父跨度</param>
    /// <returns>跨度</returns>
    public static ISpan StartDatabaseSpan(
        this ITracer tracer,
        string operation,
        string table,
        string? database = null,
        ISpan? parentSpan = null)
    {
        var operationName = $"db.{operation}";
        var tags = new Dictionary<string, object>
        {
            ["db.operation"] = operation,
            ["db.table"] = table,
            ["component"] = "database-client"
        };

        if (!string.IsNullOrEmpty(database))
        {
            tags["db.name"] = database;
        }

        return tracer.StartSpan(operationName, parentSpan, tags);
    }

    /// <summary>
    /// 使用跨度执行操作
    /// </summary>
    /// <param name="tracer">追踪器</param>
    /// <param name="operationName">操作名称</param>
    /// <param name="action">操作</param>
    /// <param name="parentSpan">父跨度</param>
    /// <param name="tags">标签</param>
    public static void WithSpan(
        this ITracer tracer,
        string operationName,
        Action action,
        ISpan? parentSpan = null,
        IDictionary<string, object>? tags = null)
    {
        using var span = tracer.StartSpan(operationName, parentSpan, tags);
        using var scope = tracer.SetActiveSpan(span);

        try
        {
            action();
            span.SetStatus(SpanStatus.Ok);
        }
        catch (Exception ex)
        {
            span.RecordException(ex);
            throw;
        }
    }

    /// <summary>
    /// 使用跨度执行异步操作
    /// </summary>
    /// <param name="tracer">追踪器</param>
    /// <param name="operationName">操作名称</param>
    /// <param name="asyncAction">异步操作</param>
    /// <param name="parentSpan">父跨度</param>
    /// <param name="tags">标签</param>
    public static async Task WithSpanAsync(
        this ITracer tracer,
        string operationName,
        Func<Task> asyncAction,
        ISpan? parentSpan = null,
        IDictionary<string, object>? tags = null)
    {
        using var span = tracer.StartSpan(operationName, parentSpan, tags);
        using var scope = tracer.SetActiveSpan(span);

        try
        {
            await asyncAction();
            span.SetStatus(SpanStatus.Ok);
        }
        catch (Exception ex)
        {
            span.RecordException(ex);
            throw;
        }
    }

    /// <summary>
    /// 使用跨度执行有返回值的操作
    /// </summary>
    /// <typeparam name="T">返回值类型</typeparam>
    /// <param name="tracer">追踪器</param>
    /// <param name="operationName">操作名称</param>
    /// <param name="func">操作</param>
    /// <param name="parentSpan">父跨度</param>
    /// <param name="tags">标签</param>
    /// <returns>操作结果</returns>
    public static T WithSpan<T>(
        this ITracer tracer,
        string operationName,
        Func<T> func,
        ISpan? parentSpan = null,
        IDictionary<string, object>? tags = null)
    {
        using var span = tracer.StartSpan(operationName, parentSpan, tags);
        using var scope = tracer.SetActiveSpan(span);

        try
        {
            var result = func();
            span.SetStatus(SpanStatus.Ok);
            return result;
        }
        catch (Exception ex)
        {
            span.RecordException(ex);
            throw;
        }
    }

    /// <summary>
    /// 使用跨度执行有返回值的异步操作
    /// </summary>
    /// <typeparam name="T">返回值类型</typeparam>
    /// <param name="tracer">追踪器</param>
    /// <param name="operationName">操作名称</param>
    /// <param name="asyncFunc">异步操作</param>
    /// <param name="parentSpan">父跨度</param>
    /// <param name="tags">标签</param>
    /// <returns>操作结果</returns>
    public static async Task<T> WithSpanAsync<T>(
        this ITracer tracer,
        string operationName,
        Func<Task<T>> asyncFunc,
        ISpan? parentSpan = null,
        IDictionary<string, object>? tags = null)
    {
        using var span = tracer.StartSpan(operationName, parentSpan, tags);
        using var scope = tracer.SetActiveSpan(span);

        try
        {
            var result = await asyncFunc();
            span.SetStatus(SpanStatus.Ok);
            return result;
        }
        catch (Exception ex)
        {
            span.RecordException(ex);
            throw;
        }
    }
}
