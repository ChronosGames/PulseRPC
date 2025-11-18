namespace PulseRPC.Channels;

/// <summary>
/// Hub 注册令牌默认实现
/// </summary>
public sealed class HubRegistrationToken : IHubRegistrationToken
{
    private readonly Action _unregisterAction;
    private bool _disposed;

    /// <summary>
    /// 创建 Hub 注册令牌
    /// </summary>
    /// <param name="hubType">Hub 接口类型</param>
    /// <param name="channelName">Channel 名称</param>
    /// <param name="unregisterAction">取消注册的回调</param>
    public HubRegistrationToken(Type hubType, string channelName, Action unregisterAction)
    {
        HubType = hubType ?? throw new ArgumentNullException(nameof(hubType));
        ChannelName = channelName ?? throw new ArgumentNullException(nameof(channelName));
        _unregisterAction = unregisterAction ?? throw new ArgumentNullException(nameof(unregisterAction));
    }

    /// <inheritdoc />
    public Type HubType { get; }

    /// <inheritdoc />
    public string ChannelName { get; }

    /// <inheritdoc />
    public bool IsUnregistered { get; private set; }

    /// <inheritdoc />
    public void Unregister()
    {
        if (_disposed || IsUnregistered)
            return;

        try
        {
            _unregisterAction();
            IsUnregistered = true;
        }
        catch
        {
            // 忽略取消注册时的错误
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Unregister();
    }
}
