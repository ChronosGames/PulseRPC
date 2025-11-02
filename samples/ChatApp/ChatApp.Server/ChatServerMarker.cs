using PulseRPC;
using ChatApp;

/// <summary>
/// 标记类 - 触发 PulseRPC 源代码生成器扫描 ChatApp.Shared 程序集
/// </summary>
/// <remarks>
/// PulseServerGeneration 特性告诉源代码生成器扫描 IChatHub 所在的程序集，
/// 查找所有实现 IPulseHub 的接口，并为它们生成：
/// <list type="bullet">
/// <item><description>服务代理类（ChatHubProxy）</description></item>
/// <item><description>服务路由表（ServiceRoutingTable）</description></item>
/// <item><description>响应序列化器（ResponseSerializers）</description></item>
/// <item><description>事件订阅管理器（EventSubscriptionManager）</description></item>
/// </list>
/// </remarks>
[PulseServerGeneration(typeof(IChatHub))]
public partial class ChatServerMarker
{
}
