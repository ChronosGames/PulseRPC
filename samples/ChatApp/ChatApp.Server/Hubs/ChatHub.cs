using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using ChatApp.Shared.Hubs;
using ChatApp.Shared.Models;
using Microsoft.Extensions.Logging;
using PulseRPC.Network;
using PulseRPC.Server;

namespace ChatApp.Server.Hubs
{
    /// <summary>
    /// 聊天Hub实现
    /// </summary>
    public class ChatHub : IChatHub
    {
        private readonly ILogger<ChatHub> _logger;
        private readonly ServiceDispatcher _dispatcher;
        private readonly ConcurrentDictionary<string, UserSession> _sessions = new();

        /// <summary>
        /// 聊天会话信息
        /// </summary>
        private class UserSession
        {
            public string UserId { get; set; } = string.Empty;
            public string UserName { get; set; } = string.Empty;
            public string RoomName { get; set; } = string.Empty;
            public NetworkSession Session { get; set; } = null!;
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="dispatcher">服务分发器</param>
        public ChatHub(ILogger<ChatHub> logger, IMessageDispatcher dispatcher)
        {
            _logger = logger;
            _dispatcher = dispatcher as ServiceDispatcher ?? throw new ArgumentException("需要ServiceDispatcher类型的分发器");
        }

        /// <summary>
        /// 实现IService接口方法
        /// </summary>
        public IChatHub WithDeadline(DateTime deadline)
        {
            // 在实际实现中，这个方法在客户端代理中使用，服务端实现可以忽略
            return this;
        }

        /// <summary>
        /// 实现IService接口方法
        /// </summary>
        public IChatHub WithCancellationToken(System.Threading.CancellationToken cancellationToken)
        {
            // 在实际实现中，这个方法在客户端代理中使用，服务端实现可以忽略
            return this;
        }

        /// <summary>
        /// 实现IService接口方法
        /// </summary>
        public IChatHub WithHost(string host)
        {
            // 在实际实现中，这个方法在客户端代理中使用，服务端实现可以忽略
            return this;
        }

        /// <summary>
        /// 加入聊天室
        /// </summary>
        public Task<bool> JoinAsync(JoinRequest request)
        {
            // 获取当前会话
            var session = GetCurrentSession();

            // 创建用户会话
            var userId = session.Id;
            var userSession = new UserSession
            {
                UserId = userId,
                UserName = request.UserName,
                RoomName = request.RoomName,
                Session = session
            };

            // 保存会话信息
            _sessions[userId] = userSession;

            _logger.LogInformation("用户 {UserName} 加入了房间 {RoomName}", request.UserName, request.RoomName);

            // 通知房间内其他用户有新用户加入
            foreach (var otherSession in _sessions.Values)
            {
                if (otherSession.RoomName == request.RoomName && otherSession.UserId != userId)
                {
                    // 通过接收器通知
                    try
                    {
                        NotifyUserJoined(otherSession.Session, request.UserName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "通知用户 {UserId} 有新用户加入时出错", otherSession.UserId);
                    }
                }
            }

            return Task.FromResult(true);
        }

        /// <summary>
        /// 离开聊天室
        /// </summary>
        public Task<bool> LeaveAsync()
        {
            // 获取当前会话
            var session = GetCurrentSession();
            var userId = session.Id;

            // 获取用户会话
            if (!_sessions.TryGetValue(userId, out var userSession))
            {
                _logger.LogWarning("用户 {UserId} 尝试离开聊天室，但未找到会话信息", userId);
                return Task.FromResult(false);
            }

            // 从会话列表中移除
            _sessions.TryRemove(userId, out _);

            _logger.LogInformation("用户 {UserName} 离开了房间 {RoomName}", userSession.UserName, userSession.RoomName);

            // 通知房间内其他用户有用户离开
            foreach (var otherSession in _sessions.Values)
            {
                if (otherSession.RoomName == userSession.RoomName)
                {
                    // 通过接收器通知
                    try
                    {
                        NotifyUserLeft(otherSession.Session, userSession.UserName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "通知用户 {UserId} 有用户离开时出错", otherSession.UserId);
                    }
                }
            }

            return Task.FromResult(true);
        }

        /// <summary>
        /// 发送消息
        /// </summary>
        public Task<bool> SendMessageAsync(string message)
        {
            // 获取当前会话
            var session = GetCurrentSession();
            var userId = session.Id;

            // 获取用户会话
            if (!_sessions.TryGetValue(userId, out var userSession))
            {
                _logger.LogWarning("用户 {UserId} 尝试发送消息，但未找到会话信息", userId);
                return Task.FromResult(false);
            }

            _logger.LogInformation("用户 {UserName} 发送消息: {Message}", userSession.UserName, message);

            // 创建消息响应
            var response = new MessageResponse
            {
                UserName = userSession.UserName,
                Message = message
            };

            // 通知房间内其他用户有新消息
            foreach (var otherSession in _sessions.Values)
            {
                if (otherSession.RoomName == userSession.RoomName)
                {
                    // 通过接收器通知
                    try
                    {
                        NotifyNewMessage(otherSession.Session, response);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "通知用户 {UserId} 有新消息时出错", otherSession.UserId);
                    }
                }
            }

            return Task.FromResult(true);
        }

        /// <summary>
        /// 生成异常（测试错误处理）
        /// </summary>
        public Task<bool> GenerateException(string message)
        {
            _logger.LogWarning("生成异常: {Message}", message);
            throw new InvalidOperationException($"测试异常: {message}");
        }

        /// <summary>
        /// 获取当前会话
        /// </summary>
        private NetworkSession GetCurrentSession()
        {
            // 在实际实现中，应该从当前执行上下文中获取会话
            // 这里简化处理，使用一个静态方法模拟
            return GetCurrentNetworkSession();
        }

        /// <summary>
        /// 模拟获取当前网络会话
        /// </summary>
        private static NetworkSession GetCurrentNetworkSession()
        {
            // 在实际实现中，应该从当前执行上下文中获取会话
            // 这里简化处理，返回null
            throw new NotImplementedException("需要实现获取当前会话的逻辑");
        }

        /// <summary>
        /// 通知用户有新用户加入
        /// </summary>
        private void NotifyUserJoined(NetworkSession session, string userName)
        {
            var notification = new ReceiverNotification
            {
                ReceiverType = typeof(IChatHubReceiver).FullName!,
                MethodId = 1, // OnJoin方法ID
                Parameters = System.Text.Encoding.UTF8.GetBytes(userName)
            };

            session.SendPacketAsync(notification).Wait();
        }

        /// <summary>
        /// 通知用户有用户离开
        /// </summary>
        private void NotifyUserLeft(NetworkSession session, string userName)
        {
            var notification = new ReceiverNotification
            {
                ReceiverType = typeof(IChatHubReceiver).FullName!,
                MethodId = 2, // OnLeave方法ID
                Parameters = System.Text.Encoding.UTF8.GetBytes(userName)
            };

            session.SendPacketAsync(notification).Wait();
        }

        /// <summary>
        /// 通知用户有新消息
        /// </summary>
        private void NotifyNewMessage(NetworkSession session, MessageResponse message)
        {
            var notification = new ReceiverNotification
            {
                ReceiverType = typeof(IChatHubReceiver).FullName!,
                MethodId = 3, // OnSendMessage方法ID
                Parameters = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(message)
            };

            session.SendPacketAsync(notification).Wait();
        }
    }
}
