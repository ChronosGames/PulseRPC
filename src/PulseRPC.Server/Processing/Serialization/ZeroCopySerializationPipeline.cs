using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MemoryPack.Compression;

namespace PulseRPC.Server.Processing.Serialization;

/// <summary>
/// 零拷贝序列化管道 - 直接写入网络缓冲区避免中间复制
/// 针对高频小消息和大消息分别优化
/// </summary>
public sealed class ZeroCopySerializationPipeline : IAsyncDisposable
{
    private readonly PipeWriter _writer;
    private readonly ILogger _logger;
    private readonly ZeroCopySerializationOptions _options;
    private readonly MemoryPackSerializerOptions _serializerOptions;

    // 小消息快速路径缓存
    private readonly ThreadLocal<ArrayBufferWriter<byte>> _fastPathBuffers;
    private readonly ConcurrentDictionary<Type, FastPathSerializer> _fastPathSerializers;

    // 性能统计
    private long _totalSerializations;
    private long _totalBytesWritten;
    private long _fastPathCount;
    private long _slowPathCount;
    private long _largeObjectCount;

    /// <summary>
    /// 零拷贝序列化选项
    /// </summary>
    public sealed class ZeroCopySerializationOptions
    {
        /// <summary>小消息阈值 - 小于此大小的消息使用快速路径</summary>
        public int SmallMessageThreshold { get; set; } = 1024; // 1KB

        /// <summary>大消息阈值 - 大于此大小的消息使用特殊处理</summary>
        public int LargeMessageThreshold { get; set; } = 64 * 1024; // 64KB

        /// <summary>快速路径缓冲区大小</summary>
        public int FastPathBufferSize { get; set; } = 4 * 1024; // 4KB

        /// <summary>是否启用预分配缓冲区优化</summary>
        public bool EnablePreallocatedBuffers { get; set; } = true;

        /// <summary>是否启用类型特定的序列化优化</summary>
        public bool EnableTypeSpecificOptimization { get; set; } = true;

        /// <summary>管道刷新阈值</summary>
        public int PipeFlushThreshold { get; set; } = 16 * 1024; // 16KB

        /// <summary>是否启用压缩</summary>
        public bool EnableCompression { get; set; } = false;

        /// <summary>压缩阈值</summary>
        public int CompressionThreshold { get; set; } = 1024;
    }

    /// <summary>
    /// 快速路径序列化器 - 为特定类型预编译
    /// </summary>
    private sealed class FastPathSerializer
    {
        public readonly Type Type;
        public readonly int EstimatedSize;
        public readonly Func<object, IBufferWriter<byte>, MemoryPackSerializerOptions, int> SerializeFunc;
        public long UsageCount;

        public FastPathSerializer(Type type, int estimatedSize,
            Func<object, IBufferWriter<byte>, MemoryPackSerializerOptions, int> serializeFunc)
        {
            Type = type;
            EstimatedSize = estimatedSize;
            SerializeFunc = serializeFunc;
        }
    }

    /// <summary>
    /// 构造函数
    /// </summary>
    public ZeroCopySerializationPipeline(PipeWriter writer,
        ZeroCopySerializationOptions? options = null,
        MemoryPackSerializerOptions? serializerOptions = null,
        ILogger? logger = null)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _options = options ?? new ZeroCopySerializationOptions();
        _serializerOptions = serializerOptions ?? MemoryPackSerializerOptions.Default;
        _logger = logger ?? NullLogger.Instance;

        _fastPathBuffers = new ThreadLocal<ArrayBufferWriter<byte>>(() => new ArrayBufferWriter<byte>(_options.FastPathBufferSize));
        _fastPathSerializers = new ConcurrentDictionary<Type, FastPathSerializer>();

