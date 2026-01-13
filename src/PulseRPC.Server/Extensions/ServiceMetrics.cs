using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseRPC.Server.Hubs; using PulseRPC.Server.Services; using PulseRPC.Server.Transport;
using PulseRPC.Server.Services.Management;
using PulseRPC.Server.Services;

namespace PulseRPC.Server.Extensions;

/// <summary>
/// PulseRPC 服务指标收集器 - 使用 System.Diagnostics.Metrics 实现
/// </summary>
/// <remarks>
/// <para>
/// <strong>与 OpenTelemetry 集成</strong>：
/// </para>
/// <para>
/// 此实现使用 .NET 内置的 <see cref="System.Diagnostics.Metrics"/> API，
/// 该 API 与 OpenTelemetry .NET SDK 完全兼容。只需添加 OpenTelemetry 包并配置
/// <c>AddMeter("PulseRPC.Server")</c> 即可导出指标。
/// </para>
/// <para>
/// <strong>配置示例</strong>：
/// </para>
/// <code>
/// // Program.cs
/// builder.Services.AddOpenTelemetry()
///     .WithMetrics(metrics =>
///     {
///         metrics.AddMeter("PulseRPC.Server");
///         metrics.AddPrometheusExporter();
///     });
/// </code>
/// <para>
/// <strong>暴露的指标</strong>：
/// </para>
/// <list type="bullet">
/// <item><description><c>pulserpc_service_instances_total</c> - 活跃服务实例数</description></item>
/// <item><description><c>pulserpc_service_created_total</c> - 创建的服务实例总数</description></item>
/// <item><description><c>pulserpc_service_disposed_total</c> - 释放的服务实例总数</description></item>
/// <item><description><c>pulserpc_message_queue_depth</c> - 消息队列深度</description></item>
/// <item><description><c>pulserpc_request_duration_seconds</c> - 请求处理时长</description></item>
/// <item><description><c>pulserpc_requests_total</c> - 请求总数</description></item>
/// </list>
/// </remarks>
public sealed class ServiceMetrics : IDisposable
{
    /// <summary>
    /// Meter 名称（用于 OpenTelemetry 配置）
    /// </summary>
    public const string MeterName = "PulseRPC.Server";

    private readonly Meter _meter;
    private readonly UnifiedServiceManager? _serviceManager;
    private readonly ServiceInstanceEvictor? _evictor;

    // ═══════════════════════════════════════════════════════════════════════════
    // 计数器 (Counters) - 单调递增
    // ═══════════════════════════════════════════════════════════════════════════

    private readonly Counter<long> _servicesCreatedCounter;
    private readonly Counter<long> _servicesDisposedCounter;
    private readonly Counter<long> _requestsCounter;
    private readonly Counter<long> _requestErrorsCounter;
    private readonly Counter<long> _messagesEnqueuedCounter;
    private readonly Counter<long> _messagesProcessedCounter;
    private readonly Counter<long> _raceConditionsAvoidedCounter;
    private readonly Counter<long> _instancesEvictedCounter;

    // ═══════════════════════════════════════════════════════════════════════════
    // 直方图 (Histograms) - 用于延迟分布
    // ═══════════════════════════════════════════════════════════════════════════

    private readonly Histogram<double> _requestDurationHistogram;
    private readonly Histogram<double> _messageProcessingDurationHistogram;

    // ═══════════════════════════════════════════════════════════════════════════
    // 仪表 (Gauges) - 可上下波动的值
    // ═══════════════════════════════════════════════════════════════════════════

    private readonly ObservableGauge<int> _activeInstancesGauge;
    private readonly ObservableGauge<int> _pendingCreationsGauge;
    private readonly ObservableGauge<int> _registeredTypesGauge;

