using Microsoft.Extensions.Logging;
using GameApp.Shared.Services;

namespace GameApp.BattleServer.Services;

/// <summary>
/// 伤害计算器实现
/// </summary>
public class DamageCalculator : IDamageCalculator
{
    private readonly ILogger<DamageCalculator> _logger;
    private readonly Random _random = new();

    public DamageCalculator(ILogger<DamageCalculator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 计算物理伤害
    /// </summary>
    public int CalculatePhysicalDamage(BattlePlayer attacker, BattlePlayer target, int baseDamage)
    {
        try
        {
            // 获取攻击者攻击力（简化实现）
            var attackPower = GetPlayerAttackPower(attacker);

            // 获取目标防御力
            var defense = GetPlayerDefense(target);

            // 基础伤害计算：(基础伤害 + 攻击力) * 伤害系数 - 防御力
            var damage = (int)((baseDamage + attackPower) * GetDamageMultiplier() - defense);

            // 最小伤害为1
            damage = Math.Max(1, damage);

            _logger.LogDebug("Physical damage calculated: {Damage} (Base: {BaseDamage}, Attack: {AttackPower}, Defense: {Defense})",
                damage, baseDamage, attackPower, defense);

            return damage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating physical damage");
            return baseDamage;
        }
    }

    /// <summary>
    /// 计算魔法伤害
    /// </summary>
    public int CalculateMagicalDamage(BattlePlayer attacker, BattlePlayer target, int baseDamage)
    {
        try
        {
            // 获取攻击者法术强度
            var spellPower = GetPlayerSpellPower(attacker);

            // 获取目标魔法抗性
            var magicResistance = GetPlayerMagicResistance(target);

            // 魔法伤害计算：(基础伤害 + 法术强度) * 伤害系数 * 魔抗减免
            var damage = (int)((baseDamage + spellPower) * GetDamageMultiplier() * (1.0f - magicResistance));

            damage = Math.Max(1, damage);

            _logger.LogDebug("Magical damage calculated: {Damage} (Base: {BaseDamage}, SpellPower: {SpellPower}, MagicRes: {MagicResistance})",
                damage, baseDamage, spellPower, magicResistance);

            return damage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating magical damage");
            return baseDamage;
        }
    }

    /// <summary>
    /// 计算真实伤害
    /// </summary>
    public int CalculateTrueDamage(int baseDamage)
    {
        // 真实伤害忽略所有防御，但仍有随机性
        var damage = (int)(baseDamage * GetDamageMultiplier());
        return Math.Max(1, damage);
    }

    /// <summary>
    /// 计算治疗量
    /// </summary>
    public int CalculateHealing(BattlePlayer healer, BattlePlayer target, int baseHealing)
    {
        try
        {
            // 获取治疗者治疗强度
            var healingPower = GetPlayerHealingPower(healer);

            // 治疗计算：(基础治疗 + 治疗强度) * 治疗系数
            var healing = (int)((baseHealing + healingPower) * GetHealingMultiplier());

            healing = Math.Max(1, healing);

            _logger.LogDebug("Healing calculated: {Healing} (Base: {BaseHealing}, HealingPower: {HealingPower})",
                healing, baseHealing, healingPower);

            return healing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating healing");
            return baseHealing;
        }
    }

    /// <summary>
    /// 计算暴击
    /// </summary>
    public bool CalculateCriticalHit(BattlePlayer attacker)
    {
        try
        {
            var critChance = GetPlayerCriticalChance(attacker);
            var roll = _random.NextDouble();

            var isCrit = roll < critChance;

            if (isCrit)
            {
                _logger.LogDebug("Critical hit! Player {PlayerId}, Chance: {CritChance:P}, Roll: {Roll:F3}",
                    attacker.PlayerId, critChance, roll);
            }

            return isCrit;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating critical hit");
            return false;
        }
    }

    /// <summary>
    /// 计算闪避
    /// </summary>
    public bool CalculateDodge(BattlePlayer target)
    {
        try
        {
            var dodgeChance = GetPlayerDodgeChance(target);
            var roll = _random.NextDouble();

            var isDodged = roll < dodgeChance;

            if (isDodged)
            {
                _logger.LogDebug("Attack dodged! Player {PlayerId}, Chance: {DodgeChance:P}, Roll: {Roll:F3}",
                    target.PlayerId, dodgeChance, roll);
            }

            return isDodged;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating dodge");
            return false;
        }
    }

    /// <summary>
    /// 计算格挡
    /// </summary>
    public bool CalculateBlock(BattlePlayer target)
    {
        try
        {
            var blockChance = GetPlayerBlockChance(target);
            var roll = _random.NextDouble();

            var isBlocked = roll < blockChance;

            if (isBlocked)
            {
                _logger.LogDebug("Attack blocked! Player {PlayerId}, Chance: {BlockChance:P}, Roll: {Roll:F3}",
                    target.PlayerId, blockChance, roll);
            }

            return isBlocked;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating block");
            return false;
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// 获取玩家攻击力
    /// </summary>
    private int GetPlayerAttackPower(BattlePlayer player)
    {
        // 简化实现：基于玩家等级和装备
        // 实际项目中应该从玩家装备和属性中计算
        var baseAttack = 50; // 基础攻击力
        var levelBonus = 10; // 假设等级为1，每级+10攻击
        var equipmentBonus = CalculateEquipmentAttackBonus(player);
        var buffBonus = CalculateBuffAttackBonus(player);

        return baseAttack + levelBonus + equipmentBonus + buffBonus;
    }

    /// <summary>
    /// 获取玩家防御力
    /// </summary>
    private int GetPlayerDefense(BattlePlayer player)
    {
        var baseDefense = 20;
        var levelBonus = 5;
        var equipmentBonus = CalculateEquipmentDefenseBonus(player);
        var buffBonus = CalculateBuffDefenseBonus(player);

        return baseDefense + levelBonus + equipmentBonus + buffBonus;
    }

    /// <summary>
    /// 获取玩家法术强度
    /// </summary>
    private int GetPlayerSpellPower(BattlePlayer player)
    {
        var baseSpellPower = 40;
        var levelBonus = 8;
        var equipmentBonus = CalculateEquipmentSpellPowerBonus(player);
        var buffBonus = CalculateBuffSpellPowerBonus(player);

        return baseSpellPower + levelBonus + equipmentBonus + buffBonus;
    }

    /// <summary>
    /// 获取玩家魔法抗性
    /// </summary>
    private float GetPlayerMagicResistance(BattlePlayer player)
    {
        var baseResistance = 0.1f; // 10%基础魔抗
        var equipmentBonus = CalculateEquipmentMagicResistanceBonus(player);
        var buffBonus = CalculateBuffMagicResistanceBonus(player);

        return Math.Min(0.75f, baseResistance + equipmentBonus + buffBonus); // 最大75%魔抗
    }

    /// <summary>
    /// 获取玩家治疗强度
    /// </summary>
    private int GetPlayerHealingPower(BattlePlayer player)
    {
        var baseHealingPower = 30;
        var levelBonus = 6;
        var equipmentBonus = CalculateEquipmentHealingPowerBonus(player);
        var buffBonus = CalculateBuffHealingPowerBonus(player);

        return baseHealingPower + levelBonus + equipmentBonus + buffBonus;
    }

    /// <summary>
    /// 获取玩家暴击率
    /// </summary>
    private double GetPlayerCriticalChance(BattlePlayer player)
    {
        var baseCritChance = 0.05; // 5%基础暴击
        var equipmentBonus = CalculateEquipmentCriticalChanceBonus(player);
        var buffBonus = CalculateBuffCriticalChanceBonus(player);

        return Math.Min(0.5, baseCritChance + equipmentBonus + buffBonus); // 最大50%暴击
    }

    /// <summary>
    /// 获取玩家闪避率
    /// </summary>
    private double GetPlayerDodgeChance(BattlePlayer player)
    {
        var baseDodgeChance = 0.02; // 2%基础闪避
        var equipmentBonus = CalculateEquipmentDodgeChanceBonus(player);
        var buffBonus = CalculateBuffDodgeChanceBonus(player);

        return Math.Min(0.3, baseDodgeChance + equipmentBonus + buffBonus); // 最大30%闪避
    }

    /// <summary>
    /// 获取玩家格挡率
    /// </summary>
    private double GetPlayerBlockChance(BattlePlayer player)
    {
        var baseBlockChance = 0.03; // 3%基础格挡
        var equipmentBonus = CalculateEquipmentBlockChanceBonus(player);
        var buffBonus = CalculateBuffBlockChanceBonus(player);

        return Math.Min(0.4, baseBlockChance + equipmentBonus + buffBonus); // 最大40%格挡
    }

    /// <summary>
    /// 获取伤害系数（随机性）
    /// </summary>
    private float GetDamageMultiplier()
    {
        // 伤害浮动在85%-115%之间
        return 0.85f + (float)(_random.NextDouble() * 0.3);
    }

    /// <summary>
    /// 获取治疗系数（随机性）
    /// </summary>
    private float GetHealingMultiplier()
    {
        // 治疗浮动在90%-110%之间
        return 0.9f + (float)(_random.NextDouble() * 0.2);
    }

    // 以下方法计算装备和Buff带来的属性加成
    // 在实际项目中，这些方法需要查询玩家的装备和激活的Buff

    private int CalculateEquipmentAttackBonus(BattlePlayer player) => 0;
    private int CalculateEquipmentDefenseBonus(BattlePlayer player) => 0;
    private int CalculateEquipmentSpellPowerBonus(BattlePlayer player) => 0;
    private float CalculateEquipmentMagicResistanceBonus(BattlePlayer player) => 0.0f;
    private int CalculateEquipmentHealingPowerBonus(BattlePlayer player) => 0;
    private double CalculateEquipmentCriticalChanceBonus(BattlePlayer player) => 0.0;
    private double CalculateEquipmentDodgeChanceBonus(BattlePlayer player) => 0.0;
    private double CalculateEquipmentBlockChanceBonus(BattlePlayer player) => 0.0;

    private int CalculateBuffAttackBonus(BattlePlayer player)
    {
        return player.ActiveBuffs
            .Where(b => b.Type == BuffType.AttackPowerIncrease)
            .Sum(b => b.Value);
    }

    private int CalculateBuffDefenseBonus(BattlePlayer player)
    {
        return player.ActiveBuffs
            .Where(b => b.Type == BuffType.DefenseIncrease)
            .Sum(b => b.Value);
    }

    private int CalculateBuffSpellPowerBonus(BattlePlayer player) => 0;
    private float CalculateBuffMagicResistanceBonus(BattlePlayer player) => 0.0f;
    private int CalculateBuffHealingPowerBonus(BattlePlayer player) => 0;
    private double CalculateBuffCriticalChanceBonus(BattlePlayer player) => 0.0;
    private double CalculateBuffDodgeChanceBonus(BattlePlayer player) => 0.0;
    private double CalculateBuffBlockChanceBonus(BattlePlayer player) => 0.0;

    #endregion
}
