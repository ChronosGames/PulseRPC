using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using GameApp.Shared.Services;

namespace GameApp.BattleServer.Services;

/// <summary>
/// 技能数据仓储实现
/// </summary>
public class SkillRepository : ISkillRepository
{
    private readonly IMongoCollection<PlayerSkill> _skillTemplateCollection;
    private readonly IMongoCollection<PlayerSkillDocument> _playerSkillCollection;
    private readonly ILogger<SkillRepository> _logger;

    public SkillRepository(IMongoDatabase database, ILogger<SkillRepository> logger)
    {
        _skillTemplateCollection = database.GetCollection<PlayerSkill>("skill_templates");
        _playerSkillCollection = database.GetCollection<PlayerSkillDocument>("player_skills");
        _logger = logger;
    }

    /// <summary>
    /// 获取技能模板
    /// </summary>
    public async Task<PlayerSkill?> GetSkillTemplateAsync(int skillId)
    {
        try
        {
            var filter = Builders<PlayerSkill>.Filter.Eq(s => s.SkillId, skillId);
            var skill = await _skillTemplateCollection.Find(filter).FirstOrDefaultAsync();

            if (skill == null)
            {
                // 如果数据库中没有，返回默认技能模板
                skill = GetDefaultSkillTemplate(skillId);
            }

            if (skill != null)
            {
                _logger.LogDebug("Retrieved skill template: {SkillId} ({SkillName})", skillId, skill.Name);
            }

            return skill;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting skill template: {SkillId}", skillId);
            return GetDefaultSkillTemplate(skillId);
        }
    }

