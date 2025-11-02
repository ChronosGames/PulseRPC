using PulseRPC;
using ChatApp;

namespace ChatApp.Client.Console;

/// <summary>
/// 触发客户端代理生成的标记类
/// </summary>
/// <remarks>
/// 使用 [PulseClientGeneration] 特性标记这个类，告诉源代码生成器
/// 需要为 ChatApp 程序集中的所有 IPulseHub 和 IPulseReceiver 接口生成客户端代理。
/// </remarks>
[PulseClientGeneration(typeof(IChatHub))]
public static class GeneratedProxies
{
    // 这是一个标记类，不包含任何实现代码
    // 源代码生成器会扫描这个类的 [PulseClientGeneration] 特性
    // 并为指定程序集中的所有服务接口生成代理类和扩展方法
}
