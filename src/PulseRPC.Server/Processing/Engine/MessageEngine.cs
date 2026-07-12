using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PulseRPC.Memory;
using PulseRPC.Messaging;
using PulseRPC.Scheduling;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Processing.Memory;
using PulseRPC.Server.Processing.Pipeline;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Services.Scheduling;
using PulseRPC.Server.Processing.Serialization;
using PulseRPC.Server.Security;
using PulseRPC.Server.Transport;
using PulseRPC.Shared;
using PulseRPC.Diagnostics;
using MessageStatus = PulseRPC.Server.Processing.Memory.MessageStatus;
using MessageParsedEventArgs = PulseRPC.Server.Transport.MessageParsedEventArgs;
using MessageProcessedEventArgs = PulseRPC.Server.Processing.Engine.MessageProcessedEventArgs;

namespace PulseRPC.Server.Processing.Engine;

/// <summary>
/// 固定分片消息引擎。连接注册时以 round-robin 绑定到固定 worker shard；
/// 每个 shard 使用单消费者有界队列，队列满时立即拒绝，不创建每连接 worker，
/// 也不运行 adaptive 或 L3 调度循环。
/// </summary>
internal sealed class MessageEngine : IAsyncDisposable, ITieredMessageEngine
{
    #region 核心组件和字段

    private readonly IMessageDispatcher _messageDispatcher;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MessageEngine> _logger;
    private readonly IServerChannelManager _channelManager;
    private readonly IResponseProcessor _responseProcessor;
    private readonly ConcurrentDictionary<Guid, RequestCancellation> _requestCancellations = new();

    // 固定 shard：连接只保存轻量 generation/lease，不再按连接创建 worker、队列和调度器。
    private readonly MessageWorkerShard[] _workerShards;
    private readonly ConcurrentDictionary<string, MessageConnectionLease> _connections = new();
    private int _nextShardIndex = -1;

    // 性能监控和统计
    private EngineMetrics _metrics;

    // 生命周期管理
    private CancellationTokenSource _cancellationTokenSource;
    private readonly object _lifecycleLock = new();
    private readonly ConcurrentDictionary<long, Task> _connectionDeactivationTasks = new();
    private long _connectionDeactivationTaskId;
    private bool _acceptingConnections;
    private Task? _startTask;
    private Task? _stopTask;
    private Task? _disposeTask;
    private bool _dispatcherStopRequested;
    private bool _responseStopRequested;

    #endregion

    #region 构造函数和初始化

    /// <summary>
    /// 构造高性能消息引擎 (使用 IMessageDispatcher)
    /// </summary>
    public MessageEngine(
        IMessageDispatcher messageDispatcher,
        IServiceProvider serviceProvider,
        IOptions<PulseServerOptions> configuration,
        ILogger<MessageEngine> logger,
        IServerChannelManager serverChannelManager,
        IResponseProcessor responseProcessor)
    {
        _messageDispatcher = messageDispatcher ?? throw new ArgumentNullException(nameof(messageDispatcher));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _channelManager =  serverChannelManager ?? throw new ArgumentNullException(nameof(serverChannelManager));
        _responseProcessor = responseProcessor ?? throw new ArgumentNullException(nameof(responseProcessor));
        var options = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MessageWorkerShardCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MessageQueueCapacityPerShard, 1);

        _cancellationTokenSource = new CancellationTokenSource();

        // 初始化监控组件
        _metrics = new EngineMetrics();