    /// <summary>
    /// 获取玩家技能
    /// </summary>
    public async Task<List<PlayerSkill>> GetPlayerSkillsAsync(int playerId)
    {
        try
        {
            var filter = Builders<PlayerSkillDocument>.Filter.Eq(ps => ps.PlayerId, playerId);
            var playerSkillDoc = await _playerSkillCollection.Find(filter).FirstOrDefaultAsync();

            if (playerSkillDoc?.Skills != null)
            {
                _logger.LogDebug("Retrieved {SkillCount} skills for player {PlayerId}",
                    playerSkillDoc.Skills.Count, playerId);
                return playerSkillDoc.Skills;
            }
            else
            {
                // 如果玩家没有技能，返回默认技能
                var defaultSkills = GetDefaultPlayerSkills();
                if (defaultSkills.Any())
                {
                    // 保存默认技能到数据库
                    await SavePlayerSkillsAsync(playerId, defaultSkills);
                }
                return defaultSkills;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting player skills: {PlayerId}", playerId);
            return GetDefaultPlayerSkills();
        }
    }

    /// <summary>
    /// 保存玩家技能
    /// </summary>
    public async Task SavePlayerSkillAsync(int playerId, PlayerSkill skill)
    {
        try
        {
            var filter = Builders<PlayerSkillDocument>.Filter.Eq(ps => ps.PlayerId, playerId);
            var playerSkillDoc = await _playerSkillCollection.Find(filter).FirstOrDefaultAsync();

            if (playerSkillDoc == null)
            {
                // 创建新的玩家技能文档
                playerSkillDoc = new PlayerSkillDocument
                {
                    PlayerId = playerId,
                    Skills = new List<PlayerSkill> { skill }
                };

                await _playerSkillCollection.InsertOneAsync(playerSkillDoc);
            }
            else
            {
                // 更新现有技能或添加新技能
                var existingSkillIndex = playerSkillDoc.Skills.FindIndex(s => s.SkillId == skill.SkillId);

                if (existingSkillIndex >= 0)
                {
                    playerSkillDoc.Skills[existingSkillIndex] = skill;
                }
                else
                {
                    playerSkillDoc.Skills.Add(skill);
                }

                var update = Builders<PlayerSkillDocument>.Update.Set(ps => ps.Skills, playerSkillDoc.Skills);
                await _playerSkillCollection.UpdateOneAsync(filter, update);
            }

            _logger.LogDebug("Saved skill for player {PlayerId}: {SkillId} ({SkillName})",
                playerId, skill.SkillId, skill.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving player skill: Player {PlayerId}, Skill {SkillId}",
                playerId, skill.SkillId);
            throw;
        }
    }

    /// <summary>
    /// 获取所有技能模板
    /// </summary>
    public async Task<List<PlayerSkill>> GetAllSkillTemplatesAsync()
    {
        try
        {
            var skills = await _skillTemplateCollection.Find(_ => true).ToListAsync();

            if (!skills.Any())
            {
                // 如果数据库中没有技能模板，返回默认模板
                skills = GetAllDefaultSkillTemplates();
            }

            _logger.LogDebug("Retrieved {SkillCount} skill templates", skills.Count);
            return skills;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all skill templates");
            return GetAllDefaultSkillTemplates();
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// 保存玩家技能列表
    /// </summary>
    private async Task SavePlayerSkillsAsync(int playerId, List<PlayerSkill> skills)
    {
        try
        {
            var filter = Builders<PlayerSkillDocument>.Filter.Eq(ps => ps.PlayerId, playerId);
            var playerSkillDoc = new PlayerSkillDocument
            {
                PlayerId = playerId,
                Skills = skills
            };

            var options = new ReplaceOptions { IsUpsert = true };
            await _playerSkillCollection.ReplaceOneAsync(filter, playerSkillDoc, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving player skills: {PlayerId}", playerId);
        }
    }

    /// <summary>
    /// 获取默认技能模板
    /// </summary>
    private PlayerSkill? GetDefaultSkillTemplate(int skillId)
    {
        return skillId switch
        {
            1001 => new PlayerSkill
            {
                SkillId = 1001,
                Name = "基础攻击",
                Description = "对单个目标造成物理伤害",
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
            1002 => new PlayerSkill
            {
                SkillId = 1002,
                Name = "治疗术",
                Description = "恢复目标生命值",
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
            },
            2001 => new PlayerSkill
            {
                SkillId = 2001,
                Name = "火球术",
                Description = "发射火球对目标造成魔法伤害",
                Level = 1,
                MaxLevel = 5,
                Type = SkillType.Attack,
                ManaCost = 40,
                CooldownSeconds = 3.0f,
                Range = 15.0f,
                Effects = new SkillEffects
                {
                    BaseDamage = 150,
                    MaxTargets = 1,
                    Buffs = new List<BuffEffect>
                    {
                        new BuffEffect
                        {
                            BuffId = 2001,
                            Name = "燃烧",
                            Type = BuffType.Burn,
                            Value = 20,
                            Duration = 5.0f
                        }
                    }
                }
            },
            3001 => new PlayerSkill
            {
                SkillId = 3001,
                Name = "力量增强",
                Description = "增加攻击力",
                Level = 1,
                MaxLevel = 3,
                Type = SkillType.Buff,
                ManaCost = 30,
                CooldownSeconds = 60.0f,
                Range = 0.0f,
                Effects = new SkillEffects
                {
                    MaxTargets = 1,
                    Buffs = new List<BuffEffect>
                    {
                        new BuffEffect
                        {
                            BuffId = 3001,
                            Name = "力量增强",
                            Type = BuffType.AttackPowerIncrease,
                            Value = 50,
                            Duration = 30.0f
                        }
                    }
                }
            },
            _ => null
        };
    }

    /// <summary>
    /// 获取默认玩家技能
    /// </summary>
    private List<PlayerSkill> GetDefaultPlayerSkills()
    {
        return new List<PlayerSkill>
        {
            GetDefaultSkillTemplate(1001)!, // 基础攻击
            GetDefaultSkillTemplate(1002)!  // 治疗术
        };
    }

    /// <summary>
    /// 获取所有默认技能模板
    /// </summary>
    private List<PlayerSkill> GetAllDefaultSkillTemplates()
    {
        return new List<PlayerSkill>
        {
            GetDefaultSkillTemplate(1001)!, // 基础攻击
            GetDefaultSkillTemplate(1002)!, // 治疗术
            GetDefaultSkillTemplate(2001)!, // 火球术
            GetDefaultSkillTemplate(3001)!  // 力量增强
        };
    }

    #endregion
}

/// <summary>
/// 玩家技能文档（用于MongoDB存储）
/// </summary>
public class PlayerSkillDocument
{
    public int PlayerId { get; set; }
    public List<PlayerSkill> Skills { get; set; } = new();
}
