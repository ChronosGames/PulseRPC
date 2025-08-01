using GameApp.Shared.Services;

namespace GameApp.BattleServer.Services;

/// <summary>
/// 战斗服务实现接口
/// </summary>
public interface IBattleServiceImpl
{
    /// <summary>
    /// 加入战斗
    /// </summary>
    Task<JoinBattleResponse> JoinBattleAsync(JoinBattleRequest request);

    /// <summary>
    /// 离开战斗
    /// </summary>
    Task LeaveBattleAsync(LeaveBattleRequest request);

    /// <summary>
    /// 使用技能
    /// </summary>
    Task<SkillResult> UseSkillAsync(UseSkillRequest request);

    /// <summary>
    /// 移动战斗位置
    /// </summary>
    Task MoveBattlePositionAsync(MoveBattlePositionRequest request);

    /// <summary>
    /// 获取战斗信息
    /// </summary>
    Task<BattleInfo> GetBattleInfoAsync(GetBattleInfoRequest request);
}

/// <summary>
/// 技能服务实现接口
/// </summary>
public interface ISkillServiceImpl
{
    /// <summary>
    /// 学习技能
    /// </summary>
    Task<LearnSkillResponse> LearnSkillAsync(LearnSkillRequest request);

    /// <summary>
    /// 升级技能
    /// </summary>
    Task<UpgradeSkillResponse> UpgradeSkillAsync(UpgradeSkillRequest request);

    /// <summary>
    /// 获取玩家技能
    /// </summary>
    Task<PlayerSkillsResponse> GetPlayerSkillsAsync(GetPlayerSkillsRequest request);
}

/// <summary>
/// 战斗事件发布器接口
/// </summary>
public interface IBattleEventPublisher
{
    /// <summary>
    /// 发布战斗状态更新事件
    /// </summary>
    Task PublishBattleStateUpdateAsync(string battleId, BattleStateUpdateEvent eventData);

    /// <summary>
    /// 发布技能使用事件
    /// </summary>
    Task PublishSkillUsedAsync(string battleId, SkillUsedEvent eventData);

    /// <summary>
    /// 发布伤害事件
    /// </summary>
    Task PublishDamageDealtAsync(string battleId, DamageDealtEvent eventData);

    /// <summary>
    /// 发布玩家击败事件
    /// </summary>
    Task PublishPlayerDefeatedAsync(string battleId, PlayerDefeatedEvent eventData);

    /// <summary>
    /// 发布战斗结束事件
    /// </summary>
    Task PublishBattleEndedAsync(string battleId, BattleEndedEvent eventData);
}

/// <summary>
/// 战斗引擎接口
/// </summary>
public interface IBattleEngine
{
    /// <summary>
    /// 初始化战斗引擎
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// 创建战斗房间
    /// </summary>
    Task<string> CreateBattleRoomAsync(string battleType, BattleSettings settings);

    /// <summary>
    /// 处理玩家加入战斗
    /// </summary>
    Task<bool> AddPlayerToBattleAsync(string battleId, BattlePlayer player);

    /// <summary>
    /// 处理玩家离开战斗
    /// </summary>
    Task RemovePlayerFromBattleAsync(string battleId, int playerId, string reason);

    /// <summary>
    /// 处理技能使用
    /// </summary>
    Task<SkillResult> ProcessSkillUseAsync(string battleId, UseSkillRequest request);

    /// <summary>
    /// 更新玩家位置
    /// </summary>
    Task UpdatePlayerPositionAsync(string battleId, int playerId, BattlePosition position);

    /// <summary>
    /// 获取战斗信息
    /// </summary>
    Task<BattleInfo?> GetBattleInfoAsync(string battleId);

    /// <summary>
    /// 战斗引擎Tick处理
    /// </summary>
    Task ProcessBattleTickAsync(string battleId);
}

/// <summary>
/// 技能系统接口
/// </summary>
public interface ISkillSystem
{
    /// <summary>
    /// 验证技能使用条件
    /// </summary>
    Task<bool> ValidateSkillUseAsync(BattlePlayer player, PlayerSkill skill, UseSkillRequest request);

    /// <summary>
    /// 计算技能伤害
    /// </summary>
    Task<List<DamageInfo>> CalculateSkillDamageAsync(BattlePlayer caster, PlayerSkill skill, List<BattlePlayer> targets);

    /// <summary>
    /// 应用技能效果
    /// </summary>
    Task ApplySkillEffectsAsync(BattlePlayer caster, PlayerSkill skill, List<BattlePlayer> targets);

    /// <summary>
    /// 获取技能冷却时间
    /// </summary>
    float GetSkillCooldown(int playerId, int skillId);

