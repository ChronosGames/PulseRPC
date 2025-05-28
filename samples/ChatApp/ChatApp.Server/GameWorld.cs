// GameServer/World/GameWorld.cs
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameServer.World
{
    /// <summary>
    /// 游戏世界接口
    /// </summary>
    public interface IGameWorld
    {
        /// <summary>
        /// 获取游戏世界名称
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 获取游戏世界ID
        /// </summary>
        Guid Id { get; }

        /// <summary>
        /// 获取创建时间
        /// </summary>
        DateTime CreationTime { get; }

        /// <summary>
        /// 获取在线玩家数量
        /// </summary>
        int OnlinePlayerCount { get; }

        /// <summary>
        /// 更新游戏世界
        /// </summary>
        Task UpdateAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 游戏世界实现
    /// </summary>
    public class GameWorld : IGameWorld
    {
        private readonly IPlayerManager _playerManager;
        private readonly ILogger<GameWorld> _logger;
        private Timer _updateTimer;

        public string Name { get; }
        public Guid Id { get; }
        public DateTime CreationTime { get; }

        public int OnlinePlayerCount => _playerManager.GetOnlinePlayerCount();

        public GameWorld(IPlayerManager playerManager, ILogger<GameWorld> logger)
        {
            _playerManager = playerManager;
            _logger = logger;

            Name = "PulseRPC测试世界";
            Id = Guid.NewGuid();
            CreationTime = DateTime.UtcNow;

            // 启动世界更新定时器 (1秒一次)
            _updateTimer = new Timer(UpdateWorld, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

            _logger.LogInformation("游戏世界 '{Name}' 已创建", Name);
        }

        /// <summary>
        /// 更新游戏世界
        /// </summary>
        private void UpdateWorld(object? state)
        {
            try
            {
                // 更新在线玩家
                _playerManager.UpdatePlayers();

                // 这里可以添加其他世界逻辑，如NPC移动、战斗系统等
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新游戏世界时发生异常");
            }
        }

        /// <summary>
        /// 异步更新游戏世界
        /// </summary>
        public Task UpdateAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                UpdateWorld(null);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新游戏世界时发生异常");
                return Task.FromException(ex);
            }
        }
    }
}
