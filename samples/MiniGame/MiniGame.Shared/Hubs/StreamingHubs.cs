using System;
using System.Threading.Tasks;
using PulseRPC;
using PulseRPC.Samples.Shared.Messages;

namespace MiniGame.Shared
{
    /// <summary>
    /// 身份验证流Hub接口
    /// </summary>
    public interface AuthStreamingHub : IAuthStreamingHub
    {
        // 已经在IAuthStreamingHub中定义了方法
    }

    /// <summary>
    /// 游戏流Hub接口
    /// </summary>
    public interface GameStreamingHub : IUserStreamingHub, INotificationReceiver
    {
        // 额外的游戏特定方法可以在这里添加
        Task<GameStatusResponse> GetGameStatusAsync();
    }

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

    /// <summary>
    /// 游戏状态响应
    /// </summary>
    [MemoryPack.MemoryPackable]
    public partial class GameStatusResponse
    {
        /// <summary>
        /// 游戏状态
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// 在线玩家数量
        /// </summary>
        public int OnlinePlayers { get; set; }

        /// <summary>
        /// 服务器时间
        /// </summary>
        public DateTime ServerTime { get; set; }
    }
}
