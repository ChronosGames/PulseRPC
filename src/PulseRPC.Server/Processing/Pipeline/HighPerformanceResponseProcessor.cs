using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Server.Transport;

namespace PulseRPC.Server.Processing.Pipeline;

public static class ResponseSerializerRegistry
{
    public static IResponseSerializerRegistry? Instance;

    public static void Register(IResponseSerializerRegistry instance)
    {
        Instance = instance;
    }
}

/// <summary>
/// 高性能响应处理器接口
/// </summary>
public interface IResponseProcessor : IDisposable
{
    /// <summary>
    /// 启动响应处理器
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止响应处理器
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 处理消息处理结果
    /// </summary>
    ValueTask ProcessMessageResultAsync(MessageProcessedEventArgs eventArgs);
}

/// <summary>
/// 高性能响应处理器实现
/// 负责序列化响应和发送给客户端
/// 符合三层抽象架构，使用会话管理器发送响应
/// </summary>
internal sealed class HighPerformanceResponseProcessor : IResponseProcessor
{
    private readonly IServerChannelManager _sessionManager;
    private readonly ISerializerProvider _serializerProvider;
    private readonly ILogger<HighPerformanceResponseProcessor> _logger;
    private readonly ResponseProcessorOptions _options;

    // 高性能通道用于响应处理
    private readonly ChannelWriter<ResponseTask> _responseWriter;
    private readonly ChannelReader<ResponseTask> _responseReader;

    // 序列化器缓存
    private readonly ConcurrentDictionary<string, ISerializer> _serializerCache = new();

    // 生成的响应序列化器注册表（优先使用，零拷贝路径）
    private readonly IResponseSerializerRegistry? _responseSerializerRegistry;

    // 处理任务
    private Task[]? _processingTasks;
    private readonly CancellationTokenSource _shutdownCts = new();

    // 性能统计（使用线程本地计数器以减少原子操作开销）
    private long _totalResponsesSent;
    private long _totalResponseErrors;
    private long _totalSerializationTime;

    // 响应发送的最大缓冲区大小限制（防止内存无限增长）
    private const int MaxResponseBufferSize = 1024 * 1024; // 1MB

    // 线程本地指标（避免原子操作开销）
    [ThreadStatic]
    private static ProcessorMetrics? t_localMetrics;

    // 所有线程的指标集合（用于聚合）
    private readonly ConcurrentBag<ProcessorMetrics> _allMetrics = new();

    public HighPerformanceResponseProcessor(
        IServerChannelManager sessionManager,
        ISerializerProvider? serializerProvider = null,
        ResponseProcessorOptions? options = null,
        ILogger<HighPerformanceResponseProcessor>? logger = null,
        IResponseSerializerRegistry? responseSerializerRegistry = null)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _serializerProvider = serializerProvider ?? PulseRPCSerializerProvider.Instance;
        _options = options ?? new ResponseProcessorOptions();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<HighPerformanceResponseProcessor>.Instance;
        _responseSerializerRegistry = responseSerializerRegistry;

         // 创建有界响应通道
         var channelOptions = new BoundedChannelOptions(_options.ChannelCapacity)
         {
             SingleReader = false, // 多个处理器线程
             SingleWriter = false, // 多个调度器线程写入
             AllowSynchronousContinuations = false,
             FullMode = BoundedChannelFullMode.Wait // 背压控制
         };

