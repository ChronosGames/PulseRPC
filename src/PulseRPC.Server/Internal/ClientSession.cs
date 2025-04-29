using Microsoft.Extensions.Logging;
using PulseRPC.Protocol;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.Sockets;
using MemoryPack;
using PulseRPC.Server.Auth;
using System.Security.Claims;
using System.Linq;

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
    private readonly IAuthenticationProvider? _authenticationProvider;
    private readonly IAuthorizationProvider? _authorizationProvider;
    private bool _isDisposed;
    private bool _isAuthenticated;
    private ClaimsPrincipal? _user;

    public string ClientId => _clientId;

    /// <summary>
    /// 获取当前会话的用户身份
    /// </summary>
    public ClaimsPrincipal? User => _user;

    /// <summary>
    /// 获取会话是否已认证
    /// </summary>
    public bool IsAuthenticated => _isAuthenticated;

    public ClientSession(
        TcpClient client,
        string clientId,
        ServiceRegistry serviceRegistry,
        HubRegistry hubRegistry,
        ILogger logger,
        Action<string> onDisconnected,
        IAuthenticationProvider? authenticationProvider = null,
        IAuthorizationProvider? authorizationProvider = null,
        Stream? customStream = null)
    {
        _client = client;
        _clientId = clientId;
        _serviceRegistry = serviceRegistry;
        _hubRegistry = hubRegistry;
        _logger = logger;
        _onDisconnected = onDisconnected;
        _authenticationProvider = authenticationProvider;
        _authorizationProvider = authorizationProvider;

        var stream = customStream ?? client.GetStream();
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
                    if (message != null)
                    {
                        await HandleMessageAsync(message.Value, cancellationToken);
                    }
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
    private static bool TryReadMessage(ref ReadOnlySequence<byte> buffer, out MessageEnvelope? message)
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
        message = MemoryPackSerializer.Deserialize<MessageEnvelope>(messageData);
        buffer = buffer.Slice(4 + messageLength);
        return true;
    }

    /// <summary>
    /// 处理接收到的消息
    /// </summary>
    private async Task HandleMessageAsync(MessageEnvelope message, CancellationToken cancellationToken)
    {
        switch (message.Type)
        {
            case MessageType.Request:
                var request = MemoryPackSerializer.Deserialize<PulseRequest>(message.Payload);
                await HandleRequestAsync(request!, cancellationToken);
                break;
            case MessageType.Event:
                var @event = MemoryPackSerializer.Deserialize<PulseEvent>(message.Payload);
                await HandleEventAsync(@event!, cancellationToken);
                break;
            default:
                _logger.LogWarning($"收到未知类型的消息: {message.Type}", message.GetType().Name);
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
            // 检查授权
            if (!await AuthorizeAsync(request.ServiceName, request.MethodName))
            {
                var errorResponse = new PulseResponse
                {
                    RequestId = request.RequestId,
                    IsSuccess = false,
                    ErrorMessage = "无权访问此服务或方法"
                };

                await SendResponseAsync(errorResponse, cancellationToken);
                return;
            }

            PulseResponse response;
            if (IsHubRequest(request.ServiceName))
            {
                var hubHandler = _hubRegistry.GetHandler(request.ServiceName);
                if (hubHandler == null)
                {
                    var errorResponse = new PulseResponse
                    {
                        RequestId = request.RequestId,
                        IsSuccess = false,
                        ErrorMessage = $"Hub '{request.ServiceName}' 未注册。"
                    };
                    await SendResponseAsync(errorResponse, cancellationToken);
                    return;
                }

                var hubContext = GetOrCreateHubContext(hubHandler);
                response = await hubHandler.HandleRequestAsync(hubContext, request);
            }
            else
            {
                var service = _serviceRegistry.GetHandler(request.ServiceName);
                if (service == null)
                {
                    var errorResponse = new PulseResponse
                    {
                        RequestId = request.RequestId,
                        IsSuccess = false,
                        ErrorMessage = $"服务 '{request.ServiceName}' 未注册。"
                    };
                    await SendResponseAsync(errorResponse, cancellationToken);
                    return;
                }

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
                ErrorMessage = ex.Message
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
            if (IsHubEvent(@event.HubName))
            {
                var hubHandler = _hubRegistry.GetHandler(@event.HubName);
                if (hubHandler == null)
                {
                    _logger.LogWarning("事件目标 Hub '{HubName}' 未注册", @event.HubName);
                    return;
                }

                var hubContext = GetOrCreateHubContext(hubHandler);
                await hubHandler.HandleEventAsync(hubContext, @event);
            }
            else
            {
                _logger.LogWarning("收到非 Hub 事件: {EventName}", @event.EventName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理事件时发生错误: {EventName}", @event.EventName);
        }
    }

    /// <summary>
    /// 判断是否为 Hub 请求
    /// </summary>
    private bool IsHubRequest(string serviceName)
    {
        return _hubRegistry.GetHandler(serviceName) != null;
    }

    /// <summary>
    /// 判断是否为 Hub 事件
    /// </summary>
    private bool IsHubEvent(string hubName)
    {
        return _hubRegistry.GetHandler(hubName) != null;
    }

    /// <summary>
    /// 发送响应消息
    /// </summary>
    private async Task SendResponseAsync(PulseResponse response, CancellationToken cancellationToken)
    {
        var envelope = new MessageEnvelope
        {
            Type = MessageType.Response,
            Payload = MemoryPackSerializer.Serialize(response)
        };

        await SendEnvelopeAsync(envelope, cancellationToken);
    }

    /// <summary>
    /// 发送事件消息
    /// </summary>
    public async Task SendEventAsync(PulseEvent @event, CancellationToken cancellationToken)
    {
        var envelope = new MessageEnvelope
        {
            Type = MessageType.Event,
            Payload = MemoryPackSerializer.Serialize(@event)
        };

        await SendEnvelopeAsync(envelope, cancellationToken);
    }

    /// <summary>
    /// 发送消息封装
    /// </summary>
    private async Task SendEnvelopeAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var messageBytes = MemoryPackSerializer.Serialize(envelope);
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

    /// <summary>
    /// 认证客户端
    /// </summary>
    /// <param name="credentials">认证凭证</param>
    /// <returns>认证结果</returns>
    public async Task<AuthenticationResult> AuthenticateAsync(string credentials)
    {
        if (_authenticationProvider == null)
        {
            // 如果未配置认证提供者，视为认证成功
            _isAuthenticated = true;
            _user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, _clientId),
                new Claim(ClaimTypes.Name, _clientId)
            }, "PulseRPC"));

            return AuthenticationResult.Success(_user);
        }

        var result = await _authenticationProvider.AuthenticateAsync(credentials);
        if (result.IsAuthenticated && result.User != null)
        {
            _isAuthenticated = true;
            _user = result.User;
            var userId = _user.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
                ?? _user.Identity?.Name
                ?? "未知用户";
            _logger.LogInformation("客户端 {ClientId} 认证成功: {UserId}", _clientId, userId);
        }
        else
        {
            _logger.LogWarning("客户端 {ClientId} 认证失败: {ErrorMessage}", _clientId, result.ErrorMessage);
        }

        return result;
    }

    /// <summary>
    /// 验证客户端是否有权限访问指定服务和方法
    /// </summary>
    private async Task<bool> AuthorizeAsync(string serviceName, string methodName)
    {
        // 如果未配置授权提供者，或不需要认证，则允许访问
        if (_authorizationProvider == null || _user == null)
        {
            return true;
        }

        var result = await _authorizationProvider.AuthorizeAsync(_user, serviceName, methodName);
        if (!result.IsAuthorized)
        {
            _logger.LogWarning("客户端 {ClientId} 无权访问 {ServiceName}.{MethodName}: {ErrorMessage}",
                _clientId, serviceName, methodName, result.ErrorMessage);
        }

        return result.IsAuthorized;
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
