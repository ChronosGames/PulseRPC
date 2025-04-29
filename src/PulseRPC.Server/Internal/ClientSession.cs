using Microsoft.Extensions.Logging;
using PulseRPC.Protocol;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.Sockets;
using MemoryPack;

namespace PulseRPC.Internal;

/// <summary>
/// 管理单个客户端连接的会话
/// </summary>
internal class ClientSession : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly string _clientId;
    private readonly ServiceRegistry _serviceRegistry;
    private readonly HubRegistry _hubRegistry;
    private readonly ILogger _logger;
    private readonly Action<string> _onDisconnected;
    private readonly PipeReader _reader;
    private readonly PipeWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly HashSet<string> _groups = new();
    private readonly ConcurrentDictionary<Type, HubContext> _hubContexts = new();
    private bool _isDisposed;

    public string ClientId => _clientId;

    public ClientSession(
        TcpClient client,
        string clientId,
        ServiceRegistry serviceRegistry,
        HubRegistry hubRegistry,
        ILogger logger,
        Action<string> onDisconnected)
    {
        _client = client;
        _clientId = clientId;
        _serviceRegistry = serviceRegistry;
        _hubRegistry = hubRegistry;
        _logger = logger;
        _onDisconnected = onDisconnected;

        var stream = client.GetStream();
        _reader = PipeReader.Create(stream);
        _writer = PipeWriter.Create(stream);
    }

    /// <summary>
    /// 处理客户端通信
    /// </summary>
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await _reader.ReadAsync(cancellationToken);
                var buffer = result.Buffer;

                while (TryReadMessage(ref buffer, out var message))
                {
                    await HandleMessageAsync(message, cancellationToken);
                }

                _reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 正常取消，不需要特殊处理
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理客户端 {ClientId} 的消息时发生错误", _clientId);
        }
        finally
        {
            _onDisconnected(_clientId);
        }
    }

    /// <summary>
    /// 尝试从缓冲区读取完整消息
    /// </summary>
    private bool TryReadMessage(ref ReadOnlySequence<byte> buffer, out PulseMessage? message)
    {
        message = null;

        if (buffer.Length < 4)
        {
            return false;
        }

        var reader = new SequenceReader<byte>(buffer);
        if (!reader.TryReadLittleEndian(out int messageLength))
        {
            return false;
        }

        if (buffer.Length < 4 + messageLength)
        {
            return false;
        }

        var messageData = buffer.Slice(4, messageLength);
        message = MemoryPackSerializer.Deserialize<PulseMessage>(messageData);
        buffer = buffer.Slice(4 + messageLength);
        return true;
    }

    /// <summary>
    /// 处理接收到的消息
    /// </summary>
    private async Task HandleMessageAsync(PulseMessage message, CancellationToken cancellationToken)
    {
        switch (message)
        {
            case PulseRequest request:
                await HandleRequestAsync(request, cancellationToken);
                break;
            case PulseEvent @event:
                await HandleEventAsync(@event, cancellationToken);
                break;
            default:
                _logger.LogWarning("收到未知类型的消息: {MessageType}", message.GetType().Name);
                break;
        }
    }

    /// <summary>
    /// 处理服务请求
    /// </summary>
    private async Task HandleRequestAsync(PulseRequest request, CancellationToken cancellationToken)
    {
        try
        {
            PulseResponse response;
            if (request.IsHubRequest)
            {
                var hubHandler = _hubRegistry.GetHandler(request.ServiceName);
                var hubContext = GetOrCreateHubContext(hubHandler);
                response = await hubHandler.HandleRequestAsync(hubContext, request);
            }
            else
            {
                var service = _serviceRegistry.GetHandler(request.ServiceName);
                response = await service.HandleRequestAsync(request);
            }

            await SendResponseAsync(response, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理请求时发生错误: {RequestId}", request.RequestId);
            var errorResponse = new PulseResponse
            {
                RequestId = request.RequestId,
                Error = ex.Message
            };
            await SendResponseAsync(errorResponse, cancellationToken);
        }
    }

    /// <summary>
    /// 处理事件消息
    /// </summary>
    private async Task HandleEventAsync(PulseEvent @event, CancellationToken cancellationToken)
    {
        try
        {
            if (@event.IsHubEvent)
            {
                var hubHandler = _hubRegistry.GetHandler(@event.ServiceName);
                var hubContext = GetOrCreateHubContext(hubHandler);
                await hubHandler.HandleEventAsync(hubContext, @event);
            }
            else
            {
                _logger.LogWarning("收到非 Hub 事件: {EventName}", @event.MethodName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理事件时发生错误: {EventName}", @event.MethodName);
        }
    }

    /// <summary>
    /// 发送响应消息
    /// </summary>
    private async Task SendResponseAsync(PulseResponse response, CancellationToken cancellationToken)
    {
        await SendMessageAsync(response, cancellationToken);
    }

    /// <summary>
    /// 发送事件消息
    /// </summary>
    public async Task SendEventAsync(PulseEvent @event, CancellationToken cancellationToken)
    {
        await SendMessageAsync(@event, cancellationToken);
    }

    /// <summary>
    /// 发送消息的底层实现
    /// </summary>
    private async Task SendMessageAsync<T>(T message, CancellationToken cancellationToken) where T : PulseMessage
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var messageBytes = MemoryPackSerializer.Serialize(message);
            var lengthBytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, messageBytes.Length);

            await _writer.WriteAsync(lengthBytes, cancellationToken);
            await _writer.WriteAsync(messageBytes, cancellationToken);
            await _writer.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// 获取或创建 Hub 上下文
    /// </summary>
    private HubContext GetOrCreateHubContext(HubHandler handler)
    {
        return _hubContexts.GetOrAdd(handler.GetType(), _ => handler.CreateContext(this));
    }

    /// <summary>
    /// 将客户端添加到指定分组
    /// </summary>
    public void AddToGroup(string groupName)
    {
        _groups.Add(groupName);
    }

    /// <summary>
    /// 将客户端从指定分组移除
    /// </summary>
    public void RemoveFromGroup(string groupName)
    {
        _groups.Remove(groupName);
    }

    /// <summary>
    /// 检查客户端是否在指定分组中
    /// </summary>
    public bool IsInGroup(string groupName)
    {
        return _groups.Contains(groupName);
    }

    /// <summary>
    /// 获取客户端所在的所有分组
    /// </summary>
    public IEnumerable<string> GetGroups()
    {
        return _groups.ToList();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        try
        {
            foreach (var hubContext in _hubContexts.Values)
            {
                await hubContext.DisposeAsync();
            }

            await _reader.CompleteAsync();
            await _writer.CompleteAsync();
            _client.Dispose();
            _writeLock.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理客户端会话资源时发生错误: {ClientId}", _clientId);
        }
    }
}
