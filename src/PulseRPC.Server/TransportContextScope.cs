using PulseRPC.Transport;

namespace PulseRPC.Server;

/// <summary>
/// 传输层上下文作用域，用于在异步调用链中传递当前请求的传输连接
/// </summary>
/// <remarks>
/// <para>
/// 注意：此类仅用于管理传输层连接的上下文传播。
/// 对于更丰富的请求上下文信息（用户、权限等），请使用 <see cref="Contexts.UnifiedRequestContext"/>。
/// </para>
/// </remarks>
public static class TransportContextScope
{
    private static readonly AsyncLocal<IServerTransport?> _currentConnection = new();

    /// <summary>
    /// 获取当前请求的传输连接
    /// </summary>
    public static IServerTransport? Current => _currentConnection.Value;

    /// <summary>
    /// 设置当前请求的传输连接
    /// </summary>
    /// <param name="connection">服务器传输连接</param>
    public static void SetCurrent(IServerTransport? connection)
    {
        _currentConnection.Value = connection;
    }

    /// <summary>
    /// 清除当前请求的传输连接
    /// </summary>
    public static void Clear()
    {
        _currentConnection.Value = null;
    }
}

/// <summary>
/// 向后兼容别名 - 已弃用，请使用 <see cref="TransportContextScope"/>
/// </summary>
[Obsolete("Use TransportContextScope instead. This alias will be removed in a future version.")]
public static class RequestContext
{
    /// <inheritdoc cref="TransportContextScope.Current"/>
    public static IServerTransport? Current => TransportContextScope.Current;

    /// <inheritdoc cref="TransportContextScope.SetCurrent"/>
    public static void SetCurrent(IServerTransport? connection) => TransportContextScope.SetCurrent(connection);

    /// <inheritdoc cref="TransportContextScope.Clear"/>
    public static void Clear() => TransportContextScope.Clear();
}
