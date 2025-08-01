using Microsoft.Extensions.Logging;
using PulseRPC;
using GameApp.Shared.Services;

namespace GameApp.BattleServer.Services;

/// <summary>
/// 技能服务实现
/// </summary>
[Channel("TcpChannel")]
public class SkillServiceImpl : IPulseService, ISkillService, ISkillServiceImpl
{
    private readonly ISkillRepository _skillRepository;
    private readonly ILogger<SkillServiceImpl> _logger;

    public SkillServiceImpl(
        ISkillRepository skillRepository,
        ILogger<SkillServiceImpl> logger)
    {
        _skillRepository = skillRepository;
        _logger = logger;
    }

    /// <summary>
    /// 学习技能
    /// </summary>
    public async Task<LearnSkillResponse> LearnSkillAsync(LearnSkillRequest request)
    {
        try
        {
            _logger.LogInformation("Player {PlayerId} learning skill {SkillId}",
                request.PlayerId, request.SkillId);

            // 获取技能模板
            var skillTemplate = await _skillRepository.GetSkillTemplateAsync(request.SkillId);
            if (skillTemplate == null)
            {
                return new LearnSkillResponse
                {
                    Success = false,
                    Message = "技能不存在"
                };
            }

            // 检查玩家是否已经拥有该技能
            var playerSkills = await _skillRepository.GetPlayerSkillsAsync(request.PlayerId);
            var existingSkill = playerSkills.FirstOrDefault(s => s.SkillId == request.SkillId);

            if (existingSkill != null)
            {
                return new LearnSkillResponse
                {
                    Success = false,
                    Message = "已经拥有该技能"
                };
            }

            // 创建玩家技能
            var playerSkill = new PlayerSkill
            {
                SkillId = skillTemplate.SkillId,
                Name = skillTemplate.Name,
                Description = skillTemplate.Description,
                Level = 1,
                MaxLevel = skillTemplate.MaxLevel,
                Type = skillTemplate.Type,
                ManaCost = skillTemplate.ManaCost,
                CooldownSeconds = skillTemplate.CooldownSeconds,
                Range = skillTemplate.Range,
                Effects = skillTemplate.Effects
            };

            // 保存玩家技能
            await _skillRepository.SavePlayerSkillAsync(request.PlayerId, playerSkill);

            _logger.LogInformation("Player {PlayerId} learned skill {SkillId} ({SkillName})",
                request.PlayerId, request.SkillId, playerSkill.Name);

            return new LearnSkillResponse
            {
                Success = true,
                Message = "技能学习成功",
                LearnedSkill = playerSkill
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error learning skill {SkillId} for player {PlayerId}",
                request.SkillId, request.PlayerId);

            return new LearnSkillResponse
            {
                Success = false,
                Message = "技能学习失败"
            };
        }
    }

    /// <summary>
    /// 升级技能
    /// </summary>
    public async Task<UpgradeSkillResponse> UpgradeSkillAsync(UpgradeSkillRequest request)
    {
        try
        {
            _logger.LogInformation("Player {PlayerId} upgrading skill {SkillId}",
                request.PlayerId, request.SkillId);

            // 获取玩家当前技能
            var playerSkills = await _skillRepository.GetPlayerSkillsAsync(request.PlayerId);
            var currentSkill = playerSkills.FirstOrDefault(s => s.SkillId == request.SkillId);

            if (currentSkill == null)
            {
                return new UpgradeSkillResponse
                {
                    Success = false,
                    Message = "玩家尚未学习该技能"
                };
            }

            if (currentSkill.Level >= currentSkill.MaxLevel)
            {
                return new UpgradeSkillResponse
                {
                    Success = false,
                    Message = "技能已达到最大等级"
                };
            }

            // 升级技能
            currentSkill.Level++;

            // 根据等级提升技能效果
            UpdateSkillEffectsForLevel(currentSkill);

            // 保存升级后的技能
            await _skillRepository.SavePlayerSkillAsync(request.PlayerId, currentSkill);

            _logger.LogInformation("Player {PlayerId} upgraded skill {SkillId} to level {NewLevel}",
                request.PlayerId, request.SkillId, currentSkill.Level);

            return new UpgradeSkillResponse
            {
                Success = true,
                Message = $"技能升级成功，当前等级：{currentSkill.Level}",
                UpgradedSkill = currentSkill
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upgrading skill {SkillId} for player {PlayerId}",
                request.SkillId, request.PlayerId);

            return new UpgradeSkillResponse
            {
                Success = false,
                Message = "技能升级失败"
            };
        }
    }

    /// <summary>
    /// 获取玩家技能
    /// </summary>
    public async Task<PlayerSkillsResponse> GetPlayerSkillsAsync(GetPlayerSkillsRequest request)
    {
        try
        {
            var playerSkills = await _skillRepository.GetPlayerSkillsAsync(request.PlayerId);

            _logger.LogDebug("Retrieved {SkillCount} skills for player {PlayerId}",
                playerSkills.Count, request.PlayerId);

            return new PlayerSkillsResponse
            {
                Skills = playerSkills,
                SkillPoints = 0 // 简化实现，技能点暂时为0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting player skills for {PlayerId}", request.PlayerId);

            return new PlayerSkillsResponse
            {
                Skills = new List<PlayerSkill>(),
                SkillPoints = 0
            };
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// 根据等级更新技能效果
    /// </summary>
    private void UpdateSkillEffectsForLevel(PlayerSkill skill)
    {
        var levelMultiplier = 1.0f + (skill.Level - 1) * 0.2f; // 每级增加20%效果

        // 更新伤害和治疗效果
        skill.Effects.BaseDamage = (int)(skill.Effects.BaseDamage * levelMultiplier);
        skill.Effects.Healing = (int)(skill.Effects.Healing * levelMultiplier);

        // 减少法力消耗和冷却时间
        var efficiencyBonus = 1.0f - (skill.Level - 1) * 0.05f; // 每级减少5%消耗
        skill.ManaCost = Math.Max(1, (int)(skill.ManaCost * efficiencyBonus));
        skill.CooldownSeconds = Math.Max(0.5f, skill.CooldownSeconds * efficiencyBonus);

        // 更新Buff效果
        foreach (var buff in skill.Effects.Buffs)
        {
            buff.Value = (int)(buff.Value * levelMultiplier);
            buff.Duration = Math.Min(60.0f, buff.Duration * (1.0f + (skill.Level - 1) * 0.1f)); // 增加持续时间，最大60秒
        }
    }

    #endregion
}