         var responseChannel = Channel.CreateBounded<ResponseTask>(channelOptions);
         _responseWriter = responseChannel.Writer;
         _responseReader = responseChannel.Reader;
     }

     public Task StartAsync(CancellationToken cancellationToken = default)
     {
         _logger.LogInformation("启动高性能响应处理器，处理器线程数: {ProcessorCount}", _options.ProcessorThreadCount);

         // 预热响应序列化器（减少运行时查找开销）
         if (_responseSerializerRegistry != null)
         {
             var serializers = _responseSerializerRegistry.EnumerateSerializers();
             _logger.LogInformation("预热响应序列化器: {Count} 个", serializers.Length);
         }

         // 启动多个响应处理器线程
         _processingTasks = new Task[_options.ProcessorThreadCount];

         for (var i = 0; i < _options.ProcessorThreadCount; i++)
         {
             var processorId = i;
             _processingTasks[i] = Task.Run(async () => await ProcessResponseTasksAsync(processorId, _shutdownCts.Token), cancellationToken);
         }

         _logger.LogInformation("响应处理器启动完成");
         return Task.CompletedTask;
     }

     public async Task StopAsync(CancellationToken cancellationToken = default)
     {
         _logger.LogInformation("停止响应处理器");

         // 标记写入完成（安全关闭通道）
         try
         {
             _responseWriter.Complete();
         }
         catch (ChannelClosedException)
         {
             // 通道已经关闭，忽略此异常
             _logger.LogDebug("响应通道已经关闭");
         }

         // 取消所有处理任务
         await _shutdownCts.CancelAsync();

         // 等待所有处理任务完成
         if (_processingTasks != null)
         {
             try
             {
                 await Task.WhenAll(_processingTasks).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
             }
             catch (TimeoutException)
             {
                 _logger.LogWarning("响应处理器停止超时");
             }
         }

         // 聚合所有线程的指标
         AggregateMetrics();

         _logger.LogInformation("响应处理器停止完成，总响应数: {TotalResponses}, 错误数: {TotalErrors}",
             _totalResponsesSent, _totalResponseErrors);
     }

     /// <summary>
     /// 处理消息结果 - 高性能入口点（快速路径同步化）
     /// </summary>
     [MethodImpl(MethodImplOptions.AggressiveInlining)]
     public ValueTask ProcessMessageResultAsync(MessageProcessedEventArgs eventArgs)
     {
         var callContext = eventArgs.CallContext;

        // 只有需要响应的消息才处理
        if (callContext.MessageType == MessageType.OneWay)
        {
            return ValueTask.CompletedTask; // 单向消息不需要响应
        }

        // 创建响应任务
        var responseTask = new ResponseTask(
            callContext.ConnectionId,
            callContext.MessageId,
            callContext.ServiceName,
            callContext.MethodName,
            callContext.ProtocolId,
            eventArgs.Result,
            eventArgs.Success,
            eventArgs.Exception,
            DateTime.UtcNow);

         // 快速路径：尝试同步写入（零分配）
         if (_responseWriter.TryWrite(responseTask))
         {
             return ValueTask.CompletedTask;
         }

         // 慢速路径：异步等待（通道满时）
         return ProcessMessageResultSlowPathAsync(responseTask);
     }

     /// <summary>
     /// 获取线程本地指标 - 避免原子操作开销
     /// </summary>
     [MethodImpl(MethodImplOptions.AggressiveInlining)]
     private ProcessorMetrics GetLocalMetrics()
     {
         if (t_localMetrics != null)
         {
             return t_localMetrics;
         }

         t_localMetrics = new ProcessorMetrics();
         _allMetrics.Add(t_localMetrics);
         return t_localMetrics;
     }

     /// <summary>
     /// 聚合所有线程的指标
     /// </summary>
     private void AggregateMetrics()
     {
         var totalSent = 0L;
         var totalErrors = 0L;
         var totalTime = 0L;

         foreach (var metrics in _allMetrics)
         {
             totalSent += metrics.ResponsesSent;
             totalErrors += metrics.ResponseErrors;
             totalTime += metrics.TotalSerializationTime;
         }

         _totalResponsesSent = totalSent;
         _totalResponseErrors = totalErrors;
         _totalSerializationTime = totalTime;
     }

     /// <summary>
     /// 处理消息结果的慢速路径 - 当通道满时使用
     /// </summary>
     private async ValueTask ProcessMessageResultSlowPathAsync(ResponseTask responseTask)
     {
         if (!await _responseWriter.WaitToWriteAsync(_shutdownCts.Token))
         {
             _logger.LogWarning("响应处理器通道已关闭");
             return;
         }

         if (!_responseWriter.TryWrite(responseTask))
         {
             _logger.LogWarning("无法写入响应任务到通道，连接: {ConnectionId}, 消息ID: {MessageId}",
                 responseTask.ConnectionId, responseTask.MessageId);

             // 使用线程本地计数器（避免原子操作开销）
             var metrics = GetLocalMetrics();
             metrics.ResponseErrors++;
         }
     }

     /// <summary>
     /// 处理响应任务的主循环 - 优化为批量读取 + 同步处理
     /// </summary>
     private async Task ProcessResponseTasksAsync(int processorId, CancellationToken cancellationToken)
     {
         _logger.LogDebug("响应处理器 #{ProcessorId} 启动", processorId);

         // 批量缓冲区（避免每次异步迭代开销）
         var batchBuffer = new ResponseTask[32];

         try
         {
             while (!cancellationToken.IsCancellationRequested)
             {
                 // 等待至少一个任务可用（异步等待）
                 if (!await _responseReader.WaitToReadAsync(cancellationToken))
                     break;

                 // 批量同步读取（快速路径，避免异步开销）
                 var count = 0;
                 while (count < batchBuffer.Length && _responseReader.TryRead(out var task))
                 {
                     batchBuffer[count++] = task;
                 }

                 // 批量处理（同步循环，减少异步状态机开销）
                 for (var i = 0; i < count; i++)
                 {
                     await ProcessResponseTaskAsync(batchBuffer[i], processorId);
                 }
             }
         }
         catch (OperationCanceledException)
         {
             // 正常关闭
         }
         catch (Exception ex)
         {
             _logger.LogError(ex, "响应处理器 #{ProcessorId} 发生异常", processorId);
         }

         _logger.LogDebug("响应处理器 #{ProcessorId} 停止", processorId);
     }

     /// <summary>
     /// 处理单个响应任务
     /// </summary>
     private async Task ProcessResponseTaskAsync(ResponseTask responseTask, int processorId)
     {
         var startTime = Stopwatch.GetTimestamp();

         try
         {
             // 创建响应消息头
             var responseHeader = new MessageHeader(MessageType.Response, string.Empty, string.Empty)
             {
                 MessageId = responseTask.MessageId,
                 // Timestamp = responseTask.ResponseTime,
                 Flags = MessageFlags.None
             };

             ReadOnlyMemory<byte> responsePayload;

            if (responseTask.Success && responseTask.Result != null)
            {
                // 序列化成功响应（普通业务消息）
                responsePayload = await SerializeResponseAsync(
                    responseTask.Result, 
                    responseTask.ServiceName, 
                    responseTask.MethodName,
                    responseTask.ProtocolId);
            }
             else if (!responseTask.Success && responseTask.Exception != null)
             {
                 // 序列化错误响应
                 responseHeader.Type = MessageType.Error;
                 responsePayload = await SerializeErrorResponseAsync(responseTask.Exception);
             }
             else
             {
                 // 空响应 (void 方法、系统消息或 null 结果)
                 responsePayload = ReadOnlyMemory<byte>.Empty;
             }

             // 更新负载长度
             // responseHeader.PayloadLength = responsePayload.Length;

             // 创建响应消息包
             var responsePacket = new MessagePacket(responseHeader, responsePayload.Span);

             // 序列化消息包到内存池缓冲区
             var estimatedSize = responsePacket.EstimateSize();

             // 防止超大消息导致内存暴涨
             if (estimatedSize > MaxResponseBufferSize)
             {
                 _logger.LogWarning("响应消息过大，跳过发送: {ConnectionId}, {MessageId}, 大小: {Size} bytes",
                     responseTask.ConnectionId, responseTask.MessageId, estimatedSize);

                 var metrics = GetLocalMetrics();
                 metrics.ResponseErrors++;
                 return;
             }

             using var packetBuffer = MemoryPool<byte>.Shared.Rent(estimatedSize);
             var bytesWritten = responsePacket.WriteTo(packetBuffer.Memory.Span);

             // 通过会话管理器发送响应到客户端
             var session = _sessionManager.GetChannel(responseTask.ConnectionId);
             var sent = false;

             if (session != null)
             {
                 // 发送完整的消息包（包括头长度、头数据和payload数据）
                 sent = await session.SendAsync(packetBuffer.Memory[..bytesWritten], _shutdownCts.Token);
                 _logger.LogDebug("[响应发送] 连接={ConnectionId}, 消息ID={MessageId}, 数据大小={BytesWritten} bytes", responseTask.ConnectionId, responseTask.MessageId, bytesWritten);
             }
             else
             {
                 _logger.LogWarning("未找到会话: {ConnectionId}, 无法发送响应", responseTask.ConnectionId);
             }

             if (sent)
             {
                 // 使用线程本地计数器（避免原子操作开销）
                 var metrics = GetLocalMetrics();
                 metrics.ResponsesSent++;

                 var processingTime = (Stopwatch.GetTimestamp() - startTime) * 1000 / Stopwatch.Frequency;
                 metrics.TotalSerializationTime += processingTime;

                 _logger.LogTrace("响应发送成功: 会话={SessionId}, 消息ID={MessageId}, 耗时={ProcessingTime}ms", responseTask.ConnectionId, responseTask.MessageId, processingTime);
             }
             else
             {
                 // 使用线程本地计数器（避免原子操作开销）
                 var metrics = GetLocalMetrics();
                 metrics.ResponseErrors++;

                 _logger.LogWarning("响应发送失败: 会话={SessionId}, 消息ID={MessageId}", responseTask.ConnectionId, responseTask.MessageId);
             }
         }
         catch (Exception ex)
         {
             // 使用线程本地计数器（避免原子操作开销）
             var metrics = GetLocalMetrics();
             metrics.ResponseErrors++;

             _logger.LogError(ex, "响应处理失败: 连接={ConnectionId}, 消息ID={MessageId}, 处理器={ProcessorId}",
                 responseTask.ConnectionId,
                 responseTask.MessageId,
                 processorId);
         }
    }

   /// <summary>
   /// 序列化成功响应
   /// </summary>
   private Task<ReadOnlyMemory<byte>> SerializeResponseAsync(
       object? result, 
       string serviceName, 
       string methodName, 
       ushort protocolId)
   {
       if (result == null)
       {
           return Task.FromResult(ReadOnlyMemory<byte>.Empty);
       }

       // 优先尝试使用协议号查找序列化器（最快路径）
       if (protocolId != 0 && _responseSerializerRegistry != null)
       {
           if (_responseSerializerRegistry.TryGetSerializer(protocolId, out var protocolSerializer))
           {
               try
               {
                   var buffer = new ArrayBufferWriter<byte>();
                   protocolSerializer.Serialize(result, buffer);
                   return Task.FromResult(buffer.WrittenMemory);
               }
               catch (Exception ex)
               {
                   _logger.LogWarning(ex, "协议号响应序列化器失败: ProtocolId=0x{ProtocolId:X4}", protocolId);
               }
           }
       }

       // 回退：尝试使用方法名查找序列化器（向后兼容）
       if (!string.IsNullOrEmpty(serviceName) && !string.IsNullOrEmpty(methodName) &&
           _responseSerializerRegistry != null &&
           _responseSerializerRegistry.TryGetSerializer(serviceName, methodName, out var responseSerializer))
       {
           try
           {
               var buffer = new ArrayBufferWriter<byte>();
               responseSerializer.Serialize(result, buffer);
               return Task.FromResult(buffer.WrittenMemory);
           }
           catch (Exception ex)
           {
               _logger.LogWarning(ex, "生成的响应序列化器失败: {ServiceName}.{MethodName}",  serviceName, methodName);
           }
       }

       // 降级：记录错误日志
       var identifier = protocolId != 0 
           ? $"ProtocolId=0x{protocolId:X4}" 
           : $"{serviceName}.{methodName}";
       throw new ArgumentException($"未找到响应序列化器: {identifier}");
   }

     /// <summary>
     /// 序列化错误响应
     /// </summary>
     private Task<ReadOnlyMemory<byte>> SerializeErrorResponseAsync(Exception exception)
     {
         // 创建错误响应对象
         var errorResponse = new ErrorResponse
         {
             ErrorCode = GetErrorCode(exception),
             ErrorMessage = exception.Message,
             ErrorType = exception.GetType().Name,
             StackTrace = _options.IncludeStackTrace ? exception.StackTrace : null
         };

         // 序列化错误响应
         var serializer = GetCachedSerializer("__error__");
         var buffer = new ArrayBufferWriter<byte>();
         serializer.Serialize(buffer, errorResponse);

         return Task.FromResult(buffer.WrittenMemory);
     }

     /// <summary>
     /// 获取缓存的序列化器
     /// </summary>
     [MethodImpl(MethodImplOptions.AggressiveInlining)]
     private ISerializer GetCachedSerializer(string cacheKey)
     {
         return _serializerCache.GetOrAdd(cacheKey, static (key, provider) => provider.Create(MethodType.Unary, null), _serializerProvider);
     }

     /// <summary>
     /// 获取错误代码
     /// </summary>
     private static string GetErrorCode(Exception exception)
     {
         return exception switch
         {
             ArgumentNullException => "NULL_ARGUMENT",
             ArgumentException => "INVALID_ARGUMENT",
             InvalidOperationException => "INVALID_OPERATION",
             NotImplementedException => "NOT_IMPLEMENTED",
             TimeoutException => "TIMEOUT",
             UnauthorizedAccessException => "UNAUTHORIZED",
             _ => "INTERNAL_ERROR"
         };
     }

     public void Dispose()
     {
         if (!_shutdownCts.IsCancellationRequested)
         {
             StopAsync().GetAwaiter().GetResult();
         }

         _shutdownCts.Dispose();
         _serializerCache.Clear();
     }
}

