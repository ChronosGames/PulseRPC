using DistributedGameApp.Shared.Messages;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace DistributedGameApp.BattleServer.Services;

/// <summary>
/// 战斗房间管理器
/// 负责创建、管理和销毁战斗房间
/// </summary>
public class BattleRoomManager
{
    private readonly ConcurrentDictionary<string, BattleRoom> _battleRooms = new();
    private readonly ILogger<BattleRoomManager> _logger;

    public BattleRoomManager(ILogger<BattleRoomManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 获取或创建战斗房间
    /// </summary>
    public Task<BattleRoom?> GetOrCreateBattleRoomAsync(string battleId)
    {
        var battleRoom = _battleRooms.GetOrAdd(battleId, id =>
        {
            _logger.LogInformation("创建新的战斗房间: {BattleId}", id);
            return new BattleRoom(id, _logger);
        });

        return Task.FromResult<BattleRoom?>(battleRoom);
    }

    /// <summary>
    /// 获取战斗房间
    /// </summary>
    public Task<BattleRoom?> GetBattleRoomAsync(string battleId)
    {
        _battleRooms.TryGetValue(battleId, out var battleRoom);
        return Task.FromResult(battleRoom);
    }

    /// <summary>
    /// 移除战斗房间
    /// </summary>
    public Task<bool> RemoveBattleRoomAsync(string battleId)
    {
        var removed = _battleRooms.TryRemove(battleId, out _);

        if (removed)
        {
            _logger.LogInformation("战斗房间已移除: {BattleId}", battleId);
        }

        return Task.FromResult(removed);
    }

    /// <summary>
    /// 获取所有战斗房间数量
    /// </summary>
    public int GetBattleRoomCount()
    {
        return _battleRooms.Count;
    }
}

/// <summary>
/// 战斗房间
/// 管理单个战斗的状态和逻辑
/// </summary>
public class BattleRoom
{
    private readonly string _battleId;
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private readonly List<BattlePlayer> _team1 = new();
    private readonly List<BattlePlayer> _team2 = new();
    private readonly HashSet<string> _readyPlayers = new();
    private readonly List<BattleAction> _actionHistory = new();

    private BattleStatus _status = BattleStatus.Waiting;
    private DateTime _startTime;
    private DateTime? _endTime;
    private int _currentRound = 0;
    private const int MaxRounds = 100;
    private const int MaxPlayersPerTeam = 5;

    public BattleRoom(string battleId, ILogger logger)
    {
        _battleId = battleId;
        _logger = logger;
        _startTime = DateTime.UtcNow;
    }

