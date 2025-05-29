using Microsoft.Extensions.Logging;
using GameServer.World;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChatApp.Shared;
using Microsoft.Extensions.Hosting;
using PulseRPC.Server;
using PulseRPC.Server.Authentication;
using System.Security.Claims;
using PulseRPC.Transport;
using System.Linq;
using PulseRPC;
using PulseRPC.Server.Events;
using PulseRPC.Server.Transport;
using PulseRPC.Serialization;
using PulseRPC.Messaging;

namespace GameServer.Services
{
    /// <summary>
    /// 玩家服务实现
    /// </summary>
    public class PlayerHub : IPlayerHub
    {
        private readonly IGameWorld _gameWorld;
        private readonly IPlayerManager _playerManager;
        private readonly IEventPublisher _eventPublisher;
        private readonly PlayerMovementBatcher _movementBatcher;
        private readonly IServerChannelManager _channelManager;
        private readonly IAuthenticationProvider _authProvider;
        private readonly ILogger<PlayerHub> _logger;
        private readonly ISerializerProvider _serializerProvider;

        public PlayerHub(
            IGameWorld gameWorld,
            IPlayerManager playerManager,
            IEventPublisher eventPublisher,
            PlayerMovementBatcher movementBatcher,
            IServerChannelManager channelManager,
            IAuthenticationProvider authProvider,
            ILogger<PlayerHub> logger,
            ISerializerProvider serializerProvider)
        {
            _gameWorld = gameWorld;
            _playerManager = playerManager;
            _eventPublisher = eventPublisher;
            _movementBatcher = movementBatcher;
            _channelManager = channelManager;
            _authProvider = authProvider;
            _logger = logger;
            _serializerProvider = serializerProvider;
        }

        /// <summary>
        /// 处理玩家登录
        /// </summary>
        public async ValueTask<LoginResponse> LoginAsync(LoginRequest request)
        {
            _logger.LogInformation("玩家登录请求: {Username}", request.Username);

            try
            {
                // 使用认证提供程序验证凭证
                var credentials = $"{request.Username}:{request.Password}";
                var authResult = await _authProvider.AuthenticateAsync(credentials);

                if (!authResult.IsAuthenticated || authResult.User == null)
                {
                    _logger.LogWarning("玩家 {Username} 认证失败: {Error}", request.Username, authResult.ErrorMessage);
                    return new LoginResponse
                    {
                        Success = false,
                        ErrorMessage = authResult.ErrorMessage ?? "认证失败"
                    };
                }

                // 从Claims中获取用户信息
                var userIdClaim = authResult.User.FindFirst(ClaimTypes.NameIdentifier);
                var usernameClaim = authResult.User.FindFirst(ClaimTypes.Name);

                if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var playerId) || usernameClaim == null)
                {
                    _logger.LogError("认证成功但无法获取有效的用户信息");
                    return new LoginResponse
                    {
                        Success = false,
                        ErrorMessage = "用户信息无效"
                    };
                }

                // 获取玩家对象
                var player = await _playerManager.GetPlayerAsync(playerId);
                if (player == null)
                {
                    _logger.LogError("找不到玩家: {PlayerId}", playerId);
                    return new LoginResponse
                    {
                        Success = false,
                        ErrorMessage = "玩家不存在"
                    };
                }

                // 更新玩家状态为在线
                player.IsOnline = true;
                player.LastLoginTime = DateTime.UtcNow;

                // 生成令牌
                var token = GenerateToken(player);

                // 创建响应
                var response = new LoginResponse
                {
                    Success = true,
                    Token = token,
                    Player = new PlayerInfo
                    {
                        Id = player.Id,
                        Username = player.Username
                    }
                };

                // 获取当前连接并设置认证信息
                var connection = RequestContext.Current;
                if (connection != null)
                {
                    // 通过 ChannelManager 获取传输通道
                    var channel = _channelManager.GetChannel(connection.ConnectionId);
                    if (channel != null)
                    {
                        // 创建认证上下文
                        var authContext = new PulseRPC.Server.Authentication.AuthenticationContext(connection.ConnectionId);
                        authContext.SetClientAuthentication(player.Id.ToString(), player.Username, token, authResult.User);

                        // 设置通道的认证信息
                        channel.SetAuthentication(authContext);

                        _logger.LogInformation("已为通道设置认证信息: UserId={UserId}, Username={Username}, ConnectionId={ConnectionId}",
                            player.Id, player.Username, connection.ConnectionId);

                        // 认证设置完成后，通知其他玩家
                        try
                        {
                            await NotifyPlayerJoinedAsync(player);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "发布玩家加入事件失败，但不影响登录: {Username}", player.Username);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("找不到连接 {ConnectionId} 的传输通道", connection.ConnectionId);
                    }
                }
                else
                {
                    _logger.LogWarning("无法获取当前连接，无法设置认证信息");
                }