    /// <summary>
    /// 设置技能冷却
    /// </summary>
    Task SetSkillCooldownAsync(int playerId, int skillId, float cooldownSeconds);
}

/// <summary>
/// 伤害计算器接口
/// </summary>
public interface IDamageCalculator
{
    /// <summary>
    /// 计算物理伤害
    /// </summary>
    int CalculatePhysicalDamage(BattlePlayer attacker, BattlePlayer target, int baseDamage);

    /// <summary>
    /// 计算魔法伤害
    /// </summary>
    int CalculateMagicalDamage(BattlePlayer attacker, BattlePlayer target, int baseDamage);

    /// <summary>
    /// 计算真实伤害
    /// </summary>
    int CalculateTrueDamage(int baseDamage);

    /// <summary>
    /// 计算治疗量
    /// </summary>
    int CalculateHealing(BattlePlayer healer, BattlePlayer target, int baseHealing);

    /// <summary>
    /// 计算暴击
    /// </summary>
    bool CalculateCriticalHit(BattlePlayer attacker);

    /// <summary>
    /// 计算闪避
    /// </summary>
    bool CalculateDodge(BattlePlayer target);

    /// <summary>
    /// 计算格挡
    /// </summary>
    bool CalculateBlock(BattlePlayer target);
}

/// <summary>
/// Buff系统接口
/// </summary>
public interface IBuffSystem
{
    /// <summary>
    /// 应用Buff效果
    /// </summary>
    Task ApplyBuffAsync(int playerId, BuffEffect buff);

    /// <summary>
    /// 移除Buff效果
    /// </summary>
    Task RemoveBuffAsync(int playerId, int buffId);

    /// <summary>
    /// 更新所有Buff状态
    /// </summary>
    Task UpdateBuffsAsync(int playerId);

    /// <summary>
    /// 获取玩家活跃Buff
    /// </summary>
    Task<List<BuffEffect>> GetActiveBuffsAsync(int playerId);

    /// <summary>
    /// 清除所有Buff
    /// </summary>
    Task ClearAllBuffsAsync(int playerId);
}

/// <summary>
/// 战斗数据仓储接口
/// </summary>
public interface IBattleRepository
{
    /// <summary>
    /// 保存战斗信息
    /// </summary>
    Task SaveBattleInfoAsync(BattleInfo battleInfo);

    /// <summary>
    /// 获取战斗信息
    /// </summary>
    Task<BattleInfo?> GetBattleInfoAsync(string battleId);

    /// <summary>
    /// 保存战斗统计
    /// </summary>
    Task SaveBattleStatisticsAsync(string battleId, BattleStatistics statistics);

    /// <summary>
    /// 获取玩家战斗历史
    /// </summary>
    Task<List<BattleInfo>> GetPlayerBattleHistoryAsync(int playerId, int limit = 10);
}

/// <summary>
/// 技能数据仓储接口
/// </summary>
public interface ISkillRepository
{
    /// <summary>
    /// 获取技能模板
    /// </summary>
    Task<PlayerSkill?> GetSkillTemplateAsync(int skillId);

    /// <summary>
    /// 获取玩家技能
    /// </summary>
    Task<List<PlayerSkill>> GetPlayerSkillsAsync(int playerId);

    /// <summary>
    /// 保存玩家技能
    /// </summary>
    Task SavePlayerSkillAsync(int playerId, PlayerSkill skill);

    /// <summary>
    /// 获取技能列表
    /// </summary>
    Task<List<PlayerSkill>> GetAllSkillTemplatesAsync();
}

/// <summary>
/// 战斗缓存服务接口
/// </summary>
public interface IBattleCacheService
{
    /// <summary>
    /// 缓存战斗信息
    /// </summary>
    Task CacheBattleInfoAsync(BattleInfo battleInfo);

    /// <summary>
    /// 获取缓存的战斗信息
    /// </summary>
    Task<BattleInfo?> GetCachedBattleInfoAsync(string battleId);

    /// <summary>
    /// 缓存玩家战斗状态
    /// </summary>
    Task CachePlayerBattleStateAsync(int playerId, string battleId, BattlePlayer playerState);

    /// <summary>
    /// 获取玩家战斗状态
    /// </summary>
    Task<BattlePlayer?> GetPlayerBattleStateAsync(int playerId);

    /// <summary>
    /// 设置技能冷却
    /// </summary>
    Task SetSkillCooldownAsync(int playerId, int skillId, DateTime expireTime);

    /// <summary>
    /// 检查技能冷却
    /// </summary>
    Task<bool> IsSkillOnCooldownAsync(int playerId, int skillId);

    /// <summary>
    /// 移除战斗缓存
    /// </summary>
    Task RemoveBattleCacheAsync(string battleId);
}
