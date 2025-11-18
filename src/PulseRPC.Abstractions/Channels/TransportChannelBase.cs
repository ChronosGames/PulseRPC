using System.Collections.Concurrent;
using System.Net;

namespace PulseRPC.Channels;

/// <summary>
/// 传输通道抽象基类 - 实现双向 RPC 的通用逻辑
/// </summary>
/// <remarks>
/// 此基类为客户端和服务端通道提供统一的双向RPC能力：
/// - 服务注册：通过 RegisterHub 注册本地服务，供对方调用
/// - 代理获取：通过 GetHubAsync 获取远程服务代理
/// - 请求处理：通过 HandleRemoteInvocationAsync 处理远程调用
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
