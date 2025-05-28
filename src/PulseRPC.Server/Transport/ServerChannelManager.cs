using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Authentication;
using PulseRPC.Transport;
using PulseRPC.Server.Transport;

namespace PulseRPC.Server.Transport
{
    /// <summary>
    /// 服务器通道管理器，管理所有客户端连接的传输通道
    /// </summary>
    public class ServerChannelManager : IServerChannelManager, IDisposable
    {
        private readonly ConcurrentDictionary<string, ITransportChannel> _channels;
        private readonly ILogger<ServerChannelManager> _logger;
        private readonly Timer _cleanupTimer;
        private readonly object _lockObject = new object();
        private volatile bool _disposed;

        /// <summary>
        /// 通道超时时间（毫秒）
        /// </summary>
        public int ChannelTimeoutMs { get; set; } = 300000; // 5分钟

        /// <summary>
        /// 当前连接数
        /// </summary>
        public int ConnectionCount => _channels.Count;

        /// <summary>
        /// 所有连接ID
        /// </summary>
        public IEnumerable<string> ConnectionIds => _channels.Keys.ToList();

        /// <summary>
        /// 通道连接事件
        /// </summary>
        public event System.EventHandler<ChannelEventArgs>? ChannelConnected;

        /// <summary>
        /// 通道断开事件
        /// </summary>
        public event System.EventHandler<ChannelEventArgs>? ChannelDisconnected;

        /// <summary>
        /// 通道认证事件
        /// </summary>
        public event System.EventHandler<ChannelAuthenticatedEventArgs>? ChannelAuthenticated;

        public ServerChannelManager(ILogger<ServerChannelManager> logger)
        {
            _channels = new ConcurrentDictionary<string, ITransportChannel>();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // 启动清理定时器，每60秒清理一次过期连接
            _cleanupTimer = new Timer(CleanupExpiredChannels, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// 添加新的传输通道
        /// </summary>
        /// <param name="connection">服务器连接</param>
        /// <returns>创建的传输通道</returns>
        public ITransportChannel AddChannel(IServerConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (_disposed) throw new ObjectDisposedException(nameof(ServerChannelManager));

            var channel = new ServerTransportChannel(connection);

            // 注册事件处理
            channel.StateChanged += OnChannelStateChanged;

            // 添加到管理字典
            if (_channels.TryAdd(connection.ConnectionId, channel))
            {
                _logger.LogInformation("已添加传输通道: {ConnectionId}", connection.ConnectionId);
                ChannelConnected?.Invoke(this, new ChannelEventArgs(channel));
                return channel;
            }
            else
            {
                // 如果添加失败，说明连接ID冲突，释放新创建的通道
                channel.Dispose();
                throw new InvalidOperationException($"连接ID冲突: {connection.ConnectionId}");
            }
        }

        /// <summary>
        /// 获取指定的传输通道
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        /// <returns>传输通道，如果不存在则返回null</returns>
        public ITransportChannel? GetChannel(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId)) return null;

            _channels.TryGetValue(connectionId, out var channel);
            return channel;
        }

        /// <summary>
        /// 移除指定的传输通道
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        /// <returns>是否成功移除</returns>
        public bool RemoveChannel(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId)) return false;

