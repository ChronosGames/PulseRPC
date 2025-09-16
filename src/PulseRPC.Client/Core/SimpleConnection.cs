namespace PulseRPC.Client.Core;

/// <summary>
/// 简单连接包装类 - 将 IConnectionContext 包装为 IConnection
/// </summary>
public sealed class SimpleConnection : IConnection
{
    private readonly IConnectionContext _context;
    private volatile bool _disposed;

    /// <summary>
    /// 连接ID
    /// </summary>
    public string Id => _context.Id;

    /// <summary>
    /// 连接描述符
    /// </summary>
    public ConnectionDescriptor Descriptor => _context.Descriptor;

    /// <summary>
    /// 连接状态
    /// </summary>
    public ExtendedConnectionState State => _context.State;

    /// <summary>
    /// 构造函数
    /// </summary>
    public SimpleConnection(IConnectionContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// 获取服务代理
    /// </summary>
    public async Task<T> GetServiceAsync<T>() where T : class, IPulseHub
    {
        ThrowIfDisposed();
        return await _context.GetServiceAsync<T>();
    }

    /// <summary>
    /// 检查是否已释放
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SimpleConnection));
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // 注意：这里不释放 _context，因为它由 ConnectionManager 管理
        // 只是标记当前包装器为已释放状态
    }

    public override string ToString()
    {
        return $"SimpleConnection[{Id}]: {State}";
    }
}