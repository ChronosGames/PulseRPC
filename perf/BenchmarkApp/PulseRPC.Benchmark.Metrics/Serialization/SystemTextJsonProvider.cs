using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Metrics.Abstractions;

namespace PulseRPC.Benchmark.Metrics.Serialization;

/// <summary>
/// System.Text.Json 序列化提供程序实现
/// </summary>
public class SystemTextJsonProvider : IJsonSerializationProvider
{
    private readonly JsonSerializerOptions _options;
    private readonly ILogger<SystemTextJsonProvider>? _logger;
    private readonly JsonSerializationOptions _configuration;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="configuration">序列化配置</param>
    /// <param name="logger">日志记录器</param>
    public SystemTextJsonProvider(JsonSerializationOptions? configuration = null, ILogger<SystemTextJsonProvider>? logger = null)
    {
        _configuration = configuration ?? new JsonSerializationOptions();
        _logger = logger;
        _options = CreateOptions(_configuration);
    }

    /// <inheritdoc />
    public JsonSerializerOptions Options => _options;

    /// <inheritdoc />
    public string Serialize<T>(T value)
    {
        if (value == null) return "null";

        try
        {
            var stopwatch = _configuration.PerformanceMonitoring.Enabled ? Stopwatch.StartNew() : null;

            var result = JsonSerializer.Serialize(value, _options);

            LogPerformanceMetrics(stopwatch, "Serialize", typeof(T).Name, result.Length);

            return result;
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "JSON序列化失败: {Type}", typeof(T).Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> SerializeAsync<T>(T value, CancellationToken cancellationToken = default)
    {
        if (value == null) return "null";

        try
        {
            var stopwatch = _configuration.PerformanceMonitoring.Enabled ? Stopwatch.StartNew() : null;

            using var stream = new MemoryStream();
            await JsonSerializer.SerializeAsync(stream, value, _options, cancellationToken);

            var result = System.Text.Encoding.UTF8.GetString(stream.ToArray());

            LogPerformanceMetrics(stopwatch, "SerializeAsync", typeof(T).Name, result.Length);

            return result;
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "异步JSON序列化失败: {Type}", typeof(T).Name);
            throw;
        }
    }

    /// <inheritdoc />
    public void Serialize<T>(Stream stream, T value)
    {
        if (value == null)
        {
            JsonSerializer.Serialize(stream, (T?)default, _options);
            return;
        }

        try
        {
            var stopwatch = _configuration.PerformanceMonitoring.Enabled ? Stopwatch.StartNew() : null;

            JsonSerializer.Serialize(stream, value, _options);

            LogPerformanceMetrics(stopwatch, "SerializeToStream", typeof(T).Name, stream.Length);
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "JSON流序列化失败: {Type}", typeof(T).Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SerializeAsync<T>(Stream stream, T value, CancellationToken cancellationToken = default)
    {
        if (value == null)
        {
            await JsonSerializer.SerializeAsync(stream, (T?)default, _options, cancellationToken);
            return;
        }

        try
        {
            var stopwatch = _configuration.PerformanceMonitoring.Enabled ? Stopwatch.StartNew() : null;

            await JsonSerializer.SerializeAsync(stream, value, _options, cancellationToken);

            LogPerformanceMetrics(stopwatch, "SerializeToStreamAsync", typeof(T).Name, stream.Length);
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "异步JSON流序列化失败: {Type}", typeof(T).Name);
            throw;
        }
    }

    /// <inheritdoc />
    public T? Deserialize<T>(string json)
    {
        if (string.IsNullOrEmpty(json)) return default;

        try
        {
            var stopwatch = _configuration.PerformanceMonitoring.Enabled ? Stopwatch.StartNew() : null;

            var result = JsonSerializer.Deserialize<T>(json, _options);

            LogPerformanceMetrics(stopwatch, "Deserialize", typeof(T).Name, json.Length);

            return result;
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "JSON反序列化失败: {Type}, JSON: {Json}", typeof(T).Name, json[..Math.Min(json.Length, 200)]);
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default)
    {
        try
        {
            var stopwatch = _configuration.PerformanceMonitoring.Enabled ? Stopwatch.StartNew() : null;

            var result = await JsonSerializer.DeserializeAsync<T>(stream, _options, cancellationToken);

            LogPerformanceMetrics(stopwatch, "DeserializeAsync", typeof(T).Name, stream.Length);

            return result;
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "异步JSON流反序列化失败: {Type}", typeof(T).Name);
            throw;
        }
    }

    /// <inheritdoc />
    public bool TrySerialize<T>(T value, out string json)
    {
        try
        {
            json = Serialize(value);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "JSON序列化尝试失败: {Type}", typeof(T).Name);
            json = string.Empty;
            return false;
        }
    }

    /// <inheritdoc />
    public bool TryDeserialize<T>(string json, out T? result)
    {
        try
        {
            result = Deserialize<T>(json);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "JSON反序列化尝试失败: {Type}", typeof(T).Name);
            result = default;
            return false;
        }
    }

    /// <summary>
    /// 创建JsonSerializerOptions
    /// </summary>
    /// <param name="config">配置选项</param>
    /// <returns>序列化选项</returns>
    private static JsonSerializerOptions CreateOptions(JsonSerializationOptions config)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = config.PropertyNamingPolicy,
            WriteIndented = config.WriteIndented,
            DefaultIgnoreCondition = config.IgnoreNullValues ? JsonIgnoreCondition.WhenWritingNull : JsonIgnoreCondition.Never,
            MaxDepth = config.MaxDepth,
            AllowTrailingCommas = config.AllowTrailingCommas,
            ReadCommentHandling = config.AllowComments ? JsonCommentHandling.Skip : JsonCommentHandling.Disallow
        };

        // 添加内置转换器
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }

    /// <summary>
    /// 记录性能指标
    /// </summary>
    /// <param name="stopwatch">计时器</param>
    /// <param name="operation">操作名称</param>
    /// <param name="typeName">类型名称</param>
    /// <param name="dataSize">数据大小</param>
    private void LogPerformanceMetrics(Stopwatch? stopwatch, string operation, string typeName, long dataSize)
    {
        if (stopwatch == null || !_configuration.PerformanceMonitoring.Enabled) return;

        stopwatch.Stop();

        if (_configuration.PerformanceMonitoring.TrackSerializationTime)
        {
            _logger?.LogDebug("JSON{Operation} {Type}: {ElapsedMs}ms, {DataSize}bytes",
                operation, typeName, stopwatch.Elapsed.TotalMilliseconds, dataSize);
        }
    }
}
