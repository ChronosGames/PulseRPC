using Microsoft.Extensions.Logging;
using GameApp.Shared.Services;

namespace GameApp.BattleServer.Services;

/// <summary>
/// 技能系统实现
/// </summary>
public class SkillSystem : ISkillSystem
{
    private readonly IBattleCacheService _battleCacheService;
    private readonly IDamageCalculator _damageCalculator;
    private readonly IBuffSystem _buffSystem;
    private readonly ILogger<SkillSystem> _logger;

    public SkillSystem(
        IBattleCacheService battleCacheService,
        IDamageCalculator damageCalculator,
        IBuffSystem buffSystem,
        ILogger<SkillSystem> logger)
    {
        _battleCacheService = battleCacheService;
        _damageCalculator = damageCalculator;
        _buffSystem = buffSystem;
        _logger = logger;
    }

    /// <summary>
    /// 验证技能使用条件
    /// </summary>
    public async Task<bool> ValidateSkillUseAsync(BattlePlayer player, PlayerSkill skill, UseSkillRequest request)
    {
        try
        {
            // 检查玩家是否存活
            if (!player.Status.IsAlive)
            {
                _logger.LogDebug("Player {PlayerId} is not alive", player.PlayerId);
                return false;
            }

            // 检查是否被眩晕
            if (player.Status.IsStunned)
            {
                _logger.LogDebug("Player {PlayerId} is stunned", player.PlayerId);
                return false;
            }

            // 检查法力值
            if (player.Status.Mana < skill.ManaCost)
            {
                _logger.LogDebug("Player {PlayerId} insufficient mana: {CurrentMana} < {RequiredMana}",
                    player.PlayerId, player.Status.Mana, skill.ManaCost);
                return false;
            }

            // 检查技能冷却
            var isOnCooldown = await _battleCacheService.IsSkillOnCooldownAsync(player.PlayerId, skill.SkillId);
            if (isOnCooldown)
            {
                _logger.LogDebug("Skill {SkillId} is on cooldown for player {PlayerId}",
                    skill.SkillId, player.PlayerId);
                return false;
            }

            // 检查目标有效性
            if (!ValidateSkillTargets(player, skill, request))
            {
                _logger.LogDebug("Invalid targets for skill {SkillId}", skill.SkillId);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating skill use for player {PlayerId}, skill {SkillId}",
                player.PlayerId, skill.SkillId);
            return false;
        }
    }

    /// <summary>
    /// 计算技能伤害
    /// </summary>
    public async Task<List<DamageInfo>> CalculateSkillDamageAsync(BattlePlayer caster, PlayerSkill skill, List<BattlePlayer> targets)
    {
        var damageResults = new List<DamageInfo>();

        try
        {
            foreach (var target in targets.Take(skill.Effects.MaxTargets))
            {
                var damageInfo = new DamageInfo
                {
                    TargetPlayerId = target.PlayerId
                };

                // 根据技能类型计算不同伤害
                switch (skill.Type)
                {
                    case SkillType.Attack:
                        // 计算伤害类型（简化实现，实际可以根据技能配置）
                        var damageType = DetermineSkillDamageType(skill);
                        damageInfo.Type = damageType;

                        var damage = damageType switch
                        {
                            DamageType.Physical => _damageCalculator.CalculatePhysicalDamage(caster, target, skill.Effects.BaseDamage),
                            DamageType.Magical => _damageCalculator.CalculateMagicalDamage(caster, target, skill.Effects.BaseDamage),
                            DamageType.True => _damageCalculator.CalculateTrueDamage(skill.Effects.BaseDamage),
                            _ => skill.Effects.BaseDamage
                        };

                        // 检查暴击
                        var isCritical = _damageCalculator.CalculateCriticalHit(caster);
                        if (isCritical)
                        {
                            damage = (int)(damage * 1.5f); // 暴击1.5倍伤害
                            damageInfo.IsCritical = true;
                        }

                        // 检查闪避
                        var isDodged = _damageCalculator.CalculateDodge(target);
                        if (isDodged)
                        {
                            damage = 0;
                            damageInfo.IsDodged = true;
                        }

                        // 检查格挡
                        if (!isDodged)
                        {
                            var isBlocked = _damageCalculator.CalculateBlock(target);
                            if (isBlocked)
                            {
                                damage = (int)(damage * 0.5f); // 格挡减少50%伤害
                                damageInfo.IsBlocked = true;
                            }
                        }

                        damageInfo.Damage = damage;
                        break;

                    case SkillType.Heal:
                        var healing = _damageCalculator.CalculateHealing(caster, target, skill.Effects.Healing);
                        damageInfo.Type = DamageType.Healing;
                        damageInfo.Damage = -healing; // 负数表示治疗
                        break;

                    default:
                        // 其他技能类型不造成直接伤害
                        continue;
                }

                damageResults.Add(damageInfo);
            }

            await Task.CompletedTask; // 避免编译器警告
            return damageResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating skill damage for skill {SkillId}", skill.SkillId);
            return damageResults;
        }
    }

