using Microsoft.Extensions.Logging;
using GameApp.Shared.Services;
using System.Collections.Concurrent;

namespace GameApp.BattleServer.Services;

/// <summary>
/// 战斗引擎实现
/// </summary>
public class BattleEngine : IBattleEngine
{
    private readonly IBattleRepository _battleRepository;
    private readonly IBattleCacheService _battleCacheService;
    private readonly IBattleEventPublisher _battleEventPublisher;
    private readonly ISkillSystem _skillSystem;
    private readonly ILogger<BattleEngine> _logger;

    // 内存中的活跃战斗
    private readonly ConcurrentDictionary<string, BattleInfo> _activeBattles = new();

    // 战斗Tick定时器
    private readonly ConcurrentDictionary<string, Timer> _battleTimers = new();

    // 战斗引擎配置
    private readonly int _tickIntervalMs = 50; // 20 TPS

    public BattleEngine(
        IBattleRepository battleRepository,
        IBattleCacheService battleCacheService,
        IBattleEventPublisher battleEventPublisher,
        ISkillSystem skillSystem,
        ILogger<BattleEngine> logger)
    {
        _battleRepository = battleRepository;
        _battleCacheService = battleCacheService;
        _battleEventPublisher = battleEventPublisher;
        _skillSystem = skillSystem;
        _logger = logger;
    }

    /// <summary>
    /// 初始化战斗引擎
    /// </summary>
    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing BattleEngine...");

        // 这里可以加载战斗配置、技能模板等
        await Task.CompletedTask;