        var engineId = $"engine-{Guid.NewGuid():N}";
        var handler = CreateMessageHandler();
        var shards = new List<MessageWorkerShard>(options.MessageWorkerShardCount);
        var connectedSubscribed = false;
        var disconnectedSubscribed = false;
        var messageSubscribed = false;
        try
        {
            for (var index = 0; index < options.MessageWorkerShardCount; index++)
            {
                shards.Add(new MessageWorkerShard(
                $"{engineId}/shard-{index}",
                options.MessageQueueCapacityPerShard,
                handler,
                OnMessageSlotFinalized,
                _logger));
            }

            _workerShards = shards.ToArray();

            SafeLog(() => _logger.LogInformation(
                "MessageEngine初始化完成: WorkerShards={WorkerShards}, QueueCapacityPerShard={QueueCapacityPerShard}",
                _workerShards.Length,
                options.MessageQueueCapacityPerShard));

            _channelManager.ChannelConnected += OnChannelConnected;
            connectedSubscribed = true;
            _channelManager.ChannelDisconnected += OnChannelDisconnected;
            disconnectedSubscribed = true;
            _channelManager.ChannelMessageParsed += OnChannelMessageParsed;
            messageSubscribed = true;
        }
        catch
        {
            if (messageSubscribed)
            {
                try { _channelManager.ChannelMessageParsed -= OnChannelMessageParsed; } catch { }
            }

            if (disconnectedSubscribed)
            {
                try { _channelManager.ChannelDisconnected -= OnChannelDisconnected; } catch { }
            }

            if (connectedSubscribed)
            {
                try { _channelManager.ChannelConnected -= OnChannelConnected; } catch { }
            }

            try
            {
                Task.WhenAll(shards.Select(shard => shard.DisposeAsync().AsTask()))
                    .GetAwaiter()
                    .GetResult();
            }
            finally
            {
                _cancellationTokenSource.Dispose();
            }

            throw;
        }
    }

    internal IReadOnlyList<string> WorkerShardIds => _workerShards.Select(shard => shard.ShardId).ToArray();
    internal int TrackedConnectionDeactivationCount => _connectionDeactivationTasks.Count;
    internal int PendingRequestCancellationCount => _requestCancellations.Count;

    internal bool TryGetWorkerShardIndex(string connectionId, out int shardIndex)
    {
        if (_connections.TryGetValue(connectionId, out var connectionLease))
        {
            shardIndex = connectionLease.ShardIndex;
            return true;
        }

        shardIndex = -1;
        return false;
    }

    /// <summary>
    /// 创建消息处理委托
    /// </summary>
    private Func<MessageSlot, CancellationToken, ValueTask<ProcessingResult>> CreateMessageHandler()
    {
        return async (messageSlot, cancellationToken) =>
        {
            var startTime = Stopwatch.GetTimestamp();

            try
            {
                // 将MessageSlot转换为MessageEnvelope进行处理
                // 现在保留完整的元数据
                var envelope = new MessageEnvelope
                {
                    MessageId = messageSlot.MessageId,
                    ConnectionId = messageSlot.ConnectionId, // 使用真实连接ID
                    Header = messageSlot.Header, // 保留完整消息头
                    Payload = messageSlot.Payload, // 零拷贝传递
                    Priority = messageSlot.Priority,
                    EnqueueTime = messageSlot.EnqueueTime,
                    Status = messageSlot.Status,
                    ConnectionLease = messageSlot.ConnectionLease
                };

                // 使用现有的消息处理逻辑
                var response = await ProcessSingleMessage(envelope, cancellationToken);

                var processingTime = Stopwatch.GetElapsedTime(startTime);
                _metrics.MessagesProcessed.Add(1);
                _metrics.RecordBatchProcessingTime(processingTime);

                return ProcessingResult.SuccessResult(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理消息时发生错误: MessageId={MessageId}, ConnectionId={ConnectionId}",
                    messageSlot.MessageId, messageSlot.ConnectionId);
                return ProcessingResult.FailResult(ex.Message);
            }
        };
    }

    #endregion

    #region 公共API - 消息处理

    /// <summary>
    /// 启动消息引擎
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleLock)
        {
            if (_disposeTask is not null)
            {
                return Task.FromException(new ObjectDisposedException(nameof(MessageEngine)));
            }

            if (_stopTask is not null)
            {
                return Task.FromException(new InvalidOperationException("MessageEngine cannot be restarted after stop."));
            }

            _startTask ??= StartCoreAsync(cancellationToken);
            return _startTask;
        }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _cancellationTokenSource.Token);
        var dispatcherStarted = false;
        var responseStarted = false;

        _logger.LogInformation("启动MessageEngine");
        try
        {
            _metrics.EngineStartTime = DateTime.UtcNow;

            await _messageDispatcher.StartAsync(combinedCts.Token).ConfigureAwait(false);
            dispatcherStarted = true;
            responseStarted = true;
            await _responseProcessor.StartAsync(combinedCts.Token).ConfigureAwait(false);
            combinedCts.Token.ThrowIfCancellationRequested();

            lock (_lifecycleLock)
            {
                if (_stopTask is not null)
                {
                    throw new OperationCanceledException("MessageEngine stopped while starting.");
                }

                _acceptingConnections = true;
            }

            _logger.LogInformation("MessageEngine启动成功");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MessageEngine启动失败");

            bool stopOwnsRollback;
            lock (_lifecycleLock)
            {
                _acceptingConnections = false;
                stopOwnsRollback = _stopTask is not null;
            }

            if (!stopOwnsRollback)
            {
                if (responseStarted)
                {
                    try
                    {
                        await StopResponseProcessorOnceAsync().ConfigureAwait(false);
                    }
                    catch (Exception rollbackException)
                    {
                        _logger.LogError(rollbackException,
                            "回滚MessageEngine响应处理器失败");
                    }
                }

                if (dispatcherStarted)
                {
                    try
                    {
                        await StopDispatcherOnceAsync().ConfigureAwait(false);
                    }
                    catch (Exception rollbackException)
                    {
                        _logger.LogError(rollbackException,
                            "回滚MessageEngine消息分发器失败");
                    }
                }
            }

            throw;
        }
    }

    /// <summary>
    /// 高性能消息入队 - L1快速路径（接受完整消息包）
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryEnqueueMessage(
        string connectionId,
        MessagePacketHolder messagePacket,
        MessagePriority priority = MessagePriority.Normal)
        => TryEnqueueMessageCore(
            connectionId,
            messagePacket,
            priority,
            sourceChannel: null,
            requireLogicalConnection: true);

    public bool TryEnqueueMessage(
        IServerChannel sourceChannel,
        MessagePacketHolder messagePacket,
        MessagePriority priority = MessagePriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(sourceChannel);
        return TryEnqueueMessageCore(
            sourceChannel.ConnectionId,
            messagePacket,
            priority,
            sourceChannel,
            requireLogicalConnection: false);
    }

    private bool TryEnqueueMessageCore(
        string connectionId,
        MessagePacketHolder messagePacket,
        MessagePriority priority,
        IServerChannel? sourceChannel,
        bool requireLogicalConnection = false)
    {
        var startTicks = Stopwatch.GetTimestamp();
        MessageConnectionLease? connectionLease = null;
        RequestCancellation? requestCancellation = null;
        var leaseAcquired = false;
        var ownershipTransferred = false;

        try
        {
            if (!_connections.TryGetValue(connectionId, out connectionLease) ||
                (requireLogicalConnection && connectionLease.Channel is not null) ||
                (sourceChannel is not null && !ReferenceEquals(connectionLease.Channel, sourceChannel)) ||
                !connectionLease.TryAcquire())
            {
                return RejectSlot(messagePacket);
            }

            leaseAcquired = true;

            if (messagePacket.Header.Type == MessageType.Cancel)
            {
                HandleCancelMessage(connectionLease, messagePacket.Header.MessageId);
                connectionLease.Release();
                leaseAcquired = false;
                messagePacket.Dispose();
                return true;
            }

            if (messagePacket.Header.Type == MessageType.Request)
            {
                requestCancellation = new RequestCancellation(
                    connectionLease,
                    CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token));

                if (!_requestCancellations.TryAdd(messagePacket.Header.MessageId, requestCancellation))
                {
                    requestCancellation.Dispose();
                    connectionLease.Release();
                    leaseAcquired = false;
                    return RejectSlot(messagePacket);
                }
            }

            var slot = new MessageSlot
            {
                MessageId = messagePacket.Header.MessageId,
                ConnectionId = connectionId,
                Header = messagePacket.Header,
                Payload = messagePacket.Payload, // 零拷贝（指向 holder 池化缓冲）
                PayloadOwner = messagePacket,    // 所有权移交给 slot
                Priority = priority,
                EnqueueTime = startTicks,
                Status = MessageStatus.Pending,
                ConnectionLease = connectionLease
            };

            if (!_workerShards[connectionLease.ShardIndex].TryEnqueue(slot))
            {
                _metrics.BackpressureEvents.Add(1);
                _metrics.MessagesDropped.Add(1);
                leaseAcquired = false;
                FinalizeRejectedSlot(slot);
                return false;
            }

            ownershipTransferred = true;
            leaseAcquired = false;
            _metrics.L1MessagesEnqueued.Add(1);
            _metrics.RecordEnqueueLatency(Stopwatch.GetTimestamp() - startTicks);

            return true;
        }
        catch (Exception ex)
        {
            _metrics.EnqueueErrors.Add(1);
            try
            {
                _logger.LogWarning(ex, "消息入队失败: ConnectionId={ConnectionId}", connectionId);
            }
            catch
            {
                // Logging must not interrupt lease and payload-owner rollback.
            }

            if (ownershipTransferred)
            {
                return true;
            }

            if (requestCancellation != null)
            {
                RemoveRequestCancellation(messagePacket.Header.MessageId);
            }

            if (leaseAcquired)
            {
                connectionLease?.Release();
            }

            return RejectSlot(messagePacket);
        }
    }

    /// <summary>
    /// 统一「拒绝入队」路径的载荷归还：将 slot/holder 持有的池化缓冲释放，并返回 <c>false</c>。
    /// <see cref="MessagePacketHolder.Dispose"/> 幂等（Interlocked 守卫），
    /// 即便调用方误判所有权状态重复调用本方法也不会二次归还池或抛异常。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool RejectSlot(IDisposable? payloadOwner)
    {
        payloadOwner?.Dispose();
        return false;
    }

    private void FinalizeRejectedSlot(MessageSlot slot)
    {
        try
        {
            slot.PayloadOwner?.Dispose();
        }
        finally
        {
            try
            {
                OnMessageSlotFinalized(slot);
            }
            finally
            {
                slot.ConnectionLease?.Release();
            }
        }
    }

    /// <summary>
    /// 注册连接上下文
    /// </summary>
    public void RegisterConnection(string connectionId)
        => RegisterConnection(connectionId, channel: null);

    public void RegisterConnection(IServerChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        RegisterConnection(channel.ConnectionId, channel);
    }

    private void RegisterConnection(string connectionId, IServerChannel? channel)
    {
        lock (_lifecycleLock)
        {
            if (!_acceptingConnections)
            {
                _logger.LogDebug("消息引擎正在停止，忽略连接注册: ConnectionId={ConnectionId}", connectionId);
                return;
            }

            if (_connections.TryGetValue(connectionId, out var existingLease))
            {
                if (channel is null || ReferenceEquals(existingLease.Channel, channel))
                {
                    return;
                }

                _connections.TryRemove(
                    new KeyValuePair<string, MessageConnectionLease>(connectionId, existingLease));
                TrackConnectionDeactivationLocked(existingLease);
            }

            var shardIndex = (int)((uint)Interlocked.Increment(ref _nextShardIndex) % (uint)_workerShards.Length);
            var connectionLease = new MessageConnectionLease(
                connectionId,
                shardIndex,
                _workerShards[shardIndex].ShutdownToken,
                _logger,
                channel);
            _connections[connectionId] = connectionLease;
            _metrics.ActiveConnections.Set(_connections.Count);
        }

        _logger.LogDebug("连接已注册: ConnectionId={ConnectionId}", connectionId);
    }

    /// <summary>
    /// 注销连接
    /// </summary>
    public void UnregisterConnection(string connectionId)
        => UnregisterConnection(connectionId, channel: null, requireLogicalConnection: true);

    public void UnregisterConnection(IServerChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        UnregisterConnection(channel.ConnectionId, channel, requireLogicalConnection: false);
    }

    private void UnregisterConnection(
        string connectionId,
        IServerChannel? channel,
        bool requireLogicalConnection = false)
    {
        lock (_lifecycleLock)
        {
            if (_connections.TryGetValue(connectionId, out var connectionLease) &&
                (!requireLogicalConnection || connectionLease.Channel is null) &&
                (channel is null || ReferenceEquals(connectionLease.Channel, channel)) &&
                _connections.TryRemove(
                    new KeyValuePair<string, MessageConnectionLease>(connectionId, connectionLease)))
            {
                TrackConnectionDeactivationLocked(connectionLease);
                _metrics.ActiveConnections.Set(_connections.Count);
                _logger.LogDebug("连接已注销: ConnectionId={ConnectionId}", connectionId);
            }
        }
    }

    private void OnChannelConnected(object? sender, ChannelEventArgs e)
    {
        RegisterConnection(e.Channel);
    }

    private void OnChannelDisconnected(object? sender, ChannelEventArgs e)
        => UnregisterConnection(e.Channel);

    private void OnChannelMessageParsed(object? sender, PulseRPC.Server.Transport.MessageParsedEventArgs eventArgs)
    {
        // 将消息路由到引擎
        // 传递完整消息包而非仅 Payload
        var header = eventArgs.MessagePacket.Header;
        var priority = DetermineMessagePriority(header);

        var success = TryEnqueueMessageCore(
            eventArgs.ConnectionId,
            eventArgs.MessagePacket, // 传递完整结构
            priority,
            sender as IServerChannel);

        if (success)
        {
            try
            {
                _logger.LogTrace(
                    "[消息路由] {ConnectionId} 消息已成功路由到引擎: 服务={ServiceName}, 方法={MethodName}, MessageId={MessageId}",
                    eventArgs.ConnectionId,
                    header.ServiceName,
                    header.MethodName,
                    header.MessageId);
            }
            catch
            {
                // The holder now belongs to a shard; this handler must not signal failure upstream.
            }
        }
        else
        {
            try
            {
                _logger.LogWarning(
                    "[消息路由] {ConnectionId} 消息入队失败",
                    eventArgs.ConnectionId);
            }
            catch
            {
                // Rejection already returned the holder; logging cannot change ownership.
            }
        }
    }

    /// <summary>
    /// 根据消息头确定消息优先级
    /// </summary>
    private static MessagePriority DetermineMessagePriority(MessageHeader header)
    {
        // 可以根据服务名、方法名或其他头信息确定优先级
        // 这里使用简单的策略
        return header.Type switch
        {
            MessageType.Request => MessagePriority.Normal,
            MessageType.Response => MessagePriority.High,
            MessageType.Event => MessagePriority.Low,
            _ => MessagePriority.Normal
        };
    }

    #endregion

    #region 核心处理逻辑

    /// <summary>
    /// 处理单个消息
    /// </summary>
    private async Task<MessageResponse?> ProcessSingleMessage(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        RequestCancellation? requestCancellation = null;
        CancellationTokenSource? linkedRequestCts = null;

        try
        {
            envelope.Status = MessageStatus.Processing;
            var dispatchToken = cancellationToken;

            if (envelope.Header.Type == MessageType.Request &&
                _requestCancellations.TryGetValue(envelope.MessageId, out requestCancellation))
            {
                linkedRequestCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    requestCancellation.CancellationToken);
                dispatchToken = linkedRequestCts.Token;
            }

            dispatchToken.ThrowIfCancellationRequested();

            // 使用消息分发器处理消息
            object? result = null;

            try
            {
                // 不再需要重新解析 MessagePacket，直接使用 envelope.Header
                var serviceName = envelope.Header.ServiceName ?? "Unknown";
                var methodName = envelope.Header.MethodName ?? "Unknown";
                var serviceKey = envelope.Header.ServiceKey ?? string.Empty;

                _logger.LogTrace("处理消息: Service={ServiceName}, Method={MethodName}, MessageId={MessageId}, ConnectionId={ConnectionId}",
                    serviceName, methodName, envelope.MessageId, envelope.ConnectionId);

                object? dispatchResult = null;

                // ✅ 设置 PulseContext（统一请求上下文）
                // A queued request belongs to one exact connection generation. Looking the
                // channel up by ID here can expose a late old request to a newly reconnected
                // client's authentication context.
                var channel = envelope.ConnectionLease?.Channel
                    ?? _channelManager.GetChannel(envelope.ConnectionId);
                PulseContext.ContextScope contextScope = default;
                var hasContext = false;

                if (channel is ServerTransportChannel serverChannel)
                {
                    var authContext = serverChannel.AuthenticationContext;

                    // 创建统一上下文，包含 RPC、认证和传输信息
                    PulseContextData context;
                    if (authContext != null && authContext.IsAuthenticated)
                    {
                        context = PulseContextData.FromAuthenticationContext(authContext, serverChannel.Transport);
                        context = context with
                        {
                            RequestId = envelope.MessageId,
                            ConnectionId = envelope.ConnectionId,
                            ServiceName = serviceName,
                            ServiceKey = serviceKey,
                            MethodName = methodName,
                            CancellationToken = dispatchToken,
                        };
                    }
                    else
                    {
                        context = PulseContextData.CreateAnonymousClientContext(
                            envelope.ConnectionId,
                            serverChannel.Transport,
                            dispatchToken);
                        context = context with
                        {
                            RequestId = envelope.MessageId,
                            ServiceName = serviceName,
                            ServiceKey = serviceKey,
                            MethodName = methodName
                        };
                    }

                    contextScope = PulseContext.SetContext(context);
                    hasContext = true;
                }

                // 🔐 ClientFacing 与声明式授权由源生成路由表在反序列化/实例激活前统一强制。

                try
                {
                    dispatchResult = await DispatchWithHostPolicyAsync(
                        envelope,
                        dispatchToken);
                }
                finally
                {
                    // ✅ 清理请求上下文
                    if (hasContext)
                    {
                        contextScope.Dispose();
                    }
                }

                result = dispatchResult;
            }
            catch (OperationCanceledException) when (dispatchToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception dispatchEx)
            {
                _logger.LogError(dispatchEx, "消息分发失败: MessageId={MessageId}, ConnectionId={ConnectionId}",
                    envelope.MessageId, envelope.ConnectionId);

                // 如果分发失败（例如 Hub 方法抛出业务异常），仍需通知 ResponseProcessor
                // 把 Error 响应回传给客户端；否则客户端会一直等待直到请求超时（见 §11 回归发现）。
                envelope.Status = MessageStatus.Failed;
                await TriggerMessageProcessedEventAsync(envelope, null, dispatchEx);

                return new MessageResponse
                {
                    MessageId = envelope.MessageId.ToString(),
                    ConnectionId = envelope.ConnectionId ?? string.Empty,
                    Success = false,
                    ErrorMessage = $"消息分发失败: {dispatchEx.Message}",
                    ProcessingTime = TimeSpan.FromTicks(Stopwatch.GetTimestamp() - envelope.EnqueueTime)
                };
            }

            envelope.Status = MessageStatus.Completed;

            var response = new MessageResponse
            {
                MessageId = envelope.MessageId.ToString(),
                ConnectionId = envelope.ConnectionId ?? string.Empty,
                Success = true,
                Data = result,
                ProcessingTime = TimeSpan.FromTicks(Stopwatch.GetTimestamp() - envelope.EnqueueTime)
            };

            await TriggerMessageProcessedEventAsync(envelope, result, null);

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested ||
                                                requestCancellation?.IsCancellationRequested == true)
        {
            envelope.Status = MessageStatus.Failed;
            _metrics.MessagesDropped.Add(1);
            _logger.LogDebug("消息处理被取消: MessageId={MessageId}, ConnectionId={ConnectionId}",
                envelope.MessageId, envelope.ConnectionId);

            return new MessageResponse
            {
                MessageId = envelope.MessageId.ToString(),
                ConnectionId = envelope.ConnectionId,
                Success = false,
                ErrorMessage = "消息处理被取消。",
                ProcessingTime = TimeSpan.FromTicks(Stopwatch.GetTimestamp() - envelope.EnqueueTime)
            };
        }
        catch (Exception ex)
        {
            envelope.Status = MessageStatus.Failed;
            _metrics.MessagesErrored.Add(1);

            _logger.LogWarning(ex, "消息处理失败: MessageId={MessageId}, ConnectionId={ConnectionId}",
                envelope.MessageId, envelope.ConnectionId);

            var response = new MessageResponse
            {
                MessageId = envelope.MessageId.ToString(),
                ConnectionId = envelope.ConnectionId,
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTime = TimeSpan.FromTicks(Stopwatch.GetTimestamp() - envelope.EnqueueTime)
            };

            await TriggerMessageProcessedEventAsync(envelope, null, ex);

            return response;
        }
        finally
        {
            linkedRequestCts?.Dispose();
            // RequestCancellation 由 MessageSlot 的统一终结点移除，覆盖处理完成、Deadline 和关闭排空，
            // 也避免旧 slot 在 MessageId 重用窗口误删新请求状态。
        }
    }

    private async ValueTask<object?> DispatchWithHostPolicyAsync(
        MessageEnvelope envelope,
        CancellationToken cancellationToken)
    {
        using var gateScope = ClientFacingGate.EnterHostPolicyScope(_serviceProvider);
        return await _messageDispatcher.DispatchAsync(
            envelope,
            _serviceProvider,
            cancellationToken).ConfigureAwait(false);
    }

    private void HandleCancelMessage(MessageConnectionLease connectionLease, Guid messageId)
    {
        if (!_requestCancellations.TryGetValue(messageId, out var requestCancellation))
        {
            _logger.LogDebug("收到未匹配的取消帧: ConnectionId={ConnectionId}, MessageId={MessageId}",
                connectionLease.ConnectionId,
                messageId);
            return;
        }

        if (!ReferenceEquals(requestCancellation.ConnectionLease, connectionLease))
        {
            _logger.LogWarning("拒绝跨 connection generation 取消请求: ConnectionId={ConnectionId}, MessageId={MessageId}",
                connectionLease.ConnectionId,
                messageId);
            return;
        }

        try
        {
            requestCancellation.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取消请求时回调异常: ConnectionId={ConnectionId}, MessageId={MessageId}",
                connectionLease.ConnectionId, messageId);
        }
    }

    private void RemoveRequestCancellation(Guid messageId)
    {
        if (_requestCancellations.TryRemove(messageId, out var requestCancellation))
        {
            requestCancellation.Dispose();
        }
    }

    private void OnMessageSlotFinalized(MessageSlot slot)
    {
        if (slot.Header?.Type == MessageType.Request)
        {
            RemoveRequestCancellation(slot.MessageId);
        }
    }

    /// <summary>
    /// 将消息处理结果提交到有界响应队列，并传播背压。
    /// </summary>
    private async ValueTask TriggerMessageProcessedEventAsync(
        MessageEnvelope envelope,
        object? result,
        Exception? exception)
    {
        var connectionLease = envelope.ConnectionLease;
        if (connectionLease is not null &&
            (!connectionLease.IsActive ||
             !_connections.TryGetValue(envelope.ConnectionId, out var currentLease) ||
             !ReferenceEquals(currentLease, connectionLease)))
        {
            _logger.LogDebug(
                "跳过旧 connection generation 的响应: ConnectionId={ConnectionId}, MessageId={MessageId}",
                envelope.ConnectionId,
                envelope.MessageId);
            return;
        }

        try
        {
            // 创建 ServiceCallContext 以匹配新的 MessageProcessedEventArgs 构造函数
            var callContext = new ServiceCallContext(
                connectionId: envelope.ConnectionId,
                messageId: envelope.MessageId,
                serviceName: envelope.Header.ServiceName,
                methodName: envelope.Header.MethodName,
                protocolId: envelope.Header.ProtocolId,
                requestData: null, // 已处理完成，不需要再传递原始请求数据
                messageType: envelope.Header.Type,
                receivedTime: envelope.ReceivedTime,
                processorId: envelope.ProcessorId,
                flags: envelope.Header.Flags)
            {
                ExpectedChannel = connectionLease?.Channel
            };

            var processingTime = TimeSpan.FromTicks(Stopwatch.GetTimestamp() - envelope.EnqueueTime);

            var eventArgs = new MessageProcessedEventArgs(
                callContext: callContext,
                result: result,
                processingTime: processingTime,
                dispatcherId: envelope.ProcessorId,
                success: exception == null,
                exception: exception);

            await _responseProcessor.ProcessMessageResultAsync(eventArgs).ConfigureAwait(false);

            _logger.LogTrace("触发MessageProcessed事件: ConnectionId={ConnectionId}, MessageId={MessageId}, Success={Success}",
                envelope.ConnectionId, envelope.MessageId, exception == null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "触发MessageProcessed事件失败: ConnectionId={ConnectionId}, MessageId={MessageId}",
                envelope.ConnectionId, envelope.MessageId);
        }
    }

    #endregion

    #region 性能监控

    /// <summary>
    /// 获取引擎统计信息
    /// </summary>
    public EngineStatistics GetStatistics()
    {
        var queueUtilization = _workerShards.Average(shard => shard.Utilization);
        _metrics.SetCurrentL1Utilization(queueUtilization);
        var latency = _metrics.GetLatencySnapshot();

        return new EngineStatistics
        {
            UpTime = DateTime.UtcNow - _metrics.EngineStartTime,

            // L1统计
            L1BufferUtilization = queueUtilization,
            L1MessagesEnqueued = _metrics.L1MessagesEnqueued.Value,
            L1BackpressureEvents = _metrics.BackpressureEvents.Value,

            // 处理统计
            TotalMessagesProcessed = _metrics.MessagesProcessed.Value,
            TotalMessagesDropped = _metrics.MessagesDropped.Value,
            TotalBatchesProcessed = _metrics.BatchesProcessed.Value,

            // 性能指标
            CurrentThroughput = _metrics.GetCurrentThroughput(),
            AverageLatencyMs = latency.AverageMilliseconds,
            P50LatencyMs = latency.GetPercentileMilliseconds(0.50),
            P95LatencyMs = latency.GetPercentileMilliseconds(0.95),
            P99LatencyMs = latency.GetPercentileMilliseconds(0.99),
            MaxLatencyMs = TimeSpan.FromTicks(latency.MaxTicks).TotalMilliseconds,
            LatencySampleCount = latency.Count,

            // 连接统计
            ActiveConnections = _connections.Count,

            // 内存统计
            MemoryPoolStatistics = null
        };
    }

    #endregion

    #region 资源清理

    /// <summary>
    /// 停止消息引擎
    /// </summary>
    public Task StopAsync()
    {
        lock (_lifecycleLock)
        {
            return GetOrCreateStopTaskLocked();
        }
    }

    private Task GetOrCreateStopTaskLocked()
    {
        if (_stopTask != null)
        {
            return _stopTask;
        }

        _acceptingConnections = false;

        foreach (var connectionLease in _connections.Values)
        {
            TrackConnectionDeactivationLocked(connectionLease);
        }

        _connections.Clear();
        _metrics.ActiveConnections.Set(0);

        var connectionDeactivations = _connectionDeactivationTasks.Values.ToArray();
        _stopTask = StopCoreAsync(connectionDeactivations, _startTask);
        return _stopTask;
    }

    private async Task StopCoreAsync(Task[] connectionDeactivations, Task? startTask)
    {
        SafeLog(() => _logger.LogInformation("停止MessageEngine"));

        try
        {
            await _cancellationTokenSource.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 外部取消回调失败不能中断 shard 释放，否则有界队列和 worker 会继续存活。
            SafeLog(() => _logger.LogError(ex, "停止MessageEngine时取消回调异常"));
        }

        if (startTask is not null)
        {
            try
            {
                await startTask.ConfigureAwait(false);
            }
            catch
            {
                // Start failure is observed by its caller; stop owns the remaining rollback.
            }
        }

        await Task.WhenAll(connectionDeactivations).ConfigureAwait(false);

        // 先关闭响应输入以解除 shard 在有界响应队列上的等待，再等待 worker 退出。
        var responseStopTask = StopResponseProcessorSafelyAsync();
        await Task.WhenAll(_workerShards.Select(shard => shard.DisposeAsync().AsTask())).ConfigureAwait(false);

        Exception? dispatcherStopException = null;
        try
        {
            await StopDispatcherOnceAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            dispatcherStopException = ex;
            SafeLog(() => _logger.LogError(ex, "停止消息分发器失败"));
        }

        var responseStopException = await responseStopTask.ConfigureAwait(false);
        if (dispatcherStopException is not null && responseStopException is not null)
        {
            throw new AggregateException(
                "停止MessageEngine下游组件失败",
                dispatcherStopException,
                responseStopException);
        }

        if (dispatcherStopException is not null)
        {
            throw new AggregateException("停止消息分发器失败", dispatcherStopException);
        }

        if (responseStopException is not null)
        {
            throw new AggregateException("停止响应处理器失败", responseStopException);
        }

        SafeLog(() => _logger.LogInformation("MessageEngine已停止"));
    }

    private async Task<Exception?> StopResponseProcessorSafelyAsync()
    {
        try
        {
            await StopResponseProcessorOnceAsync().ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            SafeLog(() => _logger.LogError(ex, "停止响应处理器失败"));
            return ex;
        }
    }

    private static void SafeLog(Action logAction)
    {
        try
        {
            logAction();
        }
        catch
        {
            // Worker, queue and downstream cleanup must not depend on a logger provider.
        }
    }

    private Task StopDispatcherOnceAsync()
    {
        lock (_lifecycleLock)
        {
            if (_dispatcherStopRequested)
            {
                return Task.CompletedTask;
            }

            _dispatcherStopRequested = true;
            return _messageDispatcher.StopAsync();
        }
    }

    private Task StopResponseProcessorOnceAsync()
    {
        lock (_lifecycleLock)
        {
            if (_responseStopRequested)
            {
                return Task.CompletedTask;
            }

            _responseStopRequested = true;
            return _responseProcessor.StopAsync();
        }
    }

    /// <summary>
    /// 异步资源释放
    /// </summary>
    public ValueTask DisposeAsync()
    {
        lock (_lifecycleLock)
        {
            if (_disposeTask == null)
            {
                _disposeTask = DisposeCoreAsync(GetOrCreateStopTaskLocked());
            }

            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync(Task stopTask)
    {
        SafeLog(() => _logger.LogInformation("释放MessageEngine资源"));

        try
        {
            await stopTask.ConfigureAwait(false);
        }
        finally
        {
            foreach (var kvp in _requestCancellations)
            {
                if (_requestCancellations.TryRemove(kvp.Key, out var requestCancellation))
                {
                    try
                    {
                        requestCancellation.Dispose();
                    }
                    catch (Exception ex)
                    {
                        SafeLog(() => _logger.LogError(
                            ex,
                            "释放请求取消状态失败: MessageId={MessageId}",
                            kvp.Key));
                    }
                }
            }

            try { _channelManager.ChannelConnected -= OnChannelConnected; } catch { }
            try { _channelManager.ChannelDisconnected -= OnChannelDisconnected; } catch { }
            try { _channelManager.ChannelMessageParsed -= OnChannelMessageParsed; } catch { }
            _cancellationTokenSource.Dispose();

            SafeLog(() => _logger.LogInformation("MessageEngine资源释放完成"));
        }
    }

    /// <summary>
    /// 同步释放资源
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private void TrackConnectionDeactivationLocked(MessageConnectionLease connectionLease)
    {
        var taskId = Interlocked.Increment(ref _connectionDeactivationTaskId);
        var task = DeactivateConnectionAsync(connectionLease);
        _connectionDeactivationTasks[taskId] = task;

        _ = task.ContinueWith(
            completedTask =>
            {
                _connectionDeactivationTasks.TryRemove(taskId, out _);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task DeactivateConnectionAsync(MessageConnectionLease connectionLease)
    {
        try
        {
            await connectionLease.DeactivateAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "停用连接消息租约失败: ConnectionId={ConnectionId}",
                connectionLease.ConnectionId);
        }
    }

    #endregion

    #region 辅助方法

    private sealed class RequestCancellation : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly object _gate = new();
        private Task? _cancelTask;
        private bool _cancellationCompleted;
        private bool _disposeRequested;
        private bool _disposed;

        public RequestCancellation(MessageConnectionLease connectionLease, CancellationTokenSource cts)
        {
            ConnectionLease = connectionLease;
            _cts = cts;
        }

        public MessageConnectionLease ConnectionLease { get; }

        public CancellationToken CancellationToken => _cts.Token;

        public bool IsCancellationRequested => _cts.IsCancellationRequested;

        public void Cancel()
            => GetOrCreateCancelTask().GetAwaiter().GetResult();

        private Task GetOrCreateCancelTask()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return Task.CompletedTask;
                }

                _cancelTask ??= CancelCoreAsync();
                return _cancelTask;
            }
        }

        private async Task CancelCoreAsync()
        {
            await Task.Yield();
            try
            {
                await _cts.CancelAsync().ConfigureAwait(false);
            }
            finally
            {
                var dispose = false;
                lock (_gate)
                {
                    _cancellationCompleted = true;
                    if (_disposeRequested && !_disposed)
                    {
                        _disposed = true;
                        dispose = true;
                    }
                }

                if (dispose)
                {
                    _cts.Dispose();
                }
            }
        }

        public void Dispose()
        {
            var dispose = false;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                if (_cancelTask is not null && !_cancellationCompleted)
                {
                    _disposeRequested = true;
                    return;
                }

                _disposed = true;
                dispose = true;
            }

            if (dispose)
            {
                _cts.Dispose();
            }
        }
    }

    #endregion
}

