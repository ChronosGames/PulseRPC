using JwtAuthApp.Shared;
using PulseRPC;

namespace JwtAuthApp.Client;

/// <summary>
/// 触发客户端代理生成的标记类（源生成器扫描 <see cref="PulseClientGenerationAttribute"/> 后
/// 为每个列出的 <c>IPulseHub</c> 接口生成 Stub / Dispatcher 及 <c>IClientChannel</c> 扩展方法）。
/// </summary>
[PulseClientGeneration(typeof(IAccountHub))]
[PulseClientGeneration(typeof(IGreeterHub))]
[PulseClientGeneration(typeof(ITimerHub))]
[PulseClientGeneration(typeof(ITimerReceiver))]
public static class GeneratedProxies
{
    // 标记类，无需实现代码。
}
