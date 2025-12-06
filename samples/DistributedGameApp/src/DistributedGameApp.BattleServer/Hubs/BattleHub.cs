using DistributedGameApp.BattleServer.Services;
using DistributedGameApp.Shared.Hubs;
using DistributedGameApp.Shared.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PulseRPC.Server;

namespace DistributedGameApp.BattleServer.Hubs;

/// <summary>
/// 战斗服务 Hub 实现 - 无状态版本
/// </summary>
/// <remarks>
/// <para><strong>设计原则</strong>:</para>
/// <list type="bullet">
/// <item><description>✅ IPulseHub 保持无状态 - 只作为请求的入口点</description></item>
/// <item><description>✅ 连接管理委托给 BattleConnectionContext</description></item>
/// <item><description>✅ 战斗逻辑委托给 BattleRoomManager</description></item>
/// <item><description>✅ 使用 RequestContext.Current 获取请求上下文</description></item>
/// </list>
/// </remarks>
public class BattleHub : IBattleHub
{
    private readonly BattleRoomManager _battleRoomManager;
    private readonly BattleConnectionContext _connectionContext;
    private readonly IAuthenticationService _authenticationService;
    private readonly ILogger<BattleHub> _logger;
    private readonly IConfiguration _configuration;

    public BattleHub(
        BattleRoomManager battleRoomManager,
        BattleConnectionContext connectionContext,
        IAuthenticationService authenticationService,
        IConfiguration configuration,
        ILogger<BattleHub> logger)
    {
        _battleRoomManager = battleRoomManager;
        _connectionContext = connectionContext;
        _authenticationService = authenticationService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// 获取当前连接的战斗状态
    /// </summary>
    /// <remarks>
    /// 每个 RPC 请求都是独立的，使用 RequestContext 和 BattleConnectionContext 获取状态
    /// </remarks>
    private BattleConnectionState GetCurrentBattleState()
    {
        var connection = RequestContext.Current;
        if (connection == null)
        {
            throw new InvalidOperationException("无法获取请求上下文");
        }

        var connectionId = connection.Id;

        // 从 BattleConnectionContext 获取该连接对应的战斗状态
        var state = _connectionContext.GetState(connectionId);
        if (state == null)
        {
            throw new InvalidOperationException("未加入任何战斗，请先调用 JoinBattleAsync");
        }

        return state;
    }

    /// <summary>
    /// 加入战斗房间
    /// </summary>
    public async Task<BattleInfo> JoinBattleAsync(JoinBattleRequest request)
    {
        try
        {
            // ✅ 验证访问令牌
            if (string.IsNullOrEmpty(request.AccessToken))
            {
                throw new UnauthorizedAccessException("访问令牌不能为空");
            }

            var authContext = await _authenticationService.AuthenticateUserAsync(request.AccessToken);
            if (authContext == null || authContext.IsExpired)
            {
                _logger.LogWarning("无效或过期的访问令牌: CharacterId={CharacterId}", request.CharacterId);
                throw new UnauthorizedAccessException("访问令牌无效或已过期");
            }

            // ✅ JWT Token 验证成功，Token 中的 UserId 表示用户账号
            // CharacterId 是该用户的角色ID，由 GameServer 验证过所有权
            // BattleServer 信任来自 BackendServer 的匹配通知，不再重复验证角色所有权
            _logger.LogInformation("访问令牌验证成功: UserId={UserId}, CharacterId={CharacterId}, BattleId={BattleId}",
                authContext.UserId, request.CharacterId, request.BattleId);

            var battleRoom = await _battleRoomManager.GetOrCreateBattleRoomAsync(request.BattleId);

            if (battleRoom == null)
            {
                throw new InvalidOperationException($"战斗房间不存在: {request.BattleId}");
            }

            // 加入战斗房间
            var success = await battleRoom.AddPlayerAsync(request.CharacterId);

            if (!success)
            {
                throw new InvalidOperationException("加入战斗失败，房间已满或角色已在战斗中");
            }

            // ✅ 获取当前连接ID
            var connection = RequestContext.Current;
            if (connection == null)
            {
                throw new InvalidOperationException("无法获取请求上下文");
            }
            var connectionId = connection.Id;

            // ✅ 使用 BattleConnectionContext 存储连接状态
            _connectionContext.JoinBattle(connectionId, request.BattleId, request.CharacterId);

            _logger.LogInformation("角色 {CharacterId} 加入战斗房间 {BattleId}, ConnectionId: {ConnectionId}",
                request.CharacterId, request.BattleId, connectionId);

            return battleRoom.GetBattleInfo();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加入战斗房间失败: BattleId={BattleId}, CharacterId={CharacterId}",
                request.BattleId, request.CharacterId);
            throw;
        }
    }

    /// <summary>
    /// 离开战斗房间
    /// </summary>
    public async Task<bool> LeaveBattleAsync()
    {
        try
        {
            // ✅ 获取当前战斗状态
            var state = GetCurrentBattleState();

            var battleRoom = await _battleRoomManager.GetBattleRoomAsync(state.BattleId);

            if (battleRoom == null)
            {
                return false;
            }

            var success = await battleRoom.RemovePlayerAsync(state.CharacterId);

            if (success)
            {
                // ✅ 清除连接状态
                var connection = RequestContext.Current;
                if (connection != null)
                {
                    _connectionContext.LeaveBattle(connection.Id);
                }

                _logger.LogInformation("角色 {CharacterId} 离开战斗房间 {BattleId}",
                    state.CharacterId, state.BattleId);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "离开战斗房间失败");
            return false;
        }
    }

