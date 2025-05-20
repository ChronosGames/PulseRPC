namespace PulseRPC;

/// <summary>
/// 服务代理生成器接口
/// </summary>
public interface IServiceProxyGenerator
{
    T CreateProxy<T>() where T : class, INetworkService;
}