        _logger.LogInformation("ZeroCopySerializationPipeline已初始化 - 选项: {Options}", _options);
    }

    /// <summary>
    /// 性能统计
    /// </summary>
    public (long TotalSerializations, long TotalBytesWritten, long FastPathCount,
        long SlowPathCount, long LargeObjectCount, double FastPathRatio) Statistics
    {
        get
        {
            var totalSerializations = Interlocked.Read(ref _totalSerializations);
            var totalBytesWritten = Interlocked.Read(ref _totalBytesWritten);
            var fastPathCount = Interlocked.Read(ref _fastPathCount);
            var slowPathCount = Interlocked.Read(ref _slowPathCount);
            var largeObjectCount = Interlocked.Read(ref _largeObjectCount);
            var fastPathRatio = totalSerializations > 0 ? (double)fastPathCount / totalSerializations : 0;

            return (totalSerializations, totalBytesWritten, fastPathCount,
                slowPathCount, largeObjectCount, fastPathRatio);
        }
    }

    /// <summary>
    /// 序列化对象到管道 - 主要API
    /// </summary>
    public ValueTask<bool> SerializeAsync<T>(T value, CancellationToken cancellationToken = default)
    {
        if (value == null)
            return WriteNullAsync(cancellationToken);

        Interlocked.Increment(ref _totalSerializations);

        var type = typeof(T);

        // 尝试快速路径
        if (TryFastPathSerialize(value, type))
        {
            Interlocked.Increment(ref _fastPathCount);
            return ValueTask.FromResult(true);
        }

        // 使用标准路径
        return StandardPathSerializeAsync(value, type, cancellationToken);
    }

    /// <summary>
    /// 快速路径序列化 - 针对小消息优化
    /// </summary>
    private bool TryFastPathSerialize<T>(T value, Type type)
    {
        if (!_options.EnableTypeSpecificOptimization)
            return false;

        // 获取或创建快速序列化器
        var fastSerializer = _fastPathSerializers.GetOrAdd(type, CreateFastPathSerializer!);
        if (fastSerializer == null)
            return false;

        try
        {
            // 使用线程本地缓冲区
            var buffer = _fastPathBuffers.Value!;
            buffer.Clear();

            // 执行序列化
            var bytesWritten = fastSerializer.SerializeFunc(value!, buffer, _serializerOptions);

            if (bytesWritten > _options.SmallMessageThreshold)
            {
                // 超出小消息阈值，使用标准路径
                return false;
            }

            // 直接写入管道
            var memory = _writer.GetMemory(bytesWritten);
            buffer.WrittenSpan.CopyTo(memory.Span);
            _writer.Advance(bytesWritten);

            // 更新统计
            Interlocked.Add(ref _totalBytesWritten, bytesWritten);
            Interlocked.Increment(ref fastSerializer.UsageCount);

            // 检查是否需要刷新
            if (bytesWritten >= _options.PipeFlushThreshold)
            {
                _writer.FlushAsync().AsTask().ConfigureAwait(false);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "快速路径序列化失败，类型: {Type}", type);
            return false;
        }
    }

    /// <summary>
    /// 标准路径序列化 - 通用序列化路径
    /// </summary>
    private async ValueTask<bool> StandardPathSerializeAsync<T>(T value, Type type, CancellationToken cancellationToken)
    {
        try
        {
            var startPosition = _writer.UnflushedBytes;

            // 检查是否为大对象
            if (IsLargeObject(value))
            {
                Interlocked.Increment(ref _largeObjectCount);
                return await SerializeLargeObjectAsync(value, cancellationToken);
            }

            // 标准序列化到管道
            MemoryPackSerializer.Serialize(_writer, value, _serializerOptions);

            var bytesWritten = _writer.UnflushedBytes - startPosition;
            Interlocked.Add(ref _totalBytesWritten, bytesWritten);
            Interlocked.Increment(ref _slowPathCount);

            // 根据写入大小决定是否立即刷新
            if (bytesWritten >= _options.PipeFlushThreshold)
            {
                await _writer.FlushAsync(cancellationToken);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "标准路径序列化失败，类型: {Type}", type);
            return false;
        }
    }

    /// <summary>
    /// 大对象序列化 - 特殊处理策略
    /// </summary>
    private async ValueTask<bool> SerializeLargeObjectAsync<T>(T value, CancellationToken cancellationToken)
    {
        try
        {
            // 对于大对象，我们可能需要压缩或分块处理
            if (_options.EnableCompression)
            {
                return await SerializeWithCompressionAsync(value, cancellationToken);
            }

            // 直接序列化但确保及时刷新
            var startPosition = _writer.UnflushedBytes;

            MemoryPackSerializer.Serialize(_writer, value, _serializerOptions);

            var bytesWritten = _writer.UnflushedBytes - startPosition;
            Interlocked.Add(ref _totalBytesWritten, bytesWritten);

            // 大对象立即刷新
            await _writer.FlushAsync(cancellationToken);

            _logger.LogDebug("大对象序列化完成: {Type}, 大小: {Size}", typeof(T), bytesWritten);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "大对象序列化失败，类型: {Type}", typeof(T));
            return false;
        }
    }

    /// <summary>
    /// 压缩序列化 - 仅在启用压缩时使用
    /// </summary>
    private async ValueTask<bool> SerializeWithCompressionAsync<T>(T value, CancellationToken cancellationToken)
    {
        try
        {
            // Compression(require using)
            using var compressor = new BrotliCompressor();
            MemoryPackSerializer.Serialize(compressor, value, _serializerOptions);

            // Or write to other IBufferWriter<byte>(for example PipeWriter)
            compressor.CopyTo(_writer);

            Interlocked.Add(ref _totalBytesWritten, _writer.UnflushedBytes);
            await _writer.FlushAsync(cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "压缩序列化失败，类型: {Type}", typeof(T));
            return false;
        }
    }

    /// <summary>
    /// 写入null值
    /// </summary>
    private ValueTask<bool> WriteNullAsync(CancellationToken cancellationToken)
    {
        try
        {
            var memory = _writer.GetMemory(1);
            memory.Span[0] = 0; // null标识
            _writer.Advance(1);

            Interlocked.Add(ref _totalBytesWritten, 1);
            Interlocked.Increment(ref _fastPathCount);

            return ValueTask.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "写入null值失败");
            return ValueTask.FromResult(false);
        }
    }

    /// <summary>
    /// 创建快速路径序列化器
    /// </summary>
    private FastPathSerializer? CreateFastPathSerializer(Type type)
    {
        try
        {
            // 估算序列化大小（简单启发式）
            var estimatedSize = EstimateSerializationSize(type);
            if (estimatedSize > _options.SmallMessageThreshold)
                return null;

            // 创建序列化委托
            var serializeMethod = typeof(MemoryPackSerializer)
                .GetMethod("Serialize", new[] { typeof(IBufferWriter<byte>), type, typeof(MemoryPackSerializerOptions) });

            if (serializeMethod == null)
                return null;

            Func<object, IBufferWriter<byte>, MemoryPackSerializerOptions, int> serializeFunc =
                (obj, writer, options) =>
                {
                    var startLength = writer is ArrayBufferWriter<byte> abw ? abw.WrittenCount : 0;
                    serializeMethod.Invoke(null, new[] { writer, obj, options });
                    var endLength = writer is ArrayBufferWriter<byte> abw2 ? abw2.WrittenCount : 0;
                    return endLength - startLength;
                };

            return new FastPathSerializer(type, estimatedSize, serializeFunc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "创建快速路径序列化器失败，类型: {Type}", type);
            return null;
        }
    }

    /// <summary>
    /// 估算序列化大小
    /// </summary>
    private static int EstimateSerializationSize(Type type)
    {
        // 简单的大小估算逻辑
        if (type.IsPrimitive)
        {
            return Marshal.SizeOf(type) + 4; // 加上MemoryPack开销
        }

        if (type == typeof(string))
        {
            return 256; // 假设平均字符串长度
        }

        if (type.IsArray || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            return 1024; // 假设平均集合大小
        }

        // 复杂对象
        return 512;
    }

    /// <summary>
    /// 检查是否为大对象
    /// </summary>
    private bool IsLargeObject<T>(T value)
    {
        // 简单的大对象检测逻辑
        var type = typeof(T);

        if (type == typeof(byte[]) && value is byte[] byteArray)
        {
            return byteArray.Length > _options.LargeMessageThreshold;
        }

        if (type == typeof(string) && value is string str)
        {
            return str.Length * 2 > _options.LargeMessageThreshold; // Unicode字符
        }

        // 对于其他类型，使用启发式判断
        return false;
    }

    /// <summary>
    /// 强制刷新管道
    /// </summary>
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _writer.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刷新序列化管道失败");
            throw;
        }
    }

    /// <summary>
    /// 获取快速路径统计信息
    /// </summary>
    public FastPathStatistics GetFastPathStatistics()
    {
        var stats = new FastPathStatistics();

        foreach (var kvp in _fastPathSerializers)
        {
            stats.TypeStats.Add(new TypeSerializationStats
            {
                Type = kvp.Key.Name,
                UsageCount = kvp.Value.UsageCount,
                EstimatedSize = kvp.Value.EstimatedSize
            });
        }

        return stats;
    }

    /// <summary>
    /// 快速路径统计信息
    /// </summary>
    public sealed class FastPathStatistics
    {
        public List<TypeSerializationStats> TypeStats { get; } = new();
    }

    /// <summary>
    /// 类型序列化统计
    /// </summary>
    public sealed class TypeSerializationStats
    {
        public string Type { get; set; } = "";
        public long UsageCount { get; set; }
        public int EstimatedSize { get; set; }
    }

    /// <summary>
    /// 异步释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            // 最后一次刷新
            await _writer.FlushAsync();

            var stats = Statistics;
            _logger.LogInformation(
                "ZeroCopySerializationPipeline已释放 - 统计信息: 总序列化数: {Total}, 总字节数: {Bytes}, " +
                "快速路径比率: {FastPathRatio:P2}, 大对象数: {LargeObjects}",
                stats.TotalSerializations, stats.TotalBytesWritten,
                stats.FastPathRatio, stats.LargeObjectCount);

            _fastPathBuffers?.Dispose();
            _fastPathSerializers.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "释放ZeroCopySerializationPipeline时异常");
        }
    }
}
