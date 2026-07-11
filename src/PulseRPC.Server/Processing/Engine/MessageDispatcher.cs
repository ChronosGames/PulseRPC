using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Messaging;
using PulseRPC.Server.Processing.Serialization;

namespace PulseRPC.Server.Processing.Engine;

/// <summary>
/// 高性能消息调度器接口
/// </summary>
public interface IMessageDispatcher : IDisposable
{
    /// <summary>
    /// 启动消息调度器
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止消息调度器
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 分发反序列化的消息
    /// </summary>
    ValueTask<object?> DispatchAsync(MessageEnvelope message, IServiceProvider serviceProvider, CancellationToken cancellationToken = default);

    /// <summary>
    /// 消息处理完成事件
    /// </summary>
    event EventHandler<MessageProcessedEventArgs> MessageProcessed;
}

/// <summary>
/// 基于源生成路由表的直接消息调度器。
/// </summary>
internal sealed class MessageDispatcher : IMessageDispatcher
{
    private const int Stopped = 0;
    private const int Running = 1;
    private const int Stopping = 2;
    private const int Disposed = 3;

    private readonly ILogger<MessageDispatcher> _logger;
    private readonly IServiceRoutingTable _serviceRoutingTable;
    private readonly object _lifecycleLock = new();

    private long _inFlight;
    private int _state;
    private TaskCompletionSource<object?> _drained =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event EventHandler<MessageProcessedEventArgs>? MessageProcessed;

    public MessageDispatcher(ILogger<MessageDispatcher>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MessageDispatcher>.Instance;
        _serviceRoutingTable = ServiceRoutingTableRegistry.Instance
            ?? throw new ArgumentNullException(nameof(ServiceRoutingTableRegistry.Instance));
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lifecycleLock)
        {
            if (_state == Disposed)
            {
                throw new ObjectDisposedException(nameof(MessageDispatcher));
            }

            if (_state == Running)
            {
                return Task.CompletedTask;
            }

            if (_state == Stopping)
            {
                throw new InvalidOperationException("消息调度器正在停止，不能启动。");
            }

            _drained = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _state = Running;
        }

        _logger.LogInformation("消息调度器启动完成（直接协议号路由模式）");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? waitForDrain = null;

        lock (_lifecycleLock)
        {
            if (_state == Disposed || _state == Stopped)
            {
                return;
            }

            if (_state == Running)
            {
                _state = Stopping;
            }

            if (Interlocked.Read(ref _inFlight) == 0)
            {
                _state = Stopped;
                _drained.TrySetResult(null);
                return;
            }

            waitForDrain = _drained.Task;
        }

        try
        {
            await waitForDrain.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("消息调度器停止超时，仍有在途调用未完成");
        }

        lock (_lifecycleLock)
        {
            if (_state == Stopping)
            {
                _state = Stopped;
            }
        }

        _logger.LogInformation("消息调度器停止完成");
    }

    /// <summary>
    /// 分发消息 - 直接进入源生成路由表。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<object?> DispatchAsync(
        MessageEnvelope message,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _state) != Running)
        {
            throw new InvalidOperationException("消息调度器未运行。");
        }

        ArgumentNullException.ThrowIfNull(serviceProvider);
        var header = message.Header ?? throw new ArgumentException("消息头不能为空。", nameof(message));

        if (header.ProtocolId == 0)
        {
            _logger.LogError("消息缺少 ProtocolId，无法分发。所有消息必须包含 Protocol ID。");
            throw new ArgumentException("消息缺少 ProtocolId。Protocol ID 是唯一支持的路由方式，字符串方法名路由已被移除。", nameof(message));
        }

        Interlocked.Increment(ref _inFlight);
        if (Volatile.Read(ref _state) != Running)
        {
            CompleteDispatch();
            throw new InvalidOperationException("消息调度器正在停止，不能接收新消息。");
        }

        var startTimestamp = Stopwatch.GetTimestamp();
        var callContext = CreateCallContext(message, header);

        try
        {
            var serviceKey = header.ServiceKey ?? string.Empty;
            _logger.LogTrace(
                "使用协议号路由: ProtocolId=0x{ProtocolId:X4} ({ProtocolId}), ServiceKey='{ServiceKey}'",
                header.ProtocolId,
                header.ProtocolId,
                serviceKey);

            var result = await _serviceRoutingTable.RouteByProtocolIdAsync(
                serviceProvider,
                header.ServiceName ?? string.Empty,
                header.ProtocolId,
                serviceKey,
                message.Payload,
                cancellationToken).ConfigureAwait(false);

            var processingTime = Stopwatch.GetElapsedTime(startTimestamp);

            MessageProcessed?.Invoke(this, new MessageProcessedEventArgs(
                callContext,
                result,
                processingTime,
                dispatcherId: callContext.ProcessorId,
                success: true));

            return result;
        }
        catch (Exception ex)
        {
            var processingTime = Stopwatch.GetElapsedTime(startTimestamp);

            _logger.LogError(ex, "协议号路由失败: ProtocolId=0x{ProtocolId:X4}", header.ProtocolId);

            MessageProcessed?.Invoke(this, new MessageProcessedEventArgs(
                callContext,
                null,
                processingTime,
                dispatcherId: callContext.ProcessorId,
                success: false,
                exception: ex));

            throw;
        }
        finally
        {
            CompleteDispatch();
        }
    }

    private static ServiceCallContext CreateCallContext(MessageEnvelope message, MessageHeader header)
    {
        return new ServiceCallContext(
            connectionId: message.ConnectionId ?? string.Empty,
            messageId: message.MessageId,
            serviceName: header.ServiceName ?? "Unknown",
            methodName: header.MethodName ?? "Unknown",
            protocolId: header.ProtocolId,
            requestData: null,
            messageType: header.Type,
            receivedTime: message.ReceivedTime,
            processorId: message.ProcessorId,
            flags: header.Flags);
    }

    private void CompleteDispatch()
    {
        if (Interlocked.Decrement(ref _inFlight) == 0 && Volatile.Read(ref _state) == Stopping)
        {
            _drained.TrySetResult(null);
        }
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _state) == Disposed)
        {
            return;
        }

        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        finally
        {
            Volatile.Write(ref _state, Disposed);
            _drained.TrySetResult(null);
        }
    }
}

/// <summary>
/// 消息处理完成事件参数
/// </summary>
public sealed class MessageProcessedEventArgs : EventArgs
{
    public ServiceCallContext CallContext { get; }
    public object? Result { get; }
    public TimeSpan ProcessingTime { get; }
    public int DispatcherId { get; }
    public bool Success { get; }
    public Exception? Exception { get; }

    public MessageProcessedEventArgs(
        ServiceCallContext callContext,
        object? result,
        TimeSpan processingTime,
        int dispatcherId,
        bool success,
        Exception? exception = null)
    {
        CallContext = callContext;
        Result = result;
        ProcessingTime = processingTime;
        DispatcherId = dispatcherId;
        Success = success;
        Exception = exception;
    }
}