#region 数据结构

/// <summary>
/// 消息信封 - 包含完整元数据的消息包装
/// </summary>
public struct MessageEnvelope
{
    /// <summary>
    /// 消息唯一标识符
    /// </summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// 真实连接ID
    /// </summary>
    public string ConnectionId { get; set; }

    /// <summary>
    /// 完整消息头部
    /// </summary>
    public MessageHeader Header { get; set; }

    /// <summary>
    /// 消息负载数据（零拷贝）
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; set; }

    /// <summary>
    /// 消息优先级
    /// </summary>
    public MessagePriority Priority { get; set; }

    /// <summary>
    /// 入队时间戳
    /// </summary>
    public long EnqueueTime { get; set; }

    /// <summary>
    /// 消息状态
    /// </summary>
    public MessageStatus Status { get; set; }

    /// <summary>
    /// 接收时间
    /// </summary>
    public DateTime ReceivedTime { get; set; }

    /// <summary>
    /// 处理器ID
    /// </summary>
    public int ProcessorId { get; set; }

    internal MessageConnectionLease? ConnectionLease { get; set; }
}

/// <summary>
/// 消息批次 - L2处理的基本单位
/// </summary>
[Obsolete("The fixed-shard message engine does not create L2 message batches.", false)]
public struct MessageBatch
{
    public string BatchId { get; set; }
    public MessageEnvelope[] Messages { get; set; }
    public long CreateTime { get; set; }
}

