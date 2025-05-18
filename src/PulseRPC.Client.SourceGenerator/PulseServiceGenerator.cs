using System;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace PulseRPC.Client.SourceGenerator;

[Generator]
public class PulseServiceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 注册生成源码的操作
        context.RegisterPostInitializationOutput(ctx => GenerateDefaultPulseService(ctx));
    }

    private void GenerateDefaultPulseService(IncrementalGeneratorPostInitializationContext context)
    {
        var source = @"
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using Microsoft.Extensions.Logging;
using PulseRPC.Network;

namespace PulseRPC.Client;

/// <summary>
/// 默认的PulseService实现，用于客户端
/// </summary>
public class DefaultPulseService : IPulseService
{
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<Type, Delegate> _handlers = new ConcurrentDictionary<Type, Delegate>();

    /// <summary>
    /// 初始化一个新的DefaultPulseService实例
    /// </summary>
    /// <param name=""logger"">日志记录器</param>
    public DefaultPulseService(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 序列化对象
    /// </summary>
    public void Serialize<T>(IBufferWriter<byte> writer, in T value) where T : IMemoryPackable<T>
    {
        try
        {
            MemoryPackSerializer.Serialize(writer, value);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, ""消息序列化失败: {Type}"", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// 处理消息
    /// </summary>
    public async Task ProcessMessageAsync(NetworkSession session, ushort sequenceId,
        ReadOnlySequence<byte> buffer, CancellationToken cancellationToken = default)
    {
        try
        {
            // 客户端实现，接收服务端消息
            _logger?.LogTrace(""处理消息: SequenceId={SequenceId}, Size={Size}"", sequenceId, buffer.Length);

            // 实际项目中需要根据协议解析消息并分发到相应的处理器
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, ""处理消息失败: SequenceId={SequenceId}"", sequenceId);
        }
    }

    /// <summary>
    /// 反序列化对象
    /// </summary>
    public T Deserialize<T>(ReadOnlySpan<byte> bytes) where T : IMemoryPackable<T>
    {
        try
        {
            return MemoryPackSerializer.Deserialize<T>(bytes)!;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, ""消息反序列化失败: {Type}"", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// 注册消息处理器
    /// </summary>
    public void RegisterHandler<TMessage, TResponse>(
        Func<NetworkSession, TMessage, CancellationToken, Task<TResponse>> handler)
        where TMessage : IMemoryPackable<TMessage>
        where TResponse : IMemoryPackable<TResponse>
    {
        _handlers[typeof(TMessage)] = handler;
        _logger?.LogDebug(""已注册处理器: {RequestType} -> {ResponseType}"", typeof(TMessage).Name, typeof(TResponse).Name);
    }

    /// <summary>
    /// 注册单向消息处理器
    /// </summary>
    public void RegisterHandler<TMessage>(
        Func<NetworkSession, TMessage, CancellationToken, Task> handler)
        where TMessage : IMemoryPackable<TMessage>
    {
        _handlers[typeof(TMessage)] = handler;
        _logger?.LogDebug(""已注册单向消息处理器: {MessageType}"", typeof(TMessage).Name);
    }

    /// <summary>
    /// 处理接收到的消息
    /// </summary>
    /// <param name=""session"">网络会话</param>
    /// <param name=""data"">消息数据</param>
    /// <param name=""cancellationToken"">取消令牌</param>
    /// <returns>是否处理成功</returns>
    public ValueTask<bool> HandleMessageAsync(NetworkSession session, Memory<byte> data, CancellationToken cancellationToken = default)
    {
        try
        {
            // 在客户端，我们只需要处理来自服务器的响应消息
            _logger?.LogTrace(""收到消息: {Length} 字节"", data.Length);
            return ValueTask.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, ""处理消息时出错"");
            return ValueTask.FromResult(false);
        }
    }

    /// <summary>
    /// 序列化消息
    /// </summary>
    /// <typeparam name=""T"">消息类型</typeparam>
    /// <param name=""message"">消息</param>
    /// <returns>序列化后的字节数组</returns>
    public byte[] SerializeMessage<T>(T message) where T : IMemoryPackable<T>
    {
        try
        {
            return MemoryPackSerializer.Serialize(message);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, ""消息序列化失败: {Type}"", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// 反序列化消息
    /// </summary>
    /// <typeparam name=""T"">消息类型</typeparam>
    /// <param name=""data"">数据</param>
    /// <returns>反序列化后的消息</returns>
    public T? DeserializeMessage<T>(ReadOnlyMemory<byte> data) where T : IMemoryPackable<T>
    {
        try
        {
            return MemoryPackSerializer.Deserialize<T>(data.Span);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, ""消息反序列化失败: {Type}"", typeof(T).Name);
            return default;
        }
    }
}
";

        context.AddSource("PulseService.Client.g.cs", SourceText.From(source, Encoding.UTF8));
    }
}
