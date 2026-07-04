using JwtAuthApp.Shared;

namespace JwtAuthApp.Client;

/// <summary>
/// <see cref="ITimerReceiver"/> 客户端实现 - 接收服务端通过 <c>IHubContext&lt;ITimerReceiver&gt;</c> 推送的定时消息。
/// </summary>
public class TimerReceiver : ITimerReceiver
{
    public Task OnTickAsync(string message)
    {
        Console.WriteLine($"[ITimerReceiver.OnTickAsync] {message}");
        return Task.CompletedTask;
    }
}
