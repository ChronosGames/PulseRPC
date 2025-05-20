// GameServer/Services/PlayerService.cs
using Microsoft.Extensions.Logging;
using GameServer.World;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChatApp.Shared;
using Microsoft.Extensions.Hosting;
using PulseRPC.Server;

namespace GameServer.Services
{
    /// <summary>
    /// 玩家服务实现
    /// </summary>
    public class PlayerService : IPlayerService
    {
        private readonly IGameWorld _gameWorld;
        private readonly IPlayerManager _playerManager;
        private readonly IEventPublisher _eventPublisher;
        private readonly PlayerMovementBatcher _movementBatcher;
        private readonly ILogger<PlayerService> _logger;

        public PlayerService(
            IGameWorld gameWorld,
            IPlayerManager playerManager,
            IEventPublisher eventPublisher,
            PlayerMovementBatcher movementBatcher,
            ILogger<PlayerService> logger)
        {
            _gameWorld = gameWorld;
            _playerManager = playerManager;
            _eventPublisher = eventPublisher;
            _movementBatcher = movementBatcher;
            _logger = logger;
        }

        /// <summary>
        /// 处理玩家登录
        /// </summary>
        public async ValueTask<LoginResponse> LoginAsync(LoginRequest request)
        {
            _logger.LogInformation("玩家登录请求: {Username}", request.Username);

            try
            {
                // 验证密码 (简化处理，实际应使用加密)
                if (request.Password != "password")
                {
                    _logger.LogWarning("玩家 {Username} 密码错误", request.Username);

                    return new LoginResponse
                    {
                        Success = false,
                        ErrorMessage = "用户名或密码错误"
                    };
                }

                // 创建或获取玩家
                var player = await _playerManager.GetOrCreatePlayerAsync(request.Username);

                // 更新玩家状态为在线
                player.IsOnline = true;
                player.LastLoginTime = DateTime.UtcNow;

                // 创建响应
                var response = new LoginResponse
                {
                    Success = true,
                    Token = GenerateToken(player),
                    Player = new PlayerInfo
                    {
                        Id = player.Id,
                        Username = player.Username
                    }
                };

                // 通知其他玩家
                await NotifyPlayerJoinedAsync(player);

                _logger.LogInformation("玩家 {Username} (ID: {PlayerId}) 登录成功",
                    player.Username, player.Id);

                // 存储当前玩家上下文
                RequestContext.Current = new RequestContext
                {
                    PlayerId = player.Id,
                    Username = player.Username
                };

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "玩家 {Username} 登录过程中发生异常", request.Username);

                return new LoginResponse
                {
                    Success = false,
                    ErrorMessage = "服务器内部错误"
                };
            }
        }

        /// <summary>
        /// 处理玩家移动
        /// </summary>
        public async ValueTask MoveAsync(MoveRequest request)
        {
            // 获取当前玩家上下文
            var context = RequestContext.Current;
            if (context == null || context.PlayerId == Guid.Empty)
            {
                _logger.LogWarning("未授权的移动请求");
                throw new UnauthorizedAccessException("未登录");
            }

            var playerId = context.PlayerId;
            var player = await _playerManager.GetPlayerAsync(playerId);

            if (player != null)
            {
                // 更新玩家位置
                player.Position = new Vector3
                {
                    X = request.X,
                    Y = request.Y,
                    Z = request.Z
                };

                // 添加到批处理队列
                _movementBatcher.AddMovementUpdate(new PlayerMovedEvent
                {
                    PlayerId = player.Id,
                    X = player.Position.X,
                    Y = player.Position.Y,
                    Z = player.Position.Z,
                    RotationY = player.RotationY,
                    IsRunning = false // 可以根据速度判断
                });

                _logger.LogDebug("玩家 {Username} (ID: {PlayerId}) 移动到 ({X}, {Y}, {Z})",
                    player.Username, player.Id, request.X, request.Y, request.Z);
            }
            else
            {
                _logger.LogWarning("找不到玩家 {PlayerId}", playerId);
                throw new KeyNotFoundException($"找不到玩家 {playerId}");
            }
        }

        /// <summary>
        /// 通知玩家加入
        /// </summary>
        private async Task NotifyPlayerJoinedAsync(Player player)
        {
            var joinEvent = new PlayerJoinedEvent
            {
                PlayerId = player.Id,
                PlayerName = player.Username,
                X = player.Position.X,
                Y = player.Position.Y,
                Z = player.Position.Z
            };

            await _eventPublisher.PublishEventAsync("OnPlayerJoined", joinEvent);

            _logger.LogInformation("已广播玩家 {Username} 加入事件", player.Username);
        }

        /// <summary>
        /// 生成令牌
        /// </summary>
        private string GenerateToken(Player player)
        {
            // 实际应用中应使用JWT或其他认证机制
            return Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>
    /// 玩家移动批处理器
    /// </summary>
    public class PlayerMovementBatcher : BackgroundService
    {
        private readonly IEventPublisher _eventPublisher;
        private readonly ILogger<PlayerMovementBatcher> _logger;
        private readonly List<PlayerMovedEvent> _pendingUpdates = new List<PlayerMovedEvent>();
        private readonly object _syncLock = new object();

        public PlayerMovementBatcher(
            IEventPublisher eventPublisher,
            ILogger<PlayerMovementBatcher> logger)
        {
            _eventPublisher = eventPublisher;
            _logger = logger;
        }

        /// <summary>
        /// 添加移动更新
        /// </summary>
        public void AddMovementUpdate(PlayerMovedEvent update)
        {
            lock (_syncLock)
            {
                // 检查是否已存在该玩家的更新
                int index = _pendingUpdates.FindIndex(u => u.PlayerId == update.PlayerId);

                if (index >= 0)
                {
                    // 更新已存在记录
                    _pendingUpdates[index] = update;
                }
                else
                {
                    // 添加新记录
                    _pendingUpdates.Add(update);
                }
            }
        }

        /// <summary>
        /// 批处理任务
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("玩家移动批处理器已启动");

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(100, stoppingToken); // 100ms更新频率

                PlayerMovedEvent[] updates;
                lock (_syncLock)
                {
                    if (_pendingUpdates.Count == 0)
                        continue;

                    updates = _pendingUpdates.ToArray();
                    _pendingUpdates.Clear();
                }

                if (updates.Length == 1)
                {
                    // 单个更新，直接发送
                    await _eventPublisher.PublishEventAsync("OnPlayerMoved", updates[0]);
                }
                else
                {
                    // 批量更新
                    await _eventPublisher.PublishEventAsync("OnPlayersMovedBatch", new PlayersBatchMovedEvent
                    {
                        Updates = updates
                    });

                    _logger.LogDebug("已发送批量移动更新: {Count}个玩家", updates.Length);
                }
            }

            _logger.LogInformation("玩家移动批处理器已停止");
        }
    }

    /// <summary>
    /// 请求上下文
    /// </summary>
    public class RequestContext
    {
        private static readonly AsyncLocal<RequestContext> _current = new AsyncLocal<RequestContext>();

        public Guid PlayerId { get; set; }
        public string Username { get; set; }

        public static RequestContext Current
        {
            get => _current.Value;
            set => _current.Value = value;
        }
    }
}
