using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Protocol.Network;
using PulseRPC.Protocol.Serialization;
using PulseRPC.Samples.Shared;
using PulseRPC.Samples.Shared.Messages;
using PulseRPC.Server;

namespace PulseRPC.Samples.Server.Handlers
{
    /// <summary>
    /// 通知服务
    /// </summary>
    public class NotificationService
    {
        private readonly ILogger<NotificationService> _logger;
        private TcpServer _server;

        /// <summary>
        /// 创建通知服务
        /// </summary>
        /// <param name="logger">日志记录器</param>
        public NotificationService(ILogger<NotificationService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 设置TCP服务器
        /// </summary>
        /// <param name="server">TCP服务器</param>
        public void SetServer(TcpServer server)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
        }

        /// <summary>
        /// 发送系统通知
        /// </summary>
        /// <param name="title">标题</param>
        /// <param name="content">内容</param>
        /// <param name="type">类型</param>
        /// <param name="expireTime">过期时间（毫秒）</param>
        /// <param name="extraData">额外数据</param>
        /// <returns>异步任务</returns>
        public async Task SendSystemNotificationAsync(
            string title,
            string content,
            NotificationType type = NotificationType.System,
            long expireTime = 0,
            Dictionary<string, string> extraData = null)
        {
            if (_server == null)
            {
                _logger.LogError("发送系统通知失败: 服务器未设置");
                return;
            }

            try
            {
                // 创建通知消息
                var notification = new SystemNotification
                {
                    Title = title,
                    Content = content,
                    Type = type,
                    ExpireTime = expireTime > 0 ? expireTime : DateTimeOffset.Now.AddDays(1).ToUnixTimeMilliseconds(),
                    ExtraData = extraData ?? new Dictionary<string, string>()
                };

                // 广播给所有客户端
                _logger.LogInformation("发送系统通知: {Title}", title);
                await _server.BroadcastAsync(notification);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送系统通知失败");
            }
        }

        /// <summary>
        /// 发送用户状态变更通知
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="status">状态</param>
        /// <returns>异步任务</returns>
        public async Task SendUserStatusNotificationAsync(int userId, UserStatus status)
        {
            if (_server == null)
            {
                _logger.LogError("发送用户状态通知失败: 服务器未设置");
                return;
            }

            try
            {
                // 创建通知消息
                var notification = new UserStatusNotification
                {
                    UserId = userId,
                    Status = status,
                    StatusChangedTime = DateTimeOffset.Now.ToUnixTimeMilliseconds()
                };

                // 广播给所有客户端
                _logger.LogInformation("发送用户状态通知: 用户ID={UserId}, 状态={Status}", userId, status);
                await _server.BroadcastAsync(notification);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送用户状态通知失败");
            }
        }

        /// <summary>
        /// 发送全局广播
        /// </summary>
        /// <param name="content">内容</param>
        /// <param name="sender">发送者</param>
        /// <param name="color">颜色</param>
        /// <param name="duration">持续时间（毫秒）</param>
        /// <returns>异步任务</returns>
        public async Task SendGlobalBroadcastAsync(
            string content,
            string sender = "系统",
            string color = "#FF0000",
            long duration = 5000)
        {
            if (_server == null)
            {
                _logger.LogError("发送全局广播失败: 服务器未设置");
                return;
            }

            try
            {
                // 创建广播消息
                var broadcast = new GlobalBroadcast
                {
                    Content = content,
                    Sender = sender,
                    Color = color,
                    Duration = duration
                };

                // 广播给所有客户端
                _logger.LogInformation("发送全局广播: {Content}, 发送者={Sender}", content, sender);
                await _server.BroadcastAsync(broadcast);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送全局广播失败");
            }
        }
    }
}
