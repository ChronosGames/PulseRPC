using System;
using System.Threading.Tasks;
using PulseRPC;
using PulseRPC.Samples.Shared.Messages;

namespace MiniGame.Shared;

/// <summary>
/// 通知接收器接口
/// </summary>
public interface INotificationReceiver
{
    /// <summary>
    /// 订阅通知
    /// </summary>
    Task SubscribeNotificationsAsync(string[] channels);

    /// <summary>
    /// 取消订阅通知
    /// </summary>
    Task UnsubscribeNotificationsAsync(string[] channels);
}
