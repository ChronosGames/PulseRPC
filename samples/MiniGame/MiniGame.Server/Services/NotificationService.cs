using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Samples.Shared;
using PulseRPC.Samples.Shared.Messages;
using PulseRPC.Server;

namespace PulseRPC.Samples.Server.Services;

/// <summary>
/// 通知服务，用于向客户端发送各种通知
/// </summary>
public class NotificationService
{
    private readonly ILogger<NotificationService> _logger;
    private readonly TcpServer _server;

    /// <summary>
    /// 初始化通知服务
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="server">TCP服务器</param>
    public NotificationService(ILogger<NotificationService> logger, TcpServer server)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _server = server ?? throw new ArgumentNullException(nameof(server));
    }

    /// <summary>
    /// 发送系统通知
    /// </summary>
    /// <param name="title">通知标题</param>
    /// <param name="content">通知内容</param>
    /// <param name="type">通知类型</param>
    /// <param name="expireTime">过期时间(毫秒时间戳)</param>
    /// <param name="metadata">元数据</param>
    /// <returns>发送任务</returns>
    public async Task SendSystemNotificationAsync(
        string title,
        string content,
        NotificationType type,
        long expireTime,
        Dictionary<string, string>? metadata = null)
    {
        var notification = new SystemNotification
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title,
            Content = content,
            Type = (int)type,
            CreateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
            ExpireTime = expireTime,
            Metadata = metadata ?? new Dictionary<string, string>()
        };

        await _server.BroadcastAsync(notification);
        _logger.LogInformation($"已发送系统通知: {title}, 类型: {type}");
    }

    /// <summary>
    /// 发送用户状态通知
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="username">用户名</param>
    /// <param name="status">状态</param>
    /// <param name="lastLoginTime">最后登录时间(毫秒时间戳)</param>
    /// <returns>发送任务</returns>
    public async Task SendUserStatusNotificationAsync(
        int userId,
        string username,
        int status,
        long lastLoginTime)
    {
        var notification = new UserStatusNotification
        {
            UserId = userId,
            Username = username,
            Status = status,
            LastLoginTime = lastLoginTime,
            Timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds()
        };

        await _server.BroadcastAsync(notification);
        _logger.LogInformation("已发送用户状态通知: 用户 {Username} ({UserId}), 状态: {Status}",
            username, userId, status);
    }

    /// <summary>
    /// 发送全局广播
    /// </summary>
    /// <param name="message">广播消息</param>
    /// <param name="sender">发送者</param>
    /// <param name="color">消息颜色</param>
    /// <param name="displayTime">显示时间(毫秒)</param>
    /// <returns>发送任务</returns>
    public async Task SendGlobalBroadcastAsync(
        string message,
        string sender,
        string color,
        int displayTime)
    {
        var broadcast = new GlobalBroadcast
        {
            Id = Guid.NewGuid().ToString("N"),
            Message = message,
            Sender = sender,
            Color = color,
            DisplayTime = displayTime,
            Timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds()
        };

        await _server.BroadcastAsync(broadcast);
        _logger.LogInformation("已发送全局广播: {Message}, 发送者: {Sender}", message, sender);
    }
}
