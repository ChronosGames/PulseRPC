using PulseRPC.Transport;

namespace PulseRPC.Server;

/// <summary>
/// 请求上下文，用于在异步调用链中传递当前请求的连接信息
/// </summary>
public static class RequestContext
{
    private static readonly AsyncLocal<IServerConnection?> _currentConnection = new();

    /// <summary>
    /// 获取当前请求的连接
    /// </summary>
    public static IServerConnection? Current => _currentConnection.Value;

    /// <summary>
    /// 设置当前请求的连接
    /// </summary>
    /// <param name="connection">服务器连接</param>
    public static void SetCurrent(IServerConnection? connection)
    {
        _currentConnection.Value = connection;
    }

    /// <summary>
    /// 清除当前请求的连接
    /// </summary>
    public static void Clear()
    {
        _currentConnection.Value = null;
    }
}