    /// <summary>
    /// 获取战斗信息
    /// </summary>
    public async Task<BattleInfo> GetBattleInfoAsync()
    {
        // ✅ 获取当前战斗状态
        var state = GetCurrentBattleState();

        var battleRoom = await _battleRoomManager.GetBattleRoomAsync(state.BattleId);

        if (battleRoom == null)
        {
            throw new InvalidOperationException($"战斗房间不存在: {state.BattleId}");
        }

        return battleRoom.GetBattleInfo();
    }

    /// <summary>
    /// 准备就绪
    /// </summary>
    public async Task<bool> ReadyAsync()
    {
        try
        {
            // ✅ 获取当前战斗状态
            var state = GetCurrentBattleState();

            var battleRoom = await _battleRoomManager.GetBattleRoomAsync(state.BattleId);

            if (battleRoom == null)
            {
                throw new InvalidOperationException($"战斗房间不存在: {state.BattleId}");
            }

            var success = await battleRoom.SetPlayerReadyAsync(state.CharacterId);

            if (success)
            {
                _logger.LogInformation("角色 {CharacterId} 在战斗 {BattleId} 中准备就绪",
                    state.CharacterId, state.BattleId);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "设置准备状态失败");
            return false;
        }
    }

    /// <summary>
    /// 执行战斗动作
    /// </summary>
    public async Task<BattleActionResult> PerformActionAsync(BattleAction action)
    {
        try
        {
            // ✅ 从 ConnectionContext 获取状态
            var state = GetCurrentBattleState();

            // ✅ 验证动作是否来自当前会话的角色
            if (action.CharacterId != state.CharacterId)
            {
                _logger.LogWarning("安全警告：角色 {CharacterId} 尝试执行其他角色 {ActionCharacterId} 的动作",
                    state.CharacterId, action.CharacterId);

                return new BattleActionResult
                {
                    ActionId = action.ActionId,
                    Success = false,
                    ErrorMessage = "只能执行自己的动作"
                };
            }

            var battleRoom = await _battleRoomManager.GetBattleRoomAsync(state.BattleId);

            if (battleRoom == null)
            {
                return new BattleActionResult
                {
                    ActionId = action.ActionId,
                    Success = false,
                    ErrorMessage = $"战斗房间不存在: {state.BattleId}"
                };
            }

            // 执行战斗动作
            var result = await battleRoom.PerformActionAsync(action);

            _logger.LogDebug("角色 {CharacterId} 在战斗 {BattleId} 中执行动作 {ActionType}",
                state.CharacterId, state.BattleId, action.Type);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行战斗动作失败");
            return new BattleActionResult
            {
                ActionId = action.ActionId,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 投降
    /// </summary>
    public async Task<bool> SurrenderAsync()
    {
        try
        {
            // ✅ 从 ConnectionContext 获取状态
            var state = GetCurrentBattleState();

            var battleRoom = await _battleRoomManager.GetBattleRoomAsync(state.BattleId);

            if (battleRoom == null)
            {
                return false;
            }

            var success = await battleRoom.SurrenderAsync(state.CharacterId);

            if (success)
            {
                _logger.LogInformation("角色 {CharacterId} 在战斗 {BattleId} 中投降",
                    state.CharacterId, state.BattleId);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "投降失败");
            return false;
        }
    }

    /// <summary>
    /// 获取战斗历史
    /// </summary>
    public async Task<BattleAction[]> GetActionHistoryAsync(int count)
    {
        try
        {
            // ✅ 从 ConnectionContext 获取状态
            var state = GetCurrentBattleState();

            var battleRoom = await _battleRoomManager.GetBattleRoomAsync(state.BattleId);

            if (battleRoom == null)
            {
                return Array.Empty<BattleAction>();
            }

            return battleRoom.GetActionHistory(count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取战斗历史失败");
            return Array.Empty<BattleAction>();
        }
    }

    /// <summary>
    /// 战斗心跳
    /// </summary>
    public Task<long> BattleHeartbeatAsync()
    {
        return Task.FromResult(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    /// <summary>
    /// 创建战斗房间（内部调用，由 MatchmakingService 使用）
    /// </summary>
    public async Task<CreateBattleRoomResponse> CreateBattleRoomAsync(CreateBattleRoomRequest request)
    {
        try
        {
            _logger.LogInformation("创建战斗房间 - MatchId: {MatchId}, MatchType: {MatchType}, Players: {PlayerCount}",
                request.MatchId, request.MatchType, request.PlayerIds.Count);

            // 创建战斗房间
            var battleRoom = await _battleRoomManager.GetOrCreateBattleRoomAsync(request.MatchId);

            if (battleRoom == null)
            {
                return new CreateBattleRoomResponse
                {
                    Success = false,
                    Message = "创建战斗房间失败"
                };
            }

            // 为每个玩家生成访问令牌
            // TODO: 在生产环境中应该为每个玩家生成独立的访问令牌
            var accessToken = Guid.NewGuid().ToString();

            // 从配置中获取服务器地址和端口
            var serverHost = _configuration.GetValue<string>("ServiceRegistration:Host", "localhost");
            var serverPort = _configuration.GetValue<int>("ServiceRegistration:TcpPort", 9080);

            _logger.LogInformation("战斗房间创建成功 - RoomId: {RoomId}, Server: {Host}:{Port}", request.MatchId, serverHost, serverPort);

            return new CreateBattleRoomResponse
            {
                Success = true,
                Message = "战斗房间创建成功",
                RoomId = request.MatchId,
                ServerHost = serverHost,
                ServerPort = serverPort,
                AccessToken = accessToken
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建战斗房间失败 - MatchId: {MatchId}", request.MatchId);
            return new CreateBattleRoomResponse
            {
                Success = false,
                Message = $"创建战斗房间失败: {ex.Message}"
            };
        }
    }
}