    /// <summary>
    /// 应用技能效果
    /// </summary>
    public async Task ApplySkillEffectsAsync(BattlePlayer caster, PlayerSkill skill, List<BattlePlayer> targets)
    {
        try
        {
            // 扣除施法者法力值
            caster.Status.Mana = Math.Max(0, caster.Status.Mana - skill.ManaCost);

            foreach (var target in targets.Take(skill.Effects.MaxTargets))
            {
                // 应用直接伤害/治疗
                switch (skill.Type)
                {
                    case SkillType.Attack:
                        var damageType = DetermineSkillDamageType(skill);
                        var damage = damageType switch
                        {
                            DamageType.Physical => _damageCalculator.CalculatePhysicalDamage(caster, target, skill.Effects.BaseDamage),
                            DamageType.Magical => _damageCalculator.CalculateMagicalDamage(caster, target, skill.Effects.BaseDamage),
                            DamageType.True => _damageCalculator.CalculateTrueDamage(skill.Effects.BaseDamage),
                            _ => skill.Effects.BaseDamage
                        };

                        // 应用伤害（考虑暴击、闪避、格挡）
                        var finalDamage = CalculateFinalDamage(caster, target, damage);
                        target.Status.Health = Math.Max(0, target.Status.Health - finalDamage);

                        // 检查是否死亡
                        if (target.Status.Health <= 0)
                        {
                            target.Status.IsAlive = false;
                        }

                        target.Status.LastDamageTime = DateTime.UtcNow;
                        break;

                    case SkillType.Heal:
                        var healing = _damageCalculator.CalculateHealing(caster, target, skill.Effects.Healing);
                        target.Status.Health = Math.Min(target.Status.MaxHealth, target.Status.Health + healing);
                        break;
                }

                // 应用Buff效果
                foreach (var buffEffect in skill.Effects.Buffs)
                {
                    var buff = new BuffEffect
                    {
                        BuffId = buffEffect.BuffId,
                        Name = buffEffect.Name,
                        Type = buffEffect.Type,
                        Value = buffEffect.Value,
                        Duration = buffEffect.Duration,
                        StartTime = DateTime.UtcNow,
                        IsStackable = buffEffect.IsStackable,
                        StackCount = 1
                    };

                    await _buffSystem.ApplyBuffAsync(target.PlayerId, buff);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying skill effects for skill {SkillId}", skill.SkillId);
        }
    }

    /// <summary>
    /// 获取技能冷却时间
    /// </summary>
    public float GetSkillCooldown(int playerId, int skillId)
    {
        // 这里应该从缓存中获取剩余冷却时间
        // 简化实现，返回0表示没有冷却
        return 0.0f;
    }

    /// <summary>
    /// 设置技能冷却
    /// </summary>
    public async Task SetSkillCooldownAsync(int playerId, int skillId, float cooldownSeconds)
    {
        try
        {
            var expireTime = DateTime.UtcNow.AddSeconds(cooldownSeconds);
            await _battleCacheService.SetSkillCooldownAsync(playerId, skillId, expireTime);

            _logger.LogDebug("Set skill cooldown: Player {PlayerId}, Skill {SkillId}, Duration {Duration}s",
                playerId, skillId, cooldownSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting skill cooldown for player {PlayerId}, skill {SkillId}",
                playerId, skillId);
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// 验证技能目标
    /// </summary>
    private bool ValidateSkillTargets(BattlePlayer caster, PlayerSkill skill, UseSkillRequest request)
    {
        // 检查是否有目标（如果技能需要目标）
        if (skill.Effects.MaxTargets > 0 &&
            !request.TargetPlayerIds.Any() &&
            skill.Type != SkillType.Area)
        {
            return false;
        }

        // 检查目标数量
        if (request.TargetPlayerIds.Count > skill.Effects.MaxTargets)
        {
            return false;
        }

        // TODO: 检查目标距离、队伍关系等

        return true;
    }

    /// <summary>
    /// 确定技能伤害类型
    /// </summary>
    private DamageType DetermineSkillDamageType(PlayerSkill skill)
    {
        // 简化实现，根据技能ID范围确定伤害类型
        return skill.SkillId switch
        {
            >= 1000 and < 2000 => DamageType.Physical,
            >= 2000 and < 3000 => DamageType.Magical,
            >= 3000 => DamageType.True,
            _ => DamageType.Physical
        };
    }

    /// <summary>
    /// 计算最终伤害（考虑各种因素）
    /// </summary>
    private int CalculateFinalDamage(BattlePlayer attacker, BattlePlayer target, int baseDamage)
    {
        var finalDamage = baseDamage;

        // 检查暴击
        if (_damageCalculator.CalculateCriticalHit(attacker))
        {
            finalDamage = (int)(finalDamage * 1.5f);
        }

        // 检查闪避
        if (_damageCalculator.CalculateDodge(target))
        {
            return 0;
        }

        // 检查格挡
        if (_damageCalculator.CalculateBlock(target))
        {
            finalDamage = (int)(finalDamage * 0.5f);
        }

        return Math.Max(1, finalDamage); // 至少造成1点伤害
    }

    #endregion
}
