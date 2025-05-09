using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ChatApp.Server;

// 装备强化服务
public class EquipmentEnhanceService {
    private readonly string _currentServerId;
    private readonly IPlayerRoutingService _routingService;
    private readonly ILocalResourceLockManager _localLockManager;
    private readonly IOrderedRpcManager _rpcManager;
    private readonly IDistributedLockService _distributedLockService;
    private readonly IRpcService _rpcService;
    private readonly ILogger _logger;

    // 装备数据访问
    private readonly IEquipmentRepository _equipmentRepo;
    private readonly IPlayerRepository _playerRepo;

    public EquipmentEnhanceService(
        string currentServerId,
        IPlayerRoutingService routingService,
        ILocalResourceLockManager localLockManager,
        IOrderedRpcManager rpcManager,
        IDistributedLockService distributedLockService,
        IEquipmentRepository equipmentRepo,
        IPlayerRepository playerRepo,
        ILogger logger) {

        _currentServerId = currentServerId;
        _routingService = routingService;
        _localLockManager = localLockManager;
        _rpcManager = rpcManager;
        _distributedLockService = distributedLockService;
        _equipmentRepo = equipmentRepo;
        _playerRepo = playerRepo;
        _logger = logger;
    }

    // 执行装备强化
    public async Task<EnhanceResult> EnhanceEquipment(
        string playerId, Guid equipmentId, int targetLevel, bool useProtection = false) {

        string requestId = Guid.NewGuid().ToString();

        try {
            // 1. 定位玩家所在服务器
            string playerServerId = await _routingService.LocatePlayer(playerId);

            // 2. 判断是本地操作还是远程操作
            if (playerServerId == _currentServerId) {
                // 本地操作
                return await ExecuteLocalEnhancement(
                    playerId, equipmentId, targetLevel, useProtection, requestId);
            }
            else {
                // 远程操作（通过有序RPC执行）
                return await _rpcManager.SendOrderedRequest<EnhanceResult>(
                    playerId,
                    "EnhanceEquipment",
                    new EnhanceRequest {
                        PlayerId = playerId,
                        EquipmentId = equipmentId,
                        TargetLevel = targetLevel,
                        UseProtection = useProtection,
                        RequestId = requestId
                    });
            }
        }
        catch (PlayerNotFoundException) {
            _logger.Warning($"Player {playerId} not found in routing table");
            return new EnhanceResult {
                Success = false,
                ErrorCode = EnhanceErrorCode.PlayerNotFound
            };
        }
        catch (Exception ex) {
            _logger.Error($"Error in enhance equipment operation: {ex.Message}");
            return new EnhanceResult {
                Success = false,
                ErrorCode = EnhanceErrorCode.ServerError,
                ErrorMessage = "Internal server error"
            };
        }
    }