/// <summary>
/// 响应批次 - L3处理的基本单位
/// </summary>
[Obsolete("The fixed-shard message engine does not create L3 response batches.", false)]
public struct ResponseBatch
{
    public string BatchId { get; set; }
    public MessageResponse[] Responses { get; set; }
    public TimeSpan ProcessingTime { get; set; }
}

/// <summary>
/// 消息响应
/// </summary>
public class MessageResponse
{
    public string MessageId { get; set; } = "";
    public string ConnectionId { get; set; } = "";
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan ProcessingTime { get; set; }
}

/// <summary>
/// 连接上下文
/// </summary>
[Obsolete("This context model is not connected to the runtime. Use PulseContext and IPulseServer connection queries.", false)]
public class ConnectionContext
{
    public string ConnectionId { get; set; } = "";
    public object? TransportChannel { get; set; }
    public DateTime ConnectedAt { get; set; }
    public DateTime LastActivity { get; set; }
    public IServiceContext? ServiceContext { get; set; }
}

/// <summary>
/// 引擎统计信息
/// </summary>
public class EngineStatistics
{
    public string EngineId { get; set; } = "";
    public TimeSpan UpTime { get; set; }

    // L1统计
    public double L1BufferUtilization { get; set; }
    public long L1MessagesEnqueued { get; set; }
    public long L1BackpressureEvents { get; set; }

