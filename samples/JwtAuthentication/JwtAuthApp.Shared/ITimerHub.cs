using System;
using System.Threading.Tasks;
using PulseRPC;

namespace JwtAuthApp.Shared;

/// <summary>
/// 定时器服务 - 需要认证后才能启动，演示服务端周期性向调用方连接推送事件。
/// </summary>
public interface ITimerHub : IPulseHub
{
    /// <summary>
    /// 启动定时器，服务端将按 <paramref name="interval"/> 周期向当前连接推送 <see cref="ITimerReceiver.OnTickAsync"/>。
    /// </summary>
    [Authorize]
    Task StartAsync(TimeSpan interval);

    /// <summary>
    /// 停止当前连接上的定时器。
    /// </summary>
    [Authorize]
    Task StopAsync();
}

/// <summary>
/// 定时器推送接收接口 - 客户端实现，由服务端通过 <c>IHubContext&lt;ITimerReceiver&gt;</c> 经
/// <c>IPulseRouter</c> 推送（统一 IPulseHub 全链路寻址架构，§P3 fanout-via-router）。
/// </summary>
[Channel("CLIENT")]
public interface ITimerReceiver : IPulseHub
{
    Task OnTickAsync(string message);
}
