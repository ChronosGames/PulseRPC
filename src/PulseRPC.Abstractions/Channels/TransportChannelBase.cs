using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;

namespace PulseRPC.Channels;

/// <summary>
/// 传输通道抽象基类 - 实现双向 RPC 的通用逻辑
/// </summary>
/// <remarks>
/// 此基类为客户端和服务端通道提供统一的双向RPC能力：
/// - 服务注册：通过 RegisterHub 注册本地服务，供对方调用
/// - 代理获取：通过 GetHubAsync 获取远程服务代理
/// - 请求处理：通过 HandleRemoteInvocationAsync 处理远程调用
/// - 同步发送：通过 Send 方法入队，后台线程负责发送
///
/// 子类职责：
/// - 实现连接管理（ConnectionId, IsConnected等）
/// - 实现消息发送（SendAsync）
/// - 实现消息接收和分发（调用 HandleRemoteInvocationAsync）
/// </remarks>
public abstract class TransportChannelBase : ITransportChannel
{
    private readonly ConcurrentDictionary<string, IServiceHandler> _serviceHandlers = new();
    private readonly ConcurrentDictionary<string, IHubRegistrationToken> _hubRegistrations = new();
    private bool _disposed;

    // ==================== 发送队列机制 ====================
    // 用于支持同步 Send 方法：调用线程序列化并入队，后台线程发送

    /// <summary>
    /// 发送队列 - 存储待发送的字节流
    /// </summary>
    private readonly Channel<byte[]> _sendQueue;

    /// <summary>
    /// 发送任务取消令牌源
    /// </summary>
    private readonly CancellationTokenSource _sendCts;

    /// <summary>
    /// 后台发送任务
    /// </summary>
    private Task? _sendTask;

    /// <summary>
    /// 发送队列是否已启动
    /// </summary>
    private volatile bool _sendQueueStarted;

    /// <summary>
    /// 默认发送队列容量
    /// </summary>
    protected virtual int SendQueueCapacity => 4096;