    // 执行本地强化操作
    private async Task<EnhanceResult> ExecuteLocalEnhancement(
        string playerId, Guid equipmentId, int targetLevel, bool useProtection, string requestId) {

        // 1. 获取资源锁
        string resourceKey = $"player:{playerId}:equipment:{equipmentId}";

        // 尝试获取分布式锁，确保跨服务器的一致性
        var lockResult = await _distributedLockService.AcquireGlobalLock(
            resourceKey, requestId, _currentServerId, TimeSpan.FromSeconds(15));

        if (!lockResult.Success) {
            return new EnhanceResult {
                Success = false,
                ErrorCode = EnhanceErrorCode.ResourceLocked,
                ErrorMessage = "Equipment is currently being used in another operation"
            };
        }

        try {
            // 2. 获取装备和玩家数据
            var equipment = await _equipmentRepo.GetEquipmentAsync(equipmentId);
            var player = await _playerRepo.GetPlayerAsync(playerId);

            // 3. 验证基本条件
            if (equipment == null || equipment.PlayerId != playerId) {
                return new EnhanceResult {
                    Success = false,
                    ErrorCode = EnhanceErrorCode.EquipmentNotFound,
                    ErrorMessage = "Equipment not found or does not belong to player"
                };
            }

            if (equipment.EnhanceLevel >= targetLevel) {
                return new EnhanceResult {
                    Success = false,
                    ErrorCode = EnhanceErrorCode.InvalidTargetLevel,
                    ErrorMessage = "Target level must be higher than current level"
                };
            }

            if (targetLevel > equipment.MaxEnhanceLevel) {
                return new EnhanceResult {
                    Success = false,
                    ErrorCode = EnhanceErrorCode.InvalidTargetLevel,
                    ErrorMessage = $"Target level exceeds maximum enhance level ({equipment.MaxEnhanceLevel})"
                };
            }

            // 4. 计算强化消耗
            var cost = CalculateEnhancementCost(equipment, targetLevel, useProtection);

            // 5. 检查资源是否足够
            if (player.Gold < cost.Gold) {
                return new EnhanceResult {
                    Success = false,
                    ErrorCode = EnhanceErrorCode.InsufficientGold,
                    ErrorMessage = $"Insufficient gold. Required: {cost.Gold}, Available: {player.Gold}"
                };
            }

            if (player.EnhanceStones < cost.EnhanceStones) {
                return new EnhanceResult {
                    Success = false,
                    ErrorCode = EnhanceErrorCode.InsufficientMaterials,
                    ErrorMessage = $"Insufficient enhance stones. Required: {cost.EnhanceStones}, Available: {player.EnhanceStones}"
                };
            }

            if (useProtection && player.ProtectionScrolls < cost.ProtectionScrolls) {
                return new EnhanceResult {
                    Success = false,
                    ErrorCode = EnhanceErrorCode.InsufficientMaterials,
                    ErrorMessage = $"Insufficient protection scrolls. Required: {cost.ProtectionScrolls}, Available: {player.ProtectionScrolls}"
                };
            }

            // 6. 创建操作日志
            var operationLog = new EnhanceOperationLog {
                OperationId = Guid.NewGuid(),
                PlayerId = playerId,
                EquipmentId = equipmentId,
                RequestId = requestId,
                InitialLevel = equipment.EnhanceLevel,
                TargetLevel = targetLevel,
                UseProtection = useProtection,
                Cost = cost,
                Timestamp = DateTime.UtcNow,
                Status = OperationStatus.InProgress
            };

            // 持久化操作日志
            await _equipmentRepo.SaveOperationLogAsync(operationLog);

            try {
                // 7. 扣除资源
                player.Gold -= cost.Gold;
                player.EnhanceStones -= cost.EnhanceStones;

                if (useProtection) {
                    player.ProtectionScrolls -= cost.ProtectionScrolls;
                }

                // 8. 计算强化成功率
                double successRate = CalculateSuccessRate(equipment, targetLevel);
                bool enhanceSuccess = DetermineEnhanceSuccess(successRate, useProtection);

                // 9. 应用强化结果
                EnhanceResult result;

                if (enhanceSuccess) {
                    // 强化成功
                    int oldLevel = equipment.EnhanceLevel;
                    equipment.EnhanceLevel = targetLevel;

                    // 更新装备属性
                    UpdateEquipmentStats(equipment);

                    result = new EnhanceResult {
                        Success = true,
                        EnhanceSuccess = true,
                        OldLevel = oldLevel,
                        NewLevel = targetLevel,
                        Equipment = equipment,
                        ResourceChange = new ResourceChange {
                            Gold = -cost.Gold,
                            EnhanceStones = -cost.EnhanceStones,
                            ProtectionScrolls = useProtection ? -cost.ProtectionScrolls : 0
                        }
                    };
                }
                else {
                    // 强化失败
                    if (!useProtection && equipment.EnhanceLevel > 0 && ShouldDecreaseLevelOnFailure(equipment)) {
                        // 未使用保护卷且需要降级
                        int oldLevel = equipment.EnhanceLevel;
                        equipment.EnhanceLevel = Math.Max(0, equipment.EnhanceLevel - 1);

                        // 更新装备属性
                        UpdateEquipmentStats(equipment);

                        result = new EnhanceResult {
                            Success = true,
                            EnhanceSuccess = false,
                            OldLevel = oldLevel,
                            NewLevel = equipment.EnhanceLevel,
                            Equipment = equipment,
                            ResourceChange = new ResourceChange {
                                Gold = -cost.Gold,
                                EnhanceStones = -cost.EnhanceStones,
                                ProtectionScrolls = 0
                            },
                            LevelDecreased = true
                        };
                    }
                    else {
                        // 失败但不降级（使用了保护卷或不需要降级）
                        result = new EnhanceResult {
                            Success = true,
                            EnhanceSuccess = false,
                            OldLevel = equipment.EnhanceLevel,
                            NewLevel = equipment.EnhanceLevel,
                            Equipment = equipment,
                            ResourceChange = new ResourceChange {
                                Gold = -cost.Gold,
                                EnhanceStones = -cost.EnhanceStones,
                                ProtectionScrolls = useProtection ? -cost.ProtectionScrolls : 0
                            },
                            LevelDecreased = false
                        };
                    }
                }

                // 10. 保存数据
                await _equipmentRepo.UpdateEquipmentAsync(equipment);
                await _playerRepo.UpdatePlayerAsync(player);

                // 11. 更新操作日志
                operationLog.Status = OperationStatus.Completed;
                operationLog.Result = enhanceSuccess ? "Success" : "Failed";
                operationLog.FinalLevel = equipment.EnhanceLevel;
                await _equipmentRepo.UpdateOperationLogAsync(operationLog);

                return result;
            }
            catch (Exception ex) {
                // 操作过程中出现异常，记录日志
                operationLog.Status = OperationStatus.Failed;
                operationLog.Result = $"Error: {ex.Message}";
                await _equipmentRepo.UpdateOperationLogAsync(operationLog);

                throw;
            }
        }
        finally {
            // 确保无论成功失败都释放锁
            await _distributedLockService.ReleaseGlobalLock(
                resourceKey, requestId, _currentServerId);
        }
    }