    /// <summary>
    /// 添加玩家到战斗房间
    /// </summary>
    public Task<bool> AddPlayerAsync(string characterId)
    {
        lock (_lock)
        {
            // 检查是否已在战斗中
            if (_team1.Any(p => p.CharacterId == characterId) ||
                _team2.Any(p => p.CharacterId == characterId))
            {
                return Task.FromResult(false);
            }

            // 分配到人数较少的队伍
            var targetTeam = _team1.Count <= _team2.Count ? _team1 : _team2;

            if (targetTeam.Count >= MaxPlayersPerTeam)
            {
                return Task.FromResult(false);
            }

            // 创建战斗玩家（简化实现，使用默认属性）
            var battlePlayer = new BattlePlayer
            {
                CharacterId = characterId,
                CharacterName = $"Player_{characterId.Substring(0, 8)}",
                Class = CharacterClass.Warrior,
                Level = 1,
                CurrentHp = 100,
                MaxHp = 100,
                CurrentMp = 50,
                MaxMp = 50,
                Attack = 10,
                Defense = 5,
                Speed = 10,
                IsAlive = true,
                Position = new BattlePosition { X = 0, Y = targetTeam.Count }
            };

            targetTeam.Add(battlePlayer);

            // 如果双方都有玩家，切换到准备状态
            if (_team1.Count > 0 && _team2.Count > 0 && _status == BattleStatus.Waiting)
            {
                _status = BattleStatus.Preparing;
            }

            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// 移除玩家
    /// </summary>
    public Task<bool> RemovePlayerAsync(string characterId)
    {
        lock (_lock)
        {
            var removed = _team1.RemoveAll(p => p.CharacterId == characterId) > 0 ||
                         _team2.RemoveAll(p => p.CharacterId == characterId) > 0;

            if (removed)
            {
                _readyPlayers.Remove(characterId);
            }

            return Task.FromResult(removed);
        }
    }

    /// <summary>
    /// 设置玩家准备状态
    /// </summary>
    public Task<bool> SetPlayerReadyAsync(string characterId)
    {
        lock (_lock)
        {
            if (_status != BattleStatus.Preparing)
            {
                return Task.FromResult(false);
            }

            _readyPlayers.Add(characterId);

            // 检查是否所有玩家都准备好
            var totalPlayers = _team1.Count + _team2.Count;
            if (_readyPlayers.Count == totalPlayers && totalPlayers > 0)
            {
                _status = BattleStatus.InProgress;
                _startTime = DateTime.UtcNow;
                _currentRound = 1;

                _logger.LogInformation("战斗 {BattleId} 开始", _battleId);
            }

            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// 执行战斗动作
    /// </summary>
    public Task<BattleActionResult> PerformActionAsync(BattleAction action)
    {
        lock (_lock)
        {
            if (_status != BattleStatus.InProgress)
            {
                return Task.FromResult(new BattleActionResult
                {
                    ActionId = action.ActionId,
                    Success = false,
                    ErrorMessage = "战斗未进行中"
                });
            }

            // 查找执行动作的玩家
            var allPlayers = _team1.Concat(_team2).ToList();
            var attacker = allPlayers.FirstOrDefault(p => p.CharacterId == action.CharacterId);

            if (attacker == null)
            {
                return Task.FromResult(new BattleActionResult
                {
                    ActionId = action.ActionId,
                    Success = false,
                    ErrorMessage = "找不到角色"
                });
            }

            if (!attacker.IsAlive)
            {
                return Task.FromResult(new BattleActionResult
                {
                    ActionId = action.ActionId,
                    Success = false,
                    ErrorMessage = "角色已死亡"
                });
            }

            var result = new BattleActionResult
            {
                ActionId = action.ActionId,
                Success = true
            };

            // 根据动作类型执行不同逻辑
            switch (action.Type)
            {
                case ActionType.Attack:
                    result = PerformAttack(attacker, action, allPlayers);
                    break;

                case ActionType.Skill:
                    result = PerformSkill(attacker, action, allPlayers);
                    break;

                case ActionType.Move:
                    result = PerformMove(attacker, action);
                    break;

                case ActionType.Defend:
                    result = PerformDefend(attacker, action);
                    break;

                default:
                    result.Success = false;
                    result.ErrorMessage = "未知的动作类型";
                    break;
            }

            // 记录动作历史
            if (result.Success)
            {
                action.Timestamp = DateTime.UtcNow;
                _actionHistory.Add(action);

                // 限制历史记录数量
                if (_actionHistory.Count > 100)
                {
                    _actionHistory.RemoveAt(0);
                }
            }

            // 检查战斗是否结束
            CheckBattleEnd();

            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// 投降
    /// </summary>
    public Task<bool> SurrenderAsync(string characterId)
    {
        lock (_lock)
        {
            var player = _team1.Concat(_team2).FirstOrDefault(p => p.CharacterId == characterId);

            if (player == null)
            {
                return Task.FromResult(false);
            }

            // 设置玩家为死亡状态
            player.IsAlive = false;

            _logger.LogInformation("角色 {CharacterId} 在战斗 {BattleId} 中投降",
                characterId, _battleId);

            // 检查战斗是否结束
            CheckBattleEnd();

            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// 获取战斗信息
    /// </summary>
    public BattleInfo GetBattleInfo()
    {
        lock (_lock)
        {
            return new BattleInfo
            {
                BattleId = _battleId,
                Status = _status,
                Mode = MatchMode.OneVsOne, // 简化实现
                Team1 = new List<BattlePlayer>(_team1),
                Team2 = new List<BattlePlayer>(_team2),
                CurrentRound = _currentRound,
                MaxRounds = MaxRounds,
                StartTime = _startTime,
                EndTime = _endTime,
                Team1Score = _team1.Sum(p => p.Kills),
                Team2Score = _team2.Sum(p => p.Kills)
            };
        }
    }

    /// <summary>
    /// 获取动作历史
    /// </summary>
    public BattleAction[] GetActionHistory(int count)
    {
        lock (_lock)
        {
            return _actionHistory.TakeLast(count).ToArray();
        }
    }

    #region 私有方法

    private BattleActionResult PerformAttack(BattlePlayer attacker, BattleAction action, List<BattlePlayer> allPlayers)
    {
        var result = new BattleActionResult
        {
            ActionId = action.ActionId,
            Success = true
        };

        foreach (var targetId in action.TargetIds)
        {
            var target = allPlayers.FirstOrDefault(p => p.CharacterId == targetId);

            if (target == null || !target.IsAlive)
            {
                continue;
            }

            // 简单的伤害计算
            var damage = Math.Max(1, attacker.Attack - target.Defense / 2);
            var isCritical = Random.Shared.Next(100) < 20; // 20% 暴击率

            if (isCritical)
            {
                damage = (int)(damage * 1.5);
            }

            target.CurrentHp = Math.Max(0, target.CurrentHp - damage);

            result.DamageRecords.Add(new DamageRecord
            {
                AttackerId = attacker.CharacterId,
                TargetId = targetId,
                Damage = damage,
                IsCritical = isCritical,
                Type = DamageType.Physical
            });

            var statusChange = new StatusChange
            {
                TargetId = targetId,
                HpChange = -damage
            };

            if (target.CurrentHp <= 0)
            {
                target.IsAlive = false;
                target.Deaths++;
                attacker.Kills++;
                statusChange.IsDead = true;
            }

            result.StatusChanges.Add(statusChange);
        }

        return result;
    }

    private BattleActionResult PerformSkill(BattlePlayer attacker, BattleAction action, List<BattlePlayer> allPlayers)
    {
        // 简化实现：技能当作强化攻击
        var result = PerformAttack(attacker, action, allPlayers);

        // 消耗MP
        attacker.CurrentMp = Math.Max(0, attacker.CurrentMp - 10);

        return result;
    }

    private BattleActionResult PerformMove(BattlePlayer attacker, BattleAction action)
    {
        if (action.TargetPosition != null)
        {
            attacker.Position = action.TargetPosition;
        }

        return new BattleActionResult
        {
            ActionId = action.ActionId,
            Success = true
        };
    }

    private BattleActionResult PerformDefend(BattlePlayer attacker, BattleAction action)
    {
        // 简化实现：防御增加临时护盾效果
        attacker.StatusEffects.Add(new StatusEffect
        {
            Type = StatusEffectType.DefenseBuff,
            RemainingRounds = 1,
            Intensity = 5
        });

        return new BattleActionResult
        {
            ActionId = action.ActionId,
            Success = true
        };
    }

    private void CheckBattleEnd()
    {
        var team1Alive = _team1.Any(p => p.IsAlive);
        var team2Alive = _team2.Any(p => p.IsAlive);

        if (!team1Alive || !team2Alive)
        {
            _status = BattleStatus.Finished;
            _endTime = DateTime.UtcNow;

            var winner = team1Alive ? 1 : 2;
            _logger.LogInformation("战斗 {BattleId} 结束，获胜队伍: {Winner}",
                _battleId, winner);
        }
        else if (_currentRound >= MaxRounds)
        {
            _status = BattleStatus.Finished;
            _endTime = DateTime.UtcNow;
            _logger.LogInformation("战斗 {BattleId} 达到最大回合数，结束", _battleId);
        }
    }

    #endregion
}