    /// <summary>
    /// 构造函数 - 初始化发送队列
    /// </summary>
    protected TransportChannelBase()
    {
        _sendQueue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(SendQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _sendCts = new CancellationTokenSource();
    }

    // === 抽象成员（由子类实现）===

    /// <inheritdoc />
    public abstract string ConnectionId { get; }

    /// <inheritdoc />
    public abstract bool IsConnected { get; }

    /// <inheritdoc />
    public abstract EndPoint? RemoteEndPoint { get; }

    /// <inheritdoc />
    public abstract EndPoint? LocalEndPoint { get; }

    /// <inheritdoc />
    public abstract DateTime ConnectedAt { get; }

    /// <inheritdoc />
    public abstract DateTime LastActivityAt { get; }

    /// <inheritdoc />
    public abstract Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    // ==================== 同步发送接口（基于队列）====================

    /// <summary>
    /// 同步发送数据（非阻塞入队）
    /// </summary>
    /// <remarks>
    /// 调用线程将数据入队后立即返回，由后台线程负责实际发送。
    /// 用于 void 返回类型的 Receiver 方法。
    /// </remarks>
    /// <param name="data">要发送的数据</param>
    /// <returns>入队成功返回 true，队列已满返回 false</returns>
    public bool Send(ReadOnlyMemory<byte> data)
    {
        if (_disposed) return false;

        // 确保发送队列已启动
        EnsureSendQueueStarted();

        // 复制数据到新数组（因为 ReadOnlyMemory 可能来自池化缓冲区）
        var buffer = data.ToArray();

        // 尝试入队（非阻塞）
        return _sendQueue.Writer.TryWrite(buffer);
    }

    /// <summary>
    /// 同步发送数据（非阻塞入队）- byte[] 重载
    /// </summary>
    /// <param name="data">要发送的数据</param>
    /// <returns>入队成功返回 true，队列已满返回 false</returns>
    public bool Send(byte[] data)
    {
        if (_disposed) return false;

        // 确保发送队列已启动
        EnsureSendQueueStarted();

        // 尝试入队（非阻塞）
        return _sendQueue.Writer.TryWrite(data);
    }

    /// <summary>
    /// 确保发送队列后台任务已启动
    /// </summary>
    private void EnsureSendQueueStarted()
    {
        if (_sendQueueStarted) return;

        lock (_serviceHandlers) // 复用已有的锁对象
        {
            if (_sendQueueStarted) return;

            _sendTask = Task.Run(SendQueueProcessorAsync);
            _sendQueueStarted = true;
        }
    }

    /// <summary>
    /// 后台发送队列处理器
    /// </summary>
    private async Task SendQueueProcessorAsync()
    {
        try
        {
            await foreach (var data in _sendQueue.Reader.ReadAllAsync(_sendCts.Token))
            {
                try
                {
                    if (!IsConnected) continue;

                    await SendAsync(data, _sendCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // 发送失败时忽略（连接可能已断开）
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
    }

    /// <summary>
    /// 获取发送队列中待发送的消息数量
    /// </summary>
    public int PendingSendCount => _sendQueue.Reader.Count;

    // === 双向 RPC 实现（所有子类共享）===

    /// <inheritdoc />
    // public virtual Task<THub> GetHubAsync<THub>() where THub : class, IPulseHub
    // {
    //     // 这个方法由源代码生成器生成的工厂方法实现
    //     // 生成器会生成类似以下代码：
    //     // public static Task<THub> CreateProxy<THub>(ITransportChannel channel) { ... }
    //
    //     // 目前返回一个未实现的异常，等待源代码生成器实现
    //     throw new NotImplementedException(
    //         $"GetHubAsync<{typeof(THub).Name}> requires source generator implementation. " +
    //         "Please ensure PulseRPC.Client.SourceGenerator or PulseRPC.Server.SourceGenerator is properly configured.");
    // }

    /// <inheritdoc />
    // public virtual IHubRegistrationToken RegisterHub<THub>(THub implementation)
    //     where THub : class, IPulseHub
    // {
    //     if (implementation == null)
    //         throw new ArgumentNullException(nameof(implementation));
    //
    //     var hubType = typeof(THub);
    //     var hubName = hubType.Name;
    //
    //     // 检查是否已注册
    //     if (_hubRegistrations.ContainsKey(hubName))
    //     {
    //         throw new InvalidOperationException($"Hub '{hubName}' is already registered on this channel");
    //     }
    //
    //     // 这个方法由源代码生成器生成的注册助手实现
    //     // 生成器会生成服务处理器并调用 RegisterServiceHandler
    //
    //     // 目前返回一个未实现的异常，等待源代码生成器实现
    //     throw new NotImplementedException(
    //         $"RegisterHub<{hubType.Name}> requires source generator implementation. " +
    //         "Please ensure PulseRPC.Client.SourceGenerator or PulseRPC.Server.SourceGenerator is properly configured.");
    // }

    /// <inheritdoc />
    public virtual IDisposable RegisterServiceHandler(string serviceName, IServiceHandler handler)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        // 注册服务处理器
        _serviceHandlers[serviceName] = handler;

        // 创建注册令牌
        var token = new HubRegistrationToken(
            typeof(object), // 实际类型由调用方提供
            serviceName,
            () =>
            {
                _serviceHandlers.TryRemove(serviceName, out _);
                _hubRegistrations.TryRemove(serviceName, out _);
            });

        _hubRegistrations[serviceName] = token;

        return token;
    }

    /// <summary>
    /// 处理远程方法调用（由子类在接收到请求消息时调用）
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="methodName">方法名称</param>
    /// <param name="parameters">序列化的参数</param>
    /// <param name="context">请求上下文</param>
    /// <returns>执行结果</returns>
    /// <exception cref="ServiceNotFoundException">服务未找到</exception>
    protected virtual async Task<object?> HandleRemoteInvocationAsync(
        string serviceName,
        string methodName,
        ReadOnlyMemory<byte> parameters,
        IRequestContext context)
    {
        if (_serviceHandlers.TryGetValue(serviceName, out var handler))
        {
            return await handler.HandleRequestAsync(methodName, parameters, context);
        }

        throw new ServiceNotFoundException(serviceName);
    }

    /// <summary>
    /// 获取已注册的服务数量
    /// </summary>
    protected int RegisteredServiceCount => _serviceHandlers.Count;

    /// <summary>
    /// 检查服务是否已注册
    /// </summary>
    protected bool IsServiceRegistered(string serviceName) => _serviceHandlers.ContainsKey(serviceName);

    // === 生命周期管理 ===

    /// <summary>
    /// 释放资源
    /// </summary>
    /// <param name="disposing">是否正在释放托管资源</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            // 停止发送队列
            try
            {
                _sendCts.Cancel();
                _sendQueue.Writer.Complete();
                _sendTask?.Wait(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // 忽略清理错误
            }
            finally
            {
                _sendCts.Dispose();
            }

            // 清理所有注册的服务
            foreach (var registration in _hubRegistrations.Values)
            {
                try
                {
                    registration.Dispose();
                }
                catch
                {
                    // 忽略清理错误
                }
            }

            _hubRegistrations.Clear();
            _serviceHandlers.Clear();
        }

        _disposed = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 服务未找到异常
/// </summary>
public class ServiceNotFoundException : Exception
{
    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; }

    /// <summary>
    /// 创建服务未找到异常
    /// </summary>
    public ServiceNotFoundException(string serviceName)
        : base($"Service '{serviceName}' not found on this channel")
    {
        ServiceName = serviceName;
    }

    /// <summary>
    /// 创建服务未找到异常（带内部异常）
    /// </summary>
    public ServiceNotFoundException(string serviceName, Exception innerException)
        : base($"Service '{serviceName}' not found on this channel", innerException)
    {
        ServiceName = serviceName;
    }
}