                _logger.LogInformation("玩家 {Username} (ID: {PlayerId}) 登录成功",
                    player.Username, player.Id);

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
        [PulseRPC.Authorize]
        public async ValueTask MoveAsync(MoveRequest request)
        {
            _logger.LogInformation("开始处理玩家移动请求");

            // 通过请求上下文获取当前连接（已由AuthenticationMiddleware验证）
            var connection = RequestContext.Current;
            _logger.LogInformation("RequestContext.Current: {IsNull}", connection == null ? "null" : "not null");

            if (connection == null)
            {
                _logger.LogError("RequestContext.Current 返回 null");
                throw new InvalidOperationException("无法获取当前请求连接");
            }

            // 通过 ChannelManager 获取传输通道
            var channel = _channelManager.GetChannel(connection.ConnectionId);
            if (channel == null)
            {
                _logger.LogError("找不到连接 {ConnectionId} 的传输通道", connection.ConnectionId);
                throw new InvalidOperationException("无法获取传输通道");
            }

            _logger.LogInformation("连接信息: ConnectionId={ConnectionId}, Channel={ChannelType}",
                connection.ConnectionId, channel.GetType().Name);

            var authContext = channel.AuthenticationContext;
            if (authContext == null || !authContext.IsAuthenticated)
            {
                _logger.LogError("通道未认证");
                throw new UnauthorizedAccessException("用户未认证");
            }

            _logger.LogInformation("认证信息: Type={AuthType}, Identity={Identity}",
                authContext.Type, authContext.Identity);

            // 从认证上下文中获取用户ID
            if (string.IsNullOrEmpty(authContext.Identity) || !Guid.TryParse(authContext.Identity, out var playerId))
            {
                _logger.LogError("认证上下文中未包含有效的用户ID: {Identity}", authContext.Identity);
                throw new InvalidOperationException("用户信息无效");
            }

            _logger.LogInformation("从认证上下文获取到玩家ID: {PlayerId}", playerId);

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
        /// 获取当前连接
        /// </summary>
        /// <returns>当前连接，如果找不到则返回null</returns>
        private IServerTransport? GetCurrentConnection()
        {
            // 使用RequestContext获取当前请求的连接
            return RequestContext.Current;
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

            // 获取当前连接ID，避免向正在登录的连接发送事件（防止死锁）
            var currentConnection = RequestContext.Current;
            var currentConnectionId = currentConnection?.ConnectionId;

            // 获取所有已认证的通道，但排除当前连接
            var channels = _channelManager.GetAuthenticatedChannels()
                .Where(c => c.ConnectionId != currentConnectionId)
                .ToList();

            if (!channels.Any())
            {
                _logger.LogDebug("没有其他已认证的连接，跳过玩家加入事件广播: {Username}", player.Username);
                return;
            }

            // 序列化事件数据
            var serializer = _serializerProvider.Create(MethodType.Unary, null);
            var writer = new System.Buffers.ArrayBufferWriter<byte>();
            serializer.Serialize(writer, in joinEvent);
            var eventDataBytes = writer.WrittenMemory.ToArray();

            // 并行发送给其他已认证的连接
            var tasks = channels.Select(async channel =>
            {
                try
                {
                    await channel.SendAsync(eventDataBytes, CancellationToken.None);
                    _logger.LogTrace("玩家加入事件已发送到连接: {ConnectionId}", channel.ConnectionId);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "向连接 {ConnectionId} 发送玩家加入事件时失败", channel.ConnectionId);
                    return false;
                }
            });

            var results = await Task.WhenAll(tasks);
            var successCount = results.Count(r => r);

            _logger.LogInformation("已广播玩家 {Username} 加入事件到 {SuccessCount}/{TotalCount} 个连接",
                player.Username, successCount, channels.Count);
        }

        /// <summary>
        /// 生成令牌
        /// </summary>
        private string GenerateToken(Player player)
        {
            // 实际应用中应使用JWT或其他认证机制
            return Guid.NewGuid().ToString("N");
        }

        /// <summary>
        /// 测试Ping方法（允许匿名访问）
        /// </summary>
        [PulseRPC.AllowAnonymous]
        public async ValueTask<string> PingAsync(PingRequest request)
        {
            _logger.LogInformation("收到Ping请求: {Message}", request.Message);
            await Task.Delay(10); // 模拟一些处理时间
            return $"Pong: {request.Message} at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
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
}