    // 处理统计
    public long TotalMessagesProcessed { get; set; }
    public long TotalMessagesDropped { get; set; }
    public long TotalBatchesProcessed { get; set; }

    // 性能指标
    public double CurrentThroughput { get; set; }
    public double AverageLatencyMs { get; set; }
    public double P50LatencyMs { get; set; }
    public double P95LatencyMs { get; set; }
    public double P99LatencyMs { get; set; }
    public double MaxLatencyMs { get; set; }
    public long LatencySampleCount { get; set; }

    // 连接统计
    public int ActiveConnections { get; set; }

    // 内存统计
    public object? MemoryPoolStatistics { get; set; }
}

/// <summary>
/// 引擎性能指标
/// </summary>
public class EngineMetrics
{
    private readonly LatencyHistogram _latencyHistogram = new();
    private long _enqueueLatencyTicks;
    private long _enqueueLatencySamples;
    private double _currentL1Utilization;

    public DateTime EngineStartTime { get; set; } = DateTime.UtcNow;

    // 计数器
    public readonly Counter<long> L1MessagesEnqueued = new();
    public readonly Counter<long> MessagesProcessed = new();
    public readonly Counter<long> MessagesDropped = new();
    public readonly Counter<long> MessagesErrored = new();
    public readonly Counter<long> BatchesProcessed = new();
    public readonly Counter<long> BatchesErrored = new();
    public readonly Counter<long> BackpressureEvents = new();
    public readonly Counter<long> ForcedEnqueues = new();
    public readonly Counter<long> ResponseBatchesSent = new();
    public readonly Counter<long> ResponsesSent = new();
    public readonly Counter<long> ResponseErrors = new();
    public readonly Counter<long> EnqueueErrors = new();