        _logger.LogInformation("BattleEngine initialized successfully");
    }

    /// <summary>
    /// 创建战斗房间
    /// </summary>
    public async Task<string> CreateBattleRoomAsync(string battleType, BattleSettings settings)
    {
        try
        {
            var battleId = GenerateBattleId();

            var battleInfo = new BattleInfo
            {
                BattleId = battleId,
                BattleType = battleType,
                Status = "waiting",
                Players = new List<BattlePlayer>(),
                Settings = settings,
                StartTime = DateTime.UtcNow,
                WinnerTeam = -1
            };

            // 保存到内存和缓存
            _activeBattles[battleId] = battleInfo;
            await _battleCacheService.CacheBattleInfoAsync(battleInfo);

            _logger.LogInformation("Created battle room: {BattleId}, Type: {BattleType}",
                battleId, battleType);

            return battleId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating battle room");
            throw;
        }
    }

    /// <summary>
    /// 添加玩家到战斗
    /// </summary>
    public async Task<bool> AddPlayerToBattleAsync(string battleId, BattlePlayer player)
    {
        try
        {
            if (!_activeBattles.TryGetValue(battleId, out var battle))
            {
                _logger.LogWarning("Battle not found: {BattleId}", battleId);
                return false;
            }

            // 检查房间是否已满
            if (battle.Players.Count >= battle.Settings.MaxPlayers)
            {
                _logger.LogWarning("Battle room full: {BattleId}", battleId);
                return false;
            }

            // 检查玩家是否已在战斗中
            if (battle.Players.Any(p => p.PlayerId == player.PlayerId))
            {
                _logger.LogWarning("Player {PlayerId} already in battle {BattleId}",
                    player.PlayerId, battleId);
                return false;
            }

            // 分配队伍
            if (battle.BattleType == "pvp")
            {
                player.Team = battle.Players.Count % 2 + 1; // 1 或 2
            }
            else
            {
                player.Team = 1; // PvE 统一队伍
            }

            // 添加玩家
            battle.Players.Add(player);

            // 缓存玩家战斗状态
            await _battleCacheService.CachePlayerBattleStateAsync(player.PlayerId, battleId, player);

            // 检查是否可以开始战斗
            if (ShouldStartBattle(battle))
            {
                await StartBattleAsync(battleId);
            }

            // 更新缓存
            await _battleCacheService.CacheBattleInfoAsync(battle);

            _logger.LogInformation("Player {PlayerId} added to battle {BattleId}, Team: {Team}",
                player.PlayerId, battleId, player.Team);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding player {PlayerId} to battle {BattleId}",
                player.PlayerId, battleId);
            return false;
        }
    }

    /// <summary>
    /// 移除玩家
    /// </summary>
    public async Task RemovePlayerFromBattleAsync(string battleId, int playerId, string reason)
    {
        try
        {
            if (!_activeBattles.TryGetValue(battleId, out var battle))
            {
                return;
            }

            var player = battle.Players.FirstOrDefault(p => p.PlayerId == playerId);
            if (player == null)
            {
                return;
            }

            battle.Players.Remove(player);

            // 发布玩家离开事件
            var leftEvent = new PlayerDefeatedEvent
            {
                BattleId = battleId,
                PlayerId = playerId,
                PlayerName = player.PlayerName,
                KillerPlayerId = -1,
                KillerPlayerName = "System",
                DeathTime = DateTime.UtcNow
            };

            await _battleEventPublisher.PublishPlayerDefeatedAsync(battleId, leftEvent);

            // 检查战斗是否应该结束
            if (ShouldEndBattle(battle))
            {
                await EndBattleAsync(battleId, "player_left");
            }
            else
            {
                // 更新战斗状态
                var stateUpdateEvent = new BattleStateUpdateEvent
                {
                    BattleId = battleId,
                    Status = battle.Status,
                    Players = battle.Players,
                    Timestamp = DateTime.UtcNow
                };

                await _battleEventPublisher.PublishBattleStateUpdateAsync(battleId, stateUpdateEvent);
            }

            _logger.LogInformation("Player {PlayerId} removed from battle {BattleId}, reason: {Reason}",
                playerId, battleId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing player {PlayerId} from battle {BattleId}",
                playerId, battleId);
        }
    }

    /// <summary>
    /// 处理技能使用
    /// </summary>
    public async Task<SkillResult> ProcessSkillUseAsync(string battleId, UseSkillRequest request)
    {
        try
        {
            if (!_activeBattles.TryGetValue(battleId, out var battle))
            {
                return new SkillResult
                {
                    Success = false,
                    Message = "战斗不存在",
                    SkillId = request.SkillId
                };
            }

            var player = battle.Players.FirstOrDefault(p => p.PlayerId == request.PlayerId);
            if (player == null)
            {
                return new SkillResult
                {
                    Success = false,
                    Message = "玩家不在战斗中",
                    SkillId = request.SkillId
                };
            }

            var skill = player.Skills.FirstOrDefault(s => s.SkillId == request.SkillId);
            if (skill == null)
            {
                return new SkillResult
                {
                    Success = false,
                    Message = "技能不存在",
                    SkillId = request.SkillId
                };
            }

            // 验证技能使用条件
            var canUseSkill = await _skillSystem.ValidateSkillUseAsync(player, skill, request);
            if (!canUseSkill)
            {
                return new SkillResult
                {
                    Success = false,
                    Message = "技能使用条件不满足",
                    SkillId = request.SkillId,
                    CooldownRemaining = _skillSystem.GetSkillCooldown(request.PlayerId, request.SkillId)
                };
            }

            // 获取目标玩家
            var targets = GetSkillTargets(battle, request);

            // 计算技能伤害
            var damageResults = await _skillSystem.CalculateSkillDamageAsync(player, skill, targets);

            // 应用技能效果
            await _skillSystem.ApplySkillEffectsAsync(player, skill, targets);

            // 设置技能冷却
            await _skillSystem.SetSkillCooldownAsync(request.PlayerId, request.SkillId, skill.CooldownSeconds);

            // 发布技能使用事件
            var skillUsedEvent = new SkillUsedEvent
            {
                BattleId = battleId,
                PlayerId = request.PlayerId,
                PlayerName = player.PlayerName,
                Skill = skill,
                CastPosition = player.Position,
                TargetPosition = request.TargetPosition,
                TargetPlayerIds = request.TargetPlayerIds,
                CastTime = request.CastTime
            };

            await _battleEventPublisher.PublishSkillUsedAsync(battleId, skillUsedEvent);

            // 发布伤害事件
            if (damageResults.Any())
            {
                var damageEvent = new DamageDealtEvent
                {
                    BattleId = battleId,
                    SourcePlayerId = request.PlayerId,
                    SourcePlayerName = player.PlayerName,
                    DamageResults = damageResults,
                    SkillId = request.SkillId,
                    Timestamp = DateTime.UtcNow
                };

                await _battleEventPublisher.PublishDamageDealtAsync(battleId, damageEvent);
            }

            return new SkillResult
            {
                Success = true,
                Message = "技能释放成功",
                SkillId = request.SkillId,
                DamageResults = damageResults,
                CooldownRemaining = skill.CooldownSeconds
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing skill use: Player {PlayerId}, Skill {SkillId}",
                request.PlayerId, request.SkillId);

            return new SkillResult
            {
                Success = false,
                Message = "技能处理失败",
                SkillId = request.SkillId
            };
        }
    }

    /// <summary>
    /// 更新玩家位置
    /// </summary>
    public async Task UpdatePlayerPositionAsync(string battleId, int playerId, BattlePosition position)
    {
        try
        {
            if (!_activeBattles.TryGetValue(battleId, out var battle))
            {
                return;
            }

            var player = battle.Players.FirstOrDefault(p => p.PlayerId == playerId);
            if (player == null)
            {
                return;
            }

            player.Position = position;

            // 更新缓存
            await _battleCacheService.CachePlayerBattleStateAsync(playerId, battleId, player);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating player position: {PlayerId}", playerId);
        }
    }

    /// <summary>
    /// 获取战斗信息
    /// </summary>
    public async Task<BattleInfo?> GetBattleInfoAsync(string battleId)
    {
        try
        {
            if (_activeBattles.TryGetValue(battleId, out var battle))
            {
                return battle;
            }

            // 尝试从缓存获取
            return await _battleCacheService.GetCachedBattleInfoAsync(battleId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting battle info: {BattleId}", battleId);
            return null;
        }
    }

    /// <summary>
    /// 战斗引擎Tick处理
    /// </summary>
    public async Task ProcessBattleTickAsync(string battleId)
    {
        try
        {
            if (!_activeBattles.TryGetValue(battleId, out var battle))
            {
                return;
            }

            // 检查战斗是否超时
            if (battle.Status == "active" &&
                DateTime.UtcNow - battle.StartTime > TimeSpan.FromSeconds(battle.Settings.TimeLimit))
            {
                await EndBattleAsync(battleId, "timeout");
                return;
            }

            // 更新Buff状态
            foreach (var player in battle.Players)
            {
                // TODO: 更新玩家Buff状态
            }

            // 发布状态更新
            var stateUpdateEvent = new BattleStateUpdateEvent
            {
                BattleId = battleId,
                Status = battle.Status,
                Players = battle.Players,
                Timestamp = DateTime.UtcNow
            };

            await _battleEventPublisher.PublishBattleStateUpdateAsync(battleId, stateUpdateEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing battle tick: {BattleId}", battleId);
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// 生成战斗ID
    /// </summary>
    private string GenerateBattleId()
    {
        return $"battle_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}";
    }

    /// <summary>
    /// 检查是否应该开始战斗
    /// </summary>
    private bool ShouldStartBattle(BattleInfo battle)
    {
        return battle.Status == "waiting" &&
               battle.Players.Count >= GetMinPlayersForBattleType(battle.BattleType);
    }

    /// <summary>
    /// 获取战斗类型最少玩家数
    /// </summary>
    private int GetMinPlayersForBattleType(string battleType)
    {
        return battleType.ToLower() switch
        {
            "pvp" => 2,
            "pve" => 1,
            "raid" => 3,
            _ => 2
        };
    }

    /// <summary>
    /// 开始战斗
    /// </summary>
    private async Task StartBattleAsync(string battleId)
    {
        if (!_activeBattles.TryGetValue(battleId, out var battle))
        {
            return;
        }

        battle.Status = "active";
        battle.StartTime = DateTime.UtcNow;

        // 启动战斗Tick定时器
        var timer = new Timer(async _ => await ProcessBattleTickAsync(battleId),
            null, TimeSpan.Zero, TimeSpan.FromMilliseconds(_tickIntervalMs));

        _battleTimers[battleId] = timer;

        // 发布战斗开始事件
        var stateUpdateEvent = new BattleStateUpdateEvent
        {
            BattleId = battleId,
            Status = battle.Status,
            Players = battle.Players,
            Timestamp = DateTime.UtcNow
        };

        await _battleEventPublisher.PublishBattleStateUpdateAsync(battleId, stateUpdateEvent);

        _logger.LogInformation("Battle started: {BattleId}", battleId);
    }

    /// <summary>
    /// 检查是否应该结束战斗
    /// </summary>
    private bool ShouldEndBattle(BattleInfo battle)
    {
        if (battle.Status != "active")
        {
            return false;
        }

        // 检查是否有队伍全部死亡
        var teamsAlive = battle.Players
            .Where(p => p.Status.IsAlive)
            .GroupBy(p => p.Team)
            .Count();

        return teamsAlive <= 1;
    }

    /// <summary>
    /// 结束战斗
    /// </summary>
    private async Task EndBattleAsync(string battleId, string reason)
    {
        if (!_activeBattles.TryGetValue(battleId, out var battle))
        {
            return;
        }

        battle.Status = "ended";
        battle.EndTime = DateTime.UtcNow;

        // 停止战斗定时器
        if (_battleTimers.TryRemove(battleId, out var timer))
        {
            timer.Dispose();
        }

        // 确定获胜队伍
        var winnerTeam = DetermineWinnerTeam(battle);
        battle.WinnerTeam = winnerTeam;

        // 计算战斗统计
        var statistics = CalculateBattleStatistics(battle);

        // 保存战斗记录
        await _battleRepository.SaveBattleInfoAsync(battle);
        await _battleRepository.SaveBattleStatisticsAsync(battleId, statistics);

        // 发布战斗结束事件
        var battleEndedEvent = new BattleEndedEvent
        {
            BattleId = battleId,
            WinnerTeam = winnerTeam,
            WinnerPlayerIds = battle.Players
                .Where(p => p.Team == winnerTeam)
                .Select(p => p.PlayerId)
                .ToList(),
            EndReason = reason,
            Statistics = statistics,
            EndTime = DateTime.UtcNow
        };

        await _battleEventPublisher.PublishBattleEndedAsync(battleId, battleEndedEvent);

        // 清理资源
        _activeBattles.TryRemove(battleId, out _);
        await _battleCacheService.RemoveBattleCacheAsync(battleId);

        _logger.LogInformation("Battle ended: {BattleId}, Winner: Team {WinnerTeam}, Reason: {Reason}",
            battleId, winnerTeam, reason);
    }

    /// <summary>
    /// 确定获胜队伍
    /// </summary>
    private int DetermineWinnerTeam(BattleInfo battle)
    {
        var aliveTeams = battle.Players
            .Where(p => p.Status.IsAlive)
            .GroupBy(p => p.Team)
            .ToList();

        if (aliveTeams.Count == 1)
        {
            return aliveTeams.First().Key;
        }

        // 如果是平局或者没有存活玩家，返回-1
        return -1;
    }

    /// <summary>
    /// 计算战斗统计
    /// </summary>
    private BattleStatistics CalculateBattleStatistics(BattleInfo battle)
    {
        var duration = battle.EndTime.HasValue
            ? (int)(battle.EndTime.Value - battle.StartTime).TotalSeconds
            : 0;

        var playerStats = new Dictionary<int, PlayerBattleStats>();

        foreach (var player in battle.Players)
        {
            playerStats[player.PlayerId] = new PlayerBattleStats
            {
                PlayerId = player.PlayerId,
                // TODO: 从实际战斗数据中计算
                DamageDealt = 0,
                DamageTaken = 0,
                HealingDone = 0,
                Kills = 0,
                Deaths = player.Status.IsAlive ? 0 : 1,
                SkillsUsed = 0
            };
        }

        return new BattleStatistics
        {
            Duration = duration,
            PlayerStats = playerStats
        };
    }

    /// <summary>
    /// 获取技能目标
    /// </summary>
    private List<BattlePlayer> GetSkillTargets(BattleInfo battle, UseSkillRequest request)
    {
        var targets = new List<BattlePlayer>();

        // 如果指定了目标玩家ID
        if (request.TargetPlayerIds.Any())
        {
            targets.AddRange(battle.Players.Where(p => request.TargetPlayerIds.Contains(p.PlayerId)));
        }
        else
        {
            // 基于位置和技能范围选择目标
            var caster = battle.Players.FirstOrDefault(p => p.PlayerId == request.PlayerId);
            if (caster?.Skills.FirstOrDefault(s => s.SkillId == request.SkillId) is { } skill)
            {
                targets.AddRange(battle.Players
                    .Where(p => p.PlayerId != request.PlayerId)
                    .Where(p => CalculateDistance(caster.Position, p.Position) <= skill.Range)
                    .Take(skill.Effects.MaxTargets));
            }
        }

        return targets;
    }

    /// <summary>
    /// 计算距离
    /// </summary>
    private float CalculateDistance(BattlePosition pos1, BattlePosition pos2)
    {
        var dx = pos1.X - pos2.X;
        var dy = pos1.Y - pos2.Y;
        var dz = pos1.Z - pos2.Z;

        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    #endregion
}
