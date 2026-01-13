using Microsoft.Extensions.Logging;
using PulseRPC.Server.Transport;
using PulseRPC.Transport;

namespace PulseRPC.Server.Transport;

/// <summary>
/// 传输层集成管理器接口 - 统一管理所有传输协议
/// </summary>
public interface ITransportIntegrationManager
{
    /// <summary>
    /// 注册传输提供程序
    /// </summary>
    /// <param name="provider">传输提供程序</param>
    void RegisterProvider(ITransportProvider provider);

    /// <summary>
    /// 创建传输监听器
    /// </summary>
    /// <param name="config">传输配置</param>
    /// <param name="loggerFactory">日志工厂</param>
    /// <returns>传输监听器</returns>
    IServerListener CreateListener(TransportChannelConfiguration config, ILoggerFactory loggerFactory);

    /// <summary>
    /// 获取所有已注册的传输类型
    /// </summary>
    /// <returns>支持的传输类型列表</returns>
    IReadOnlyList<string> GetSupportedTransportTypes();

    /// <summary>
    /// 检查是否支持指定的传输类型
    /// </summary>
    /// <param name="transportType">传输类型</param>
    /// <returns>是否支持</returns>
    bool IsSupported(string transportType);
}