    // 新增指标
    public readonly Counter<long>? RetrySuccesses = new();
    public readonly Counter<long> BackpressureBlocks = new();
    public readonly Counter<long> FallbackProcessed = new();

    // 指标
    public readonly Gauge<int> ActiveConnections = new();

    // 性能指标方法
    public double GetCurrentL1Utilization() => Volatile.Read(ref _currentL1Utilization);

    public double GetCurrentThroughput()
    {
        var elapsedSeconds = (DateTime.UtcNow - EngineStartTime).TotalSeconds;
        return elapsedSeconds <= 0 ? 0 : MessagesProcessed.Value / elapsedSeconds;
    }

    public double GetAverageLatencyMs()
        => GetLatencySnapshot().AverageMilliseconds;

    public double GetP50LatencyMs()
        => GetLatencySnapshot().GetPercentileMilliseconds(0.50);

    public double GetP95LatencyMs()
        => GetLatencySnapshot().GetPercentileMilliseconds(0.95);

    public double GetP99LatencyMs()
        => GetLatencySnapshot().GetPercentileMilliseconds(0.99);

    public double GetMaxLatencyMs()
        => TimeSpan.FromTicks(GetLatencySnapshot().MaxTicks).TotalMilliseconds;

    public long GetLatencySampleCount()
        => GetLatencySnapshot().Count;