    // 计算强化消耗
    private EnhancementCost CalculateEnhancementCost(Equipment equipment, int targetLevel, bool useProtection) {
        int levelDiff = targetLevel - equipment.EnhanceLevel;

        // 基础消耗公式
        int baseGold = equipment.Quality * 100 * levelDiff;
        int baseStones = levelDiff * 5;

        // 高等级强化额外成本
        if (targetLevel > 10) {
            baseGold += (targetLevel - 10) * 500 * levelDiff;
            baseStones += (targetLevel - 10) * 3 * levelDiff;
        }

        // 使用保护卷的额外消耗
        int protectionScrolls = 0;
        if (useProtection) {
            protectionScrolls = targetLevel >= 15 ? 2 : 1;
            baseGold = (int)(baseGold * 1.2); // 使用保护卷时金币消耗增加20%
        }

        return new EnhancementCost {
            Gold = baseGold,
            EnhanceStones = baseStones,
            ProtectionScrolls = protectionScrolls
        };
    }

    // 计算强化成功率
    private double CalculateSuccessRate(Equipment equipment, int targetLevel) {
        // 基础成功率
        double baseRate = 1.0;

        // 目标等级越高，成功率越低
        if (targetLevel > 5) {
            baseRate = 0.9;
        }
        if (targetLevel > 10) {
            baseRate = 0.7;
        }
        if (targetLevel > 15) {
            baseRate = 0.4;
        }
        if (targetLevel > 20) {
            baseRate = 0.2;
        }

        // 装备品质影响
        double qualityFactor = 1.0 + (equipment.Quality * 0.02);

        // 最终成功率
        double finalRate = baseRate * qualityFactor;

        // 限制范围在[0.1, 0.95]
        return Math.Min(0.95, Math.Max(0.1, finalRate));
    }

    // 确定强化是否成功
    private bool DetermineEnhanceSuccess(double successRate, bool useProtection) {
        // 随机数生成
        Random random = new Random();
        double roll = random.NextDouble();

        // 使用保护卷时成功率提高15%
        if (useProtection) {
            successRate = Math.Min(0.95, successRate + 0.15);
        }

        return roll < successRate;
    }

    // 判断失败时是否需要降级
    private bool ShouldDecreaseLevelOnFailure(Equipment equipment) {
        // 10级以下失败不降级
        if (equipment.EnhanceLevel < 10) {
            return false;
        }

        // 10-15级有30%概率降级
        if (equipment.EnhanceLevel >= 10 && equipment.EnhanceLevel <= 15) {
            return new Random().NextDouble() < 0.3;
        }

        // 15级以上有60%概率降级
        return new Random().NextDouble() < 0.6;
    }

    // 更新装备属性
    private void UpdateEquipmentStats(Equipment equipment) {
        // 基础属性
        double enhanceFactor = 1.0 + (equipment.EnhanceLevel * 0.1);

        equipment.Attack = (int)(equipment.BaseAttack * enhanceFactor);
        equipment.Defense = (int)(equipment.BaseDefense * enhanceFactor);

        // 高级强化特殊加成
        if (equipment.EnhanceLevel >= 10) {
            equipment.CriticalRate += (equipment.EnhanceLevel - 9) * 0.01;
        }

        if (equipment.EnhanceLevel >= 15) {
            equipment.DamageReduction += (equipment.EnhanceLevel - 14) * 0.01;
        }

        if (equipment.EnhanceLevel >= 20) {
            equipment.SpecialEffects.Add($"+{equipment.EnhanceLevel - 19}% Skill Damage");
        }
    }
}

// 装备强化请求
public class EnhanceRequest
{
    public string PlayerId { get; set; }
    public Guid EquipmentId { get; set; }
    public int TargetLevel { get; set; }
    public bool UseProtection { get; set; }
    public string RequestId { get; set; }
    public int ClientSequence { get; set; }
}


// 强化结果
public class EnhanceResult {
    public bool Success { get; set; }
    public EnhanceErrorCode ErrorCode { get; set; }
    public string ErrorMessage { get; set; }

    // 强化成功相关信息
    public bool EnhanceSuccess { get; set; }
    public int OldLevel { get; set; }
    public int NewLevel { get; set; }
    public Equipment Equipment { get; set; }
    public ResourceChange ResourceChange { get; set; }
    public bool LevelDecreased { get; set; }
}

// 资源变化
public class ResourceChange {
    public int Gold { get; set; }
    public int EnhanceStones { get; set; }
    public int ProtectionScrolls { get; set; }
}