            if (_channels.TryRemove(connectionId, out var channel))
            {
                _logger.LogInformation("已移除传输通道: {ConnectionId}", connectionId);

                // 取消订阅事件
                channel.StateChanged -= OnChannelStateChanged;

                // 触发断开事件
                ChannelDisconnected?.Invoke(this, new ChannelEventArgs(channel));

                // 释放通道资源
                channel.Dispose();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 获取所有传输通道
        /// </summary>
        /// <returns>所有传输通道的集合</returns>
        public IEnumerable<ITransportChannel> GetAllChannels()
        {
            return _channels.Values.ToList();
        }

        /// <summary>
        /// 获取所有已认证的传输通道
        /// </summary>
        /// <returns>已认证的传输通道集合</returns>
        public IEnumerable<ITransportChannel> GetAuthenticatedChannels()
        {
            return _channels.Values.Where(c => c.IsAuthenticated).ToList();
        }

        /// <summary>
        /// 根据认证用户名获取传输通道
        /// </summary>
        /// <param name="username">用户名</param>
        /// <returns>用户的传输通道集合</returns>
        public IEnumerable<ITransportChannel> GetChannelsByUser(string username)
        {
            if (string.IsNullOrEmpty(username)) return Enumerable.Empty<ITransportChannel>();

            return _channels.Values
                .Where(c => c.IsAuthenticated &&
                           string.Equals(c.AuthenticationContext?.Name, username, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>
        /// 广播消息到所有已认证的通道
        /// </summary>
        /// <param name="data">要发送的数据</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>成功发送的通道数量</returns>
        public async Task<int> BroadcastAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            var authenticatedChannels = GetAuthenticatedChannels().ToList();
            var tasks = authenticatedChannels.Select(async channel =>
            {
                try
                {
                    return await channel.SendAsync(data, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "向通道 {ConnectionId} 发送广播消息失败", channel.ConnectionId);
                    return false;
                }
            });

            var results = await Task.WhenAll(tasks);
            return results.Count(success => success);
        }

        /// <summary>
        /// 处理通道状态变更事件
        /// </summary>
        private void OnChannelStateChanged(object? sender, TransportStateEventArgs e)
        {
            if (sender is not ITransportChannel channel) return;

            _logger.LogDebug("通道状态变更: {ConnectionId} - {OldState} -> {NewState}",
                channel.ConnectionId, e.PreviousState, e.CurrentState);

            // 如果连接断开，自动移除通道
            if (e.CurrentState == ConnectionState.Disconnected)
            {
                RemoveChannel(channel.ConnectionId);
            }
        }

        /// <summary>
        /// 清理过期的连接
        /// </summary>
        private void CleanupExpiredChannels(object? state)
        {
            if (_disposed) return;

            try
            {
                var expiredThreshold = DateTime.UtcNow.AddMilliseconds(-ChannelTimeoutMs);
                var expiredChannels = _channels.Values
                    .Where(c => c.LastActiveTime < expiredThreshold)
                    .ToList();

                foreach (var channel in expiredChannels)
                {
                    _logger.LogInformation("清理过期通道: {ConnectionId}, 最后活动时间: {LastActiveTime}",
                        channel.ConnectionId, channel.LastActiveTime);

                    RemoveChannel(channel.ConnectionId);
                }

                if (expiredChannels.Count > 0)
                {
                    _logger.LogInformation("已清理 {Count} 个过期通道", expiredChannels.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理过期通道时发生异常");
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            // 停止清理定时器
            _cleanupTimer?.Dispose();

            // 关闭所有通道
            var allChannels = _channels.Values.ToList();
            _channels.Clear();

            foreach (var channel in allChannels)
            {
                try
                {
                    channel.StateChanged -= OnChannelStateChanged;
                    channel.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "释放通道 {ConnectionId} 时发生异常", channel.ConnectionId);
                }
            }

            _logger.LogInformation("ServerChannelManager 已释放");
        }
    }

    /// <summary>
    /// 通道事件参数
    /// </summary>
    public class ChannelEventArgs : EventArgs
    {
        public ITransportChannel Channel { get; }

        public ChannelEventArgs(ITransportChannel channel)
        {
            Channel = channel ?? throw new ArgumentNullException(nameof(channel));
        }
    }

    /// <summary>
    /// 通道认证事件参数
    /// </summary>
    public class ChannelAuthenticatedEventArgs : ChannelEventArgs
    {
        public IAuthenticationContext AuthenticationContext { get; }

        public ChannelAuthenticatedEventArgs(ITransportChannel channel, IAuthenticationContext authContext)
            : base(channel)
        {
            AuthenticationContext = authContext ?? throw new ArgumentNullException(nameof(authContext));
        }
    }
}