    internal LatencyHistogramSnapshot GetLatencySnapshot()
        => _latencyHistogram.GetSnapshot();

    public void RecordEnqueueLatency(long ticks)
    {
        if (ticks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticks));
        }

        Interlocked.Add(ref _enqueueLatencyTicks, ticks);
        Interlocked.Increment(ref _enqueueLatencySamples);
    }

    public void RecordBatchProcessingTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(time));
        }

        _latencyHistogram.Record(time);
    }

    internal void SetCurrentL1Utilization(double utilization)
    {
        Volatile.Write(ref _currentL1Utilization, Math.Clamp(utilization, 0, 1));
    }
}

/// <summary>
/// 简单计数器（线程安全）
/// </summary>
public class Counter<T> where T : struct, IConvertible
{
    private long _value;

    public T Value
    {
        get
        {
            var longValue = Interlocked.Read(ref _value);
            return (T)Convert.ChangeType(longValue, typeof(T));
        }
    }

    public void Increment() => Interlocked.Increment(ref _value);
    public void Add(int count) => Interlocked.Add(ref _value, count);
    public void Add(long count) => Interlocked.Add(ref _value, count);
}

/// <summary>
/// 简单仪表（线程安全）
/// </summary>
public class Gauge<T> where T : struct, IConvertible
{
    private long _value;

