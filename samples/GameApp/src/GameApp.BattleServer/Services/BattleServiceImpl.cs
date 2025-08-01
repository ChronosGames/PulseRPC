using Microsoft.Extensions.Logging;
using PulseRPC;
using GameApp.Shared.Services;

namespace GameApp.BattleServer.Services;

/// <summary>
/// 战斗服务实现
/// </summary>
[Channel("KcpChannel")]
public class BattleServiceImpl : IPulseService, IBattleService, IBattleServiceImpl
{
    private readonly IBattleEngine _battleEngine;
    private readonly IBattleCacheService _battleCacheService;
    private readonly IBattleEventPublisher _battleEventPublisher;
    private readonly ILogger<BattleServiceImpl> _logger;

    public BattleServiceImpl(
        IBattleEngine battleEngine,
        IBattleCacheService battleCacheService,
        IBattleEventPublisher battleEventPublisher,
        ILogger<BattleServiceImpl> logger)
    {
        _battleEngine = battleEngine;
        _battleCacheService = battleCacheService;
        _battleEventPublisher = battleEventPublisher;
        _logger = logger;
    }

    /// <summary>
    /// 加入战斗
    /// </summary>
    public async Task<JoinBattleResponse> JoinBattleAsync(JoinBattleRequest request)
    {
        try
        {
            _logger.LogInformation("Player {PlayerId} attempting to join battle: {BattleType}",
                request.PlayerId, request.BattleType);

            // 1. 检查玩家是否已在战斗中
            var existingBattleState = await _battleCacheService.GetPlayerBattleStateAsync(request.PlayerId);
            if (existingBattleState != null)
            {
                return new JoinBattleResponse
                {
                    Success = false,
                    Message = "玩家已在战斗中"
                };
            }

            // 2. 创建或找到合适的战斗房间
            string battleId;
            if (!string.IsNullOrEmpty(request.RoomId))
            {
                battleId = request.RoomId;
            }
            else
            {
                // 创建新的战斗房间
                var settings = CreateBattleSettings(request.BattleType, request.Parameters);
                battleId = await _battleEngine.CreateBattleRoomAsync(request.BattleType, settings);
            }

            // 3. 创建战斗玩家数据
            var battlePlayer = await CreateBattlePlayerAsync(request.PlayerId);

            // 4. 添加玩家到战斗
            var joinResult = await _battleEngine.AddPlayerToBattleAsync(battleId, battlePlayer);
            if (!joinResult)
            {
                return new JoinBattleResponse
                {
                    Success = false,
                    Message = "无法加入战斗房间"
                };
            }

            // 5. 获取战斗信息
            var battleInfo = await _battleEngine.GetBattleInfoAsync(battleId);
            if (battleInfo == null)
            {
                return new JoinBattleResponse
                {
                    Success = false,
                    Message = "战斗房间不存在"
                };
            }

            _logger.LogInformation("Player {PlayerId} successfully joined battle {BattleId}",
                request.PlayerId, battleId);

            return new JoinBattleResponse
            {
                Success = true,
                Message = "成功加入战斗",
                BattleInfo = battleInfo,
                Players = battleInfo.Players
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining battle for player {PlayerId}", request.PlayerId);
            return new JoinBattleResponse
            {
                Success = false,
                Message = "加入战斗失败，请稍后重试"
            };
        }
    }

    /// <summary>
    /// 离开战斗
    /// </summary>
    public async Task LeaveBattleAsync(LeaveBattleRequest request)
    {
        try
        {
            _logger.LogInformation("Player {PlayerId} leaving battle {BattleId}, reason: {Reason}",
                request.PlayerId, request.BattleId, request.Reason);

            await _battleEngine.RemovePlayerFromBattleAsync(request.BattleId, request.PlayerId, request.Reason);

            _logger.LogInformation("Player {PlayerId} left battle {BattleId}",
                request.PlayerId, request.BattleId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving battle for player {PlayerId}", request.PlayerId);
        }
    }

    /// <summary>
    /// 使用技能
    /// </summary>
    public async Task<SkillResult> UseSkillAsync(UseSkillRequest request)
    {
        try
        {
            _logger.LogDebug("Player {PlayerId} using skill {SkillId} in battle {BattleId}",
                request.PlayerId, request.SkillId, request.BattleId);

            var result = await _battleEngine.ProcessSkillUseAsync(request.BattleId, request);

            if (result.Success)
            {
                _logger.LogDebug("Skill {SkillId} successfully used by player {PlayerId}",
                    request.SkillId, request.PlayerId);
            }
            else
            {
                _logger.LogWarning("Skill {SkillId} failed for player {PlayerId}: {Message}",
                    request.SkillId, request.PlayerId, result.Message);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error using skill {SkillId} for player {PlayerId}",
                request.SkillId, request.PlayerId);

            return new SkillResult
            {
                Success = false,
                Message = "技能释放失败",
                SkillId = request.SkillId
            };
        }
    }

    /// <summary>
    /// 移动战斗位置
    /// </summary>
    public async Task MoveBattlePositionAsync(MoveBattlePositionRequest request)
    {
        try
        {
            _logger.LogDebug("Player {PlayerId} moving to position ({X}, {Y}, {Z}) in battle {BattleId}",
                request.PlayerId, request.Position.X, request.Position.Y, request.Position.Z, request.BattleId);

            await _battleEngine.UpdatePlayerPositionAsync(request.BattleId, request.PlayerId, request.Position);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error moving player {PlayerId} in battle {BattleId}",
                request.PlayerId, request.BattleId);
        }
    }

    /// <summary>
    /// 获取战斗信息
    /// </summary>
    [Channel("TcpChannel")]
    public async Task<BattleInfo> GetBattleInfoAsync(GetBattleInfoRequest request)
    {
        try
        {
            var battleInfo = await _battleEngine.GetBattleInfoAsync(request.BattleId);

            if (battleInfo == null)
            {
                return new BattleInfo
                {
                    BattleId = request.BattleId,
                    Status = "not_found"
                };
            }

            return battleInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting battle info for {BattleId}", request.BattleId);
            return new BattleInfo
            {
                BattleId = request.BattleId,
                Status = "error"
            };
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// 创建战斗设置
    /// </summary>
    private BattleSettings CreateBattleSettings(string battleType, Dictionary<string, object> parameters)
    {
        var settings = new BattleSettings();

        switch (battleType.ToLower())
        {
            case "pvp":
                settings.MaxPlayers = 2;
                settings.TimeLimit = 300; // 5分钟
                settings.FriendlyFire = false;
                break;

            case "pve":
                settings.MaxPlayers = 4;
                settings.TimeLimit = 1800; // 30分钟
                settings.FriendlyFire = false;
                break;

            case "raid":
                settings.MaxPlayers = 10;
                settings.TimeLimit = 3600; // 60分钟
                settings.FriendlyFire = false;
                break;

            default:
                settings.MaxPlayers = 4;
                settings.TimeLimit = 600; // 10分钟
                settings.FriendlyFire = false;
                break;
        }

        // 应用自定义参数
        settings.CustomSettings = parameters;

        return settings;
    }

    /// <summary>
    /// 创建战斗玩家数据
    /// </summary>
    private async Task<BattlePlayer> CreateBattlePlayerAsync(int playerId)
    {
        // 这里应该从GameServer或数据库获取玩家数据
        // 简化实现
        return new BattlePlayer
        {
            PlayerId = playerId,
            PlayerName = $"Player{playerId}",
            Team = 1, // 默认队伍
            Position = new BattlePosition
            {
                X = 0,
                Y = 0,
                Z = 0,
                Rotation = 0,
                LastUpdate = DateTime.UtcNow
            },
            Status = new BattlePlayerStatus
            {
                Health = 1000,
                MaxHealth = 1000,
                Mana = 500,
                MaxMana = 500,
                IsAlive = true,
                IsStunned = false,
                LastDamageTime = DateTime.MinValue
            },
            Skills = await GetPlayerSkillsAsync(playerId),
            ActiveBuffs = new List<BuffEffect>()
        };
    }

    /// <summary>
    /// 获取玩家技能列表
    /// </summary>
    private async Task<List<PlayerSkill>> GetPlayerSkillsAsync(int playerId)
    {
        // 简化实现，返回默认技能
        await Task.Delay(1); // 避免编译器警告

        return new List<PlayerSkill>
        {
            new PlayerSkill
            {
                SkillId = 1001,
                Name = "基础攻击",
                Description = "基础物理攻击",
                Level = 1,
                MaxLevel = 5,
                Type = SkillType.Attack,
                ManaCost = 10,
                CooldownSeconds = 2.0f,
                Range = 5.0f,
                Effects = new SkillEffects
                {
                    BaseDamage = 100,
                    MaxTargets = 1
                }
            },
            new PlayerSkill
            {
                SkillId = 1002,
                Name = "治疗术",
                Description = "恢复生命值",
                Level = 1,
                MaxLevel = 5,
                Type = SkillType.Heal,
                ManaCost = 50,
                CooldownSeconds = 5.0f,
                Range = 10.0f,
                Effects = new SkillEffects
                {
                    Healing = 200,
                    MaxTargets = 1
                }
            }
        };
    }

    #endregion
}
