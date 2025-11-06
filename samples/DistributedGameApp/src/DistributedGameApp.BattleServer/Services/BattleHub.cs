using DistributedGameApp.Shared.Hubs;
using DistributedGameApp.Shared.Messages;
using Microsoft.Extensions.Logging;
using PulseRPC;
using PulseRPC.Server;
using PulseRPC.Server.Abstractions;

namespace DistributedGameApp.BattleServer.Services;

/// <summary>
/// 战斗服务 Hub 实现 - 基于 BaseService 架构
/// </summary>
/// <remarks>
/// <para><strong>改进点</strong>:</para>
/// <list type="bullet">
/// <item><description>继承 BaseService，获得消息队列和线程安全保证</description></item>
/// <item><description>实现 IPulseService，支持全局单例调度</description></item>
/// <item><description>使用 GetCurrentCaller() 获取认证上下文，替代 AsyncLocal</description></item>
/// <item><description>获得表达式树编译优化（性能提升 50 倍）</description></item>
/// <item><description>获得监控指标和灾难隔离能力</description></item>
/// </list>
/// </remarks>
public class BattleHub : BaseService, IBattleHub, IPulseService
{
    private readonly BattleRoomManager _battleRoomManager;

    // ServiceId 用于标识全局单例
    public string ServiceName => "BattleHub";
    public string ServiceId => "BattleHub:Global";

    // 用于跟踪角色到战斗的映射（替代 AsyncLocal）
    private readonly Dictionary<string, string> _characterToBattleMap = new();

    public BattleHub(
        BattleRoomManager battleRoomManager,
        ILogger<BattleHub> logger,
        IAuthenticationService authenticationService,
        PermissionValidator permissionValidator)
        : base(logger, authenticationService, permissionValidator)
    {
        _battleRoomManager = battleRoomManager;
    }