/// <summary>
/// 线程本地性能指标（避免原子操作开销）
/// </summary>
internal sealed class ProcessorMetrics
{
    public long ResponsesSent;
    public long ResponseErrors;
    public long TotalSerializationTime;
}

/// <summary>
/// 响应任务结构
/// </summary>
internal readonly struct ResponseTask
{
    public readonly string ConnectionId;
    public readonly Guid MessageId;
    public readonly string ServiceName;
    public readonly string MethodName;
    public readonly ushort ProtocolId; // 协议号（0 表示使用方法名路径）
    public readonly object? Result;
    public readonly bool Success;
    public readonly Exception? Exception;
    public readonly DateTime ResponseTime;

    public ResponseTask(
        string connectionId,
        Guid messageId,
        string serviceName,
        string methodName,
        ushort protocolId,
        object? result,
        bool success,
        Exception? exception,
        DateTime responseTime)
    {
        ConnectionId = connectionId;
        MessageId = messageId;
        ServiceName = serviceName;
        MethodName = methodName;
        ProtocolId = protocolId;
        Result = result;
        Success = success;
        Exception = exception;
        ResponseTime = responseTime;
    }
}

/// <summary>
/// 错误响应模型
/// </summary>
[MemoryPack.MemoryPackable]
public partial class ErrorResponse
{
    [MemoryPack.MemoryPackOrder(0)]
    public string ErrorCode { get; set; } = string.Empty;

    [MemoryPack.MemoryPackOrder(1)]
    public string ErrorMessage { get; set; } = string.Empty;

    [MemoryPack.MemoryPackOrder(2)]
    public string ErrorType { get; set; } = string.Empty;

    [MemoryPack.MemoryPackOrder(3)]
    public string? StackTrace { get; set; }

    [MemoryPack.MemoryPackOrder(4)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 响应处理器配置选项
/// </summary>
public sealed class ResponseProcessorOptions
{
    /// <summary>
    /// 处理器线程数量
    /// </summary>
    public int ProcessorThreadCount { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>
    /// 响应通道容量（增大到50000以应对高并发场景）
    /// </summary>
    public int ChannelCapacity { get; set; } = 50000;

    /// <summary>
    /// 是否在错误响应中包含堆栈跟踪
    /// </summary>
    public bool IncludeStackTrace { get; set; } = false;

    /// <summary>
    /// 序列化器缓存大小
    /// </summary>
    public int SerializerCacheSize { get; set; } = 1000;

    /// <summary>
    /// 响应超时时间
    /// </summary>
    public TimeSpan ResponseTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