    /// <summary>
    /// 创建 ServiceMetrics 实例
    /// </summary>
    /// <param name="serviceManager">服务管理器（可选，用于获取实时统计）</param>
    /// <param name="evictor">清理器（可选，用于获取清理统计）</param>
    public ServiceMetrics(
        UnifiedServiceManager? serviceManager = null,
        ServiceInstanceEvictor? evictor = null)
    {
        _serviceManager = serviceManager;
        _evictor = evictor;

        _meter = new Meter(MeterName, "1.0.0");

        // ═══════════════════════════════════════════════════════════════════════
        // 初始化计数器
        // ═══════════════════════════════════════════════════════════════════════

        _servicesCreatedCounter = _meter.CreateCounter<long>(
            name: "pulserpc_service_created_total",
            unit: "{service}",
            description: "Total number of service instances created");

        _servicesDisposedCounter = _meter.CreateCounter<long>(
            name: "pulserpc_service_disposed_total",
            unit: "{service}",
            description: "Total number of service instances disposed");

        _requestsCounter = _meter.CreateCounter<long>(
            name: "pulserpc_requests_total",
            unit: "{request}",
            description: "Total number of RPC requests processed");

        _requestErrorsCounter = _meter.CreateCounter<long>(
            name: "pulserpc_request_errors_total",
            unit: "{error}",
            description: "Total number of RPC request errors");

        _messagesEnqueuedCounter = _meter.CreateCounter<long>(
            name: "pulserpc_messages_enqueued_total",
            unit: "{message}",
            description: "Total number of messages enqueued to service queues");

        _messagesProcessedCounter = _meter.CreateCounter<long>(
            name: "pulserpc_messages_processed_total",
            unit: "{message}",
            description: "Total number of messages processed from service queues");

        _raceConditionsAvoidedCounter = _meter.CreateCounter<long>(
            name: "pulserpc_race_conditions_avoided_total",
            unit: "{occurrence}",
            description: "Total number of race conditions avoided during service creation");

        _instancesEvictedCounter = _meter.CreateCounter<long>(
            name: "pulserpc_instances_evicted_total",
            unit: "{service}",
            description: "Total number of service instances evicted due to idle timeout or capacity limits");

        // ═══════════════════════════════════════════════════════════════════════
        // 初始化直方图
        // ═══════════════════════════════════════════════════════════════════════

        _requestDurationHistogram = _meter.CreateHistogram<double>(
            name: "pulserpc_request_duration_seconds",
            unit: "s",
            description: "Duration of RPC request processing in seconds");

        _messageProcessingDurationHistogram = _meter.CreateHistogram<double>(
            name: "pulserpc_message_processing_duration_seconds",
            unit: "s",
            description: "Duration of message processing in service queue");

        // ═══════════════════════════════════════════════════════════════════════
        // 初始化可观测仪表（从 ServiceManager 获取实时数据）
        // ═══════════════════════════════════════════════════════════════════════

        _activeInstancesGauge = _meter.CreateObservableGauge(
            name: "pulserpc_service_instances_active",
            observeValue: () => _serviceManager?.GetStatistics().ActiveInstances ?? 0,
            unit: "{service}",
            description: "Current number of active service instances");

        _pendingCreationsGauge = _meter.CreateObservableGauge(
            name: "pulserpc_service_creations_pending",
            observeValue: () => _serviceManager?.GetStatistics().PendingCreations ?? 0,
            unit: "{service}",
            description: "Current number of pending service creations");

        _registeredTypesGauge = _meter.CreateObservableGauge(
            name: "pulserpc_service_types_registered",
            observeValue: () => _serviceManager?.GetStatistics().RegisteredTypes ?? 0,
            unit: "{type}",
            description: "Number of registered service types");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 记录方法
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 记录服务创建
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    public void RecordServiceCreated(string serviceType)
    {
        _servicesCreatedCounter.Add(1, new KeyValuePair<string, object?>("service_type", serviceType));
    }

    /// <summary>
    /// 记录服务释放
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    public void RecordServiceDisposed(string serviceType)
    {
        _servicesDisposedCounter.Add(1, new KeyValuePair<string, object?>("service_type", serviceType));
    }

    /// <summary>
    /// 记录请求处理
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    /// <param name="methodName">方法名</param>
    /// <param name="duration">处理时长</param>
    /// <param name="success">是否成功</param>
    public void RecordRequest(string serviceType, string methodName, TimeSpan duration, bool success)
    {
        var tags = new TagList
        {
            { "service_type", serviceType },
            { "method", methodName },
            { "status", success ? "success" : "error" }
        };

        _requestsCounter.Add(1, tags);

        if (!success)
        {
            _requestErrorsCounter.Add(1, tags);
        }

        _requestDurationHistogram.Record(duration.TotalSeconds, tags);
    }

    /// <summary>
    /// 记录消息入队
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    public void RecordMessageEnqueued(string serviceType)
    {
        _messagesEnqueuedCounter.Add(1, new KeyValuePair<string, object?>("service_type", serviceType));
    }

    /// <summary>
    /// 记录消息处理完成
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    /// <param name="processingTime">处理时长</param>
    public void RecordMessageProcessed(string serviceType, TimeSpan processingTime)
    {
        var tag = new KeyValuePair<string, object?>("service_type", serviceType);
        _messagesProcessedCounter.Add(1, tag);
        _messageProcessingDurationHistogram.Record(processingTime.TotalSeconds, tag);
    }

    /// <summary>
    /// 记录避免的竞态条件
    /// </summary>
    public void RecordRaceConditionAvoided()
    {
        _raceConditionsAvoidedCounter.Add(1);
    }

    /// <summary>
    /// 记录实例被清理
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    /// <param name="reason">清理原因</param>
    public void RecordInstanceEvicted(string serviceType, string reason)
    {
        _instancesEvictedCounter.Add(1,
            new KeyValuePair<string, object?>("service_type", serviceType),
            new KeyValuePair<string, object?>("reason", reason));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _meter.Dispose();
    }
}

/// <summary>
/// ServiceMetrics DI 扩展方法
/// </summary>
public static class ServiceMetricsExtensions
{
    /// <summary>
    /// 添加 PulseRPC 服务指标收集器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合，支持链式调用</returns>
    /// <remarks>
    /// <para>
    /// 调用此方法后，可以通过 OpenTelemetry 导出指标：
    /// </para>
    /// <code>
    /// services.AddPulseServiceMetrics();
    ///
    /// services.AddOpenTelemetry()
    ///     .WithMetrics(metrics =>
    ///     {
    ///         metrics.AddMeter(ServiceMetrics.MeterName);
    ///         metrics.AddPrometheusExporter();
    ///     });
    /// </code>
    /// </remarks>
    public static IServiceCollection AddPulseServiceMetrics(
        this IServiceCollection services)
    {
        services.AddSingleton<ServiceMetrics>();
        return services;
    }
}

