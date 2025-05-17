namespace PulseRPC;

/// <summary>
/// 服务标记接口
/// </summary>
public interface IServiceMarker
{

}

/// <summary>
/// 服务接口
/// </summary>
/// <typeparam name="TSelf">服务接口类型</typeparam>
public interface IStreamingHub<TSelf> : IServiceMarker
    where TSelf : IStreamingHub<TSelf>
{
    /// <summary>
    /// 设置请求超时时间
    /// </summary>
    /// <param name="deadline">截止时间</param>
    /// <returns>服务实例</returns>
    TSelf WithDeadline(DateTime deadline);

    /// <summary>
    /// 设置取消令牌
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务实例</returns>
    TSelf WithCancellationToken(CancellationToken cancellationToken);

    /// <summary>
    /// 设置服务主机
    /// </summary>
    /// <param name="host">主机地址</param>
    /// <returns>服务实例</returns>
    TSelf WithHost(string host);
}

/// <summary>
/// 特定服务的接收器接口
/// </summary>
/// <typeparam name="TReceiver">接收器接口类型</typeparam>
public interface IStreamingReceiver<TReceiver> : IStreamingReceiver
    where TReceiver : IStreamingReceiver<TReceiver>;

/// <summary>
/// 服务方法特性
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public class ServiceMethodAttribute : Attribute
{
    /// <summary>
    /// 初始化服务方法特性
    /// </summary>
    /// <param name="id">方法ID</param>
    public ServiceMethodAttribute(ushort id)
    {
        Id = id;
    }

    /// <summary>
    /// 方法ID
    /// </summary>
    public ushort Id { get; }
}

/// <summary>
/// 接收器方法特性
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public class ReceiverMethodAttribute : Attribute
{
    /// <summary>
    /// 初始化接收器方法特性
    /// </summary>
    /// <param name="id">方法ID</param>
    public ReceiverMethodAttribute(ushort id)
    {
        Id = id;
    }

    /// <summary>
    /// 方法ID
    /// </summary>
    public ushort Id { get; }
}
