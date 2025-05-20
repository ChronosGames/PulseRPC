// GameServer/World/PlayerManager.cs
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChatApp.Shared;
using PulseRPC.Server;

namespace GameServer.World
{
    /// <summary>
    /// 玩家管理器接口
    /// </summary>
    public interface IPlayerManager
    {
        /// <summary>
        /// 获取或创建玩家
        /// </summary>
        Task<Player> GetOrCreatePlayerAsync(string username);

        /// <summary>
        /// 获取玩家
        /// </summary>
        Task<Player> GetPlayerAsync(Guid playerId);

        /// <summary>
        /// 获取所有玩家
        /// </summary>
        IEnumerable<Player> GetAllPlayers();

        /// <summary>
        /// 获取在线玩家数量
        /// </summary>
        int GetOnlinePlayerCount();

        /// <summary>
        /// 更新玩家
        /// </summary>
        void UpdatePlayers();
    }

    /// <summary>
    /// 玩家管理器实现
    /// </summary>
    public class PlayerManager : IPlayerManager
    {
        private readonly ConcurrentDictionary<Guid, Player> _players = new ConcurrentDictionary<Guid, Player>();
        private readonly ConcurrentDictionary<string, Guid> _usernameToId = new ConcurrentDictionary<string, Guid>();
        private readonly IEventPublisher _eventPublisher;
        private readonly ILogger<PlayerManager> _logger;

        // 5分钟不活动则视为离线
        private readonly TimeSpan _inactivityTimeout = TimeSpan.FromMinutes(5);

        public PlayerManager(IEventPublisher eventPublisher, ILogger<PlayerManager> logger)
        {
            _eventPublisher = eventPublisher;
            _logger = logger;
        }

        /// <summary>
        /// 获取或创建玩家
        /// </summary>
        public Task<Player> GetOrCreatePlayerAsync(string username)
        {
            // 检查玩家是否已存在
            if (_usernameToId.TryGetValue(username, out var playerId))
            {
                if (_players.TryGetValue(playerId, out var existingPlayer))
                {
                    return Task.FromResult(existingPlayer);
                }
            }

            // 创建新玩家
            var player = new Player
            {
                Id = Guid.NewGuid(),
                Username = username,
                Level = 1,
                Position = new Vector3(0, 0, 0),
                RotationY = 0,
                IsOnline = false,
                LastLoginTime = DateTime.UtcNow,
                LastActivityTime = DateTime.UtcNow,
                CreationTime = DateTime.UtcNow
            };

            // 添加到集合
            _players[player.Id] = player;
            _usernameToId[username] = player.Id;

            _logger.LogInformation("创建新玩家: {Username} (ID: {PlayerId})",
                player.Username, player.Id);

            return Task.FromResult(player);
        }

        /// <summary>
        /// 获取玩家
        /// </summary>
        public Task<Player> GetPlayerAsync(Guid playerId)
        {
            if (_players.TryGetValue(playerId, out var player))
            {
                // 更新活动时间
                player.UpdateActivity();
                return Task.FromResult(player);
            }

            return Task.FromResult<Player>(null);
        }

        /// <summary>
        /// 获取所有玩家
        /// </summary>
        public IEnumerable<Player> GetAllPlayers()
        {
            return _players.Values;
        }

        /// <summary>
        /// 获取在线玩家数量
        /// </summary>
        public int GetOnlinePlayerCount()
        {
            return _players.Values.Count(p => p.IsOnline);
        }

        /// <summary>
        /// 更新玩家
        /// </summary>
        public void UpdatePlayers()
        {
            var now = DateTime.UtcNow;
            var playersToDisconnect = new List<Player>();

            // 检查不活跃的玩家
            foreach (var player in _players.Values)
            {
                if (player.IsOnline && (now - player.LastActivityTime) > _inactivityTimeout)
                {
                    playersToDisconnect.Add(player);
                }
            }

            // 处理离线玩家
            foreach (var player in playersToDisconnect)
            {
                player.IsOnline = false;

                // 发送离线事件
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _eventPublisher.PublishEventAsync("OnPlayerLeft", new PlayerLeftEvent
                        {
                            PlayerId = player.Id,
                            Reason = "不活跃超时"
                        });

                        _logger.LogInformation("玩家 {Username} (ID: {PlayerId}) 因不活跃而离线",
                            player.Username, player.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "发送玩家离线事件异常");
                    }
                });
            }
        }
    }
}