    public T Value
    {
        get
        {
            var longValue = Interlocked.Read(ref _value);
            return (T)Convert.ChangeType(longValue, typeof(T));
        }
    }

    public void Set(T value)
    {
        var longValue = Convert.ToInt64(value);
        Interlocked.Exchange(ref _value, longValue);
    }
}

/// <summary>
/// 负载均衡策略
/// </summary>
[Obsolete("Server message workers use fixed round-robin shard assignment. This type has no runtime behavior.", false)]
public class LoadBalancingStrategy(LoadBalancingMode mode)
{
    private readonly LoadBalancingMode _mode = mode;
}


/// <summary>
/// 性能监控器
/// </summary>
[Obsolete("This monitor is not connected to MessageEngine. Use EngineStatistics and RuntimeQueueMetrics.", false)]
public class PerformanceMonitor
{
    private readonly EngineMetrics _metrics;
    private readonly ILogger _logger;

    public PerformanceMonitor(EngineMetrics metrics, ILogger logger)
    {
        _metrics = metrics;
        _logger = logger;
    }

    public PerformanceSnapshot TakeSnapshot()
    {
        return new PerformanceSnapshot
        {
            Timestamp = DateTime.UtcNow,
            CurrentThroughput = _metrics.GetCurrentThroughput(),
            P99LatencyMs = _metrics.GetP99LatencyMs(),
            P95LatencyMs = _metrics.GetAverageLatencyMs() // 临时
        };
    }
}

/// <summary>
/// 性能快照
/// </summary>
[Obsolete("This snapshot is not produced by MessageEngine. Use EngineStatistics.", false)]
public class PerformanceSnapshot
{
    public DateTime Timestamp { get; set; }
    public double CurrentThroughput { get; set; }
    public double P99LatencyMs { get; set; }
    public double P95LatencyMs { get; set; }
}

#endregion
