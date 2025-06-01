using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PulseRPC.Tracing;

/// <summary>
/// 链路追踪器实现
/// </summary>
public class Tracer : ITracer, IDisposable
{
    private readonly TracingOptions _options;
    private readonly ILogger<Tracer> _logger;
    private readonly ConcurrentDictionary<string, Span> _activeSpans = new();
    private readonly ThreadLocal<ISpan?> _currentSpan = new();
    private readonly Random _random = new();
    private bool _disposed;

    /// <summary>
    /// 活动源
    /// </summary>
    private static readonly ActivitySource ActivitySource = new("PulseRPC.Tracing");

    public Tracer(IOptions<TracingOptions> options, ILogger<Tracer> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 当前活跃跨度
    /// </summary>
    public ISpan? ActiveSpan => _currentSpan.Value;

    /// <summary>
    /// 开始新的跨度
    /// </summary>
    public ISpan StartSpan(string operationName, ISpan? parentSpan = null, IDictionary<string, object>? tags = null)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Tracer));

        // 决定是否采样
        var shouldSample = ShouldSample();
        if (!shouldSample && !_options.ForceTracing)
        {
            return new NoOpSpan(operationName);
        }

        var parentContext = parentSpan?.Context;
        var traceId = parentContext?.TraceId ?? GenerateTraceId();
        var spanId = GenerateSpanId();
        var parentSpanId = parentContext?.SpanId;

        var spanContext = new SpanContext(traceId, spanId, parentSpanId, shouldSample, new Dictionary<string, string>());
        var span = new Span(this, spanContext, operationName, DateTimeOffset.UtcNow, tags);

        _activeSpans.TryAdd(spanId, span);

        _logger.LogDebug("开始跨度: {OperationName}, TraceId: {TraceId}, SpanId: {SpanId}",
            operationName, traceId, spanId);

        return span;
    }

    /// <summary>
    /// 从活动开始跨度
    /// </summary>
    public ISpan StartSpan(Activity activity)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Tracer));

        var operationName = activity.OperationName;
        var traceId = activity.TraceId.ToString();
        var spanId = activity.SpanId.ToString();
        var parentSpanId = activity.ParentSpanId.ToString();

        var spanContext = new SpanContext(traceId, spanId, parentSpanId, true, new Dictionary<string, string>());
        var tags = activity.Tags.ToDictionary(t => t.Key, t => (object)t.Value!);
        var span = new Span(this, spanContext, operationName, activity.StartTimeUtc, tags);

        _activeSpans.TryAdd(spanId, span);

        _logger.LogDebug("从活动开始跨度: {OperationName}, TraceId: {TraceId}, SpanId: {SpanId}",
            operationName, traceId, spanId);

        return span;
    }

    /// <summary>
    /// 注入跨度上下文到载体
    /// </summary>
    public void Inject(ISpan span, IDictionary<string, string> carrier)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Tracer));

        if (span?.Context == null) return;

        carrier["traceparent"] = $"00-{span.Context.TraceId}-{span.Context.SpanId}-{(span.Context.IsSampled ? "01" : "00")}";

        if (span.Context.Baggage.Count > 0)
        {
            var baggage = string.Join(",", span.Context.Baggage.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            carrier["baggage"] = baggage;
        }

        _logger.LogDebug("注入跨度上下文: TraceId: {TraceId}, SpanId: {SpanId}",
            span.Context.TraceId, span.Context.SpanId);
    }

    /// <summary>
    /// 从载体提取跨度上下文
    /// </summary>
    public ISpanContext? Extract(IDictionary<string, string> carrier)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Tracer));

        if (!carrier.TryGetValue("traceparent", out var traceparent) || string.IsNullOrEmpty(traceparent))
            return null;

        try
        {
            // 解析 W3C Trace Context 格式: 00-traceId-spanId-flags
            var parts = traceparent.Split('-');
            if (parts.Length != 4) return null;

            var version = parts[0];
            var traceId = parts[1];
            var spanId = parts[2];
            var flags = parts[3];

            if (version != "00") return null; // 仅支持版本 00

            var isSampled = flags == "01";

            // 解析行李
            var baggage = new Dictionary<string, string>();
            if (carrier.TryGetValue("baggage", out var baggageHeader) && !string.IsNullOrEmpty(baggageHeader))
            {
                foreach (var item in baggageHeader.Split(','))
                {
                    var kvp = item.Split('=', 2);
                    if (kvp.Length == 2)
                    {
                        baggage[kvp[0].Trim()] = kvp[1].Trim();
                    }
                }
            }

            var context = new SpanContext(traceId, GenerateSpanId(), spanId, isSampled, baggage);

            _logger.LogDebug("提取跨度上下文: TraceId: {TraceId}, ParentSpanId: {ParentSpanId}",
                traceId, spanId);

            return context;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "提取跨度上下文失败: {Traceparent}", traceparent);
            return null;
        }
    }

    /// <summary>
    /// 设置当前活跃跨度
    /// </summary>
    public IScope SetActiveSpan(ISpan span)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Tracer));

        return new Scope(this, span);
    }

    /// <summary>
    /// 刷新追踪数据
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        _logger.LogDebug("刷新追踪数据");

        // 等待所有活跃跨度完成
        var activeTasks = _activeSpans.Values
            .Where(s => !s.IsFinished)
            .Select(async s =>
            {
                try
                {
                    // 给跨度一点时间自然完成
                    await Task.Delay(100, cancellationToken);
                }
                catch (OperationCanceledException) { }
            });

        try
        {
            await Task.WhenAll(activeTasks);
        }
        catch (OperationCanceledException) { }

        _logger.LogInformation("追踪数据刷新完成，活跃跨度数量: {Count}", _activeSpans.Count);
    }

    /// <summary>
    /// 关闭追踪器
    /// </summary>
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        _logger.LogInformation("关闭追踪器");

        await FlushAsync(cancellationToken);

        // 完成所有未完成的跨度
        foreach (var span in _activeSpans.Values.Where(s => !s.IsFinished))
        {
            span.Finish();
        }

        _activeSpans.Clear();
        _disposed = true;

        _logger.LogInformation("追踪器已关闭");
    }

    /// <summary>
    /// 设置当前跨度（内部使用）
    /// </summary>
    internal void SetCurrentSpan(ISpan? span)
    {
        _currentSpan.Value = span;
    }

    /// <summary>
    /// 移除活跃跨度
    /// </summary>
    internal void RemoveActiveSpan(string spanId)
    {
        _activeSpans.TryRemove(spanId, out _);
    }

    /// <summary>
    /// 判断是否应该采样
    /// </summary>
    private bool ShouldSample()
    {
        if (_options.SamplingRate >= 1.0) return true;
        if (_options.SamplingRate <= 0.0) return false;

        return _random.NextDouble() < _options.SamplingRate;
    }

    /// <summary>
    /// 生成跟踪ID
    /// </summary>
    private string GenerateTraceId()
    {
        var bytes = new byte[16];
        _random.NextBytes(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// 生成跨度ID
    /// </summary>
    private string GenerateSpanId()
    {
        var bytes = new byte[8];
        _random.NextBytes(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            CloseAsync().GetAwaiter().GetResult();
            _currentSpan.Dispose();
            ActivitySource.Dispose();
        }
    }
}

/// <summary>
/// 空操作跨度（用于未采样的情况）
/// </summary>
internal class NoOpSpan : ISpan
{
    public NoOpSpan(string operationName)
    {
        OperationName = operationName;
        Context = new SpanContext("", "", null, false, new Dictionary<string, string>());
        StartTime = DateTimeOffset.UtcNow;
    }

    public ISpanContext Context { get; }
    public string OperationName { get; }
    public DateTimeOffset StartTime { get; }
    public DateTimeOffset? FinishTime { get; private set; }
    public bool IsFinished { get; private set; }

    public void Dispose() => Finish();
    public void Finish(DateTimeOffset? finishTime = null)
    {
        if (!IsFinished)
        {
            FinishTime = finishTime ?? DateTimeOffset.UtcNow;
            IsFinished = true;
        }
    }

    public object? GetTag(string key) => null;
    public IReadOnlyDictionary<string, object> GetTags() => new Dictionary<string, object>();
    public IReadOnlyList<SpanLog> GetLogs() => Array.Empty<SpanLog>();
    public ISpan Log(string message, DateTimeOffset? timestamp = null, IDictionary<string, object>? fields = null) => this;
    public ISpan RecordException(Exception exception) => this;
    public ISpan SetStatus(SpanStatus status, string? description = null) => this;
    public ISpan SetTag(string key, object value) => this;
    public ISpan SetTags(IDictionary<string, object> tags) => this;
}