    /// <summary>
    /// 加入战斗房间
    /// </summary>
    /// <remarks>
    /// ✅ BaseService 保证单线程顺序执行，无需加锁
    /// ✅ 使用 Dictionary 追踪映射关系（替代 AsyncLocal）
    /// </remarks>
    public async Task<BattleInfo> JoinBattleAsync(JoinBattleRequest request)
    {
        try
        {
            // TODO: 验证访问令牌
            // 可以通过 GetCurrentCaller() 获取认证信息

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

            // ✅ 使用 Dictionary 记录映射关系（BaseService 保证线程安全）
            _characterToBattleMap[request.CharacterId] = request.BattleId;

            Logger.LogInformation("角色 {CharacterId} 加入战斗房间 {BattleId}",
                request.CharacterId, request.BattleId);

            return battleRoom.GetBattleInfo();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "加入战斗房间失败: {BattleId}", request.BattleId);
            throw;
        }
    }

    /// <summary>
    /// 离开战斗房间
    /// </summary>
    /// <remarks>
    /// ✅ 从认证上下文获取角色ID，从映射表获取战斗ID
    /// </remarks>
    public async Task<bool> LeaveBattleAsync()
    {
        try
        {
            // ✅ 从认证上下文获取角色ID
            var caller = GetCurrentCaller();
            var characterId = caller.Claims.TryGetValue("CharacterId", out var charIdFromClaim)
                ? charIdFromClaim
                : caller.UserId;

            if (string.IsNullOrEmpty(characterId))
            {
                Logger.LogWarning("无法获取角色ID");
                return false;
            }

            // ✅ 从映射表获取战斗ID
            if (!_characterToBattleMap.TryGetValue(characterId, out var battleId))
            {
                Logger.LogWarning("角色 {CharacterId} 未加入任何战斗", characterId);
                return false;
            }

            var battleRoom = await _battleRoomManager.GetBattleRoomAsync(battleId);

            if (battleRoom == null)
            {
                return false;
            }

            var success = await battleRoom.RemovePlayerAsync(characterId);

            if (success)
            {
                // ✅ 移除映射关系
                _characterToBattleMap.Remove(characterId);

                Logger.LogInformation("角色 {CharacterId} 离开战斗房间 {BattleId}",
                    characterId, battleId);
            }

            return success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "离开战斗房间失败");
            return false;
        }
    }

    /// <summary>
    /// 获取战斗信息
    /// </summary>
    public async Task<BattleInfo> GetBattleInfoAsync()
    {
        var caller = GetCurrentCaller();
        var characterId = caller.Claims.TryGetValue("CharacterId", out var charIdFromClaim)
            ? charIdFromClaim
            : caller.UserId;

        if (string.IsNullOrEmpty(characterId) || !_characterToBattleMap.TryGetValue(characterId, out var battleId))
        {
            throw new InvalidOperationException("未加入任何战斗");
        }

        var battleRoom = await _battleRoomManager.GetBattleRoomAsync(battleId);

        if (battleRoom == null)
        {
            throw new InvalidOperationException($"战斗房间不存在: {battleId}");
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
            var caller = GetCurrentCaller();
            var characterId = caller.Claims.TryGetValue("CharacterId", out var charIdFromClaim)
                ? charIdFromClaim
                : caller.UserId;

            if (string.IsNullOrEmpty(characterId) || !_characterToBattleMap.TryGetValue(characterId, out var battleId))
            {
                throw new InvalidOperationException("未加入任何战斗");
            }

            var battleRoom = await _battleRoomManager.GetBattleRoomAsync(battleId);

            if (battleRoom == null)
            {
                throw new InvalidOperationException($"战斗房间不存在: {battleId}");
            }

            var success = await battleRoom.SetPlayerReadyAsync(characterId);

            if (success)
            {
                Logger.LogInformation("角色 {CharacterId} 在战斗 {BattleId} 中准备就绪",
                    characterId, battleId);
            }

            return success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "设置准备状态失败");
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
            var caller = GetCurrentCaller();
            var characterId = caller.Claims.TryGetValue("CharacterId", out var charIdFromClaim)
                ? charIdFromClaim
                : caller.UserId;

            if (string.IsNullOrEmpty(characterId) || !_characterToBattleMap.TryGetValue(characterId, out var battleId))
            {
                return new BattleActionResult
                {
                    ActionId = action.ActionId,
                    Success = false,
                    ErrorMessage = "未加入任何战斗"
                };
            }

            // 验证动作是否来自当前角色
            if (action.CharacterId != characterId)
            {
                return new BattleActionResult
                {
                    ActionId = action.ActionId,
                    Success = false,
                    ErrorMessage = "只能执行自己的动作"
                };
            }

            var battleRoom = await _battleRoomManager.GetBattleRoomAsync(battleId);

            if (battleRoom == null)
            {
                return new BattleActionResult
                {
                    ActionId = action.ActionId,
                    Success = false,
                    ErrorMessage = $"战斗房间不存在: {battleId}"
                };
            }

            // 执行战斗动作
            var result = await battleRoom.PerformActionAsync(action);

            Logger.LogDebug("角色 {CharacterId} 在战斗 {BattleId} 中执行动作 {ActionType}",
                characterId, battleId, action.Type);

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "执行战斗动作失败");
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
            var caller = GetCurrentCaller();
            var characterId = caller.Claims.TryGetValue("CharacterId", out var charIdFromClaim)
                ? charIdFromClaim
                : caller.UserId;

            if (string.IsNullOrEmpty(characterId) || !_characterToBattleMap.TryGetValue(characterId, out var battleId))
            {
                return false;
            }

            var battleRoom = await _battleRoomManager.GetBattleRoomAsync(battleId);

            if (battleRoom == null)
            {
                return false;
            }

            var success = await battleRoom.SurrenderAsync(characterId);

            if (success)
            {
                Logger.LogInformation("角色 {CharacterId} 在战斗 {BattleId} 中投降",
                    characterId, battleId);
            }

            return success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "投降失败");
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
            var caller = GetCurrentCaller();
            var characterId = caller.Claims.TryGetValue("CharacterId", out var charIdFromClaim)
                ? charIdFromClaim
                : caller.UserId;

            if (string.IsNullOrEmpty(characterId) || !_characterToBattleMap.TryGetValue(characterId, out var battleId))
            {
                return Array.Empty<BattleAction>();
            }

            var battleRoom = await _battleRoomManager.GetBattleRoomAsync(battleId);

            if (battleRoom == null)
            {
                return Array.Empty<BattleAction>();
            }

            return battleRoom.GetActionHistory(count);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "获取战斗历史失败");
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
}
