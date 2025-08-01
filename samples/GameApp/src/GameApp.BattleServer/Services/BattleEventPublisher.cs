using Microsoft.Extensions.Logging;
using PulseRPC.Server.Events;
using GameApp.Shared.Services;

namespace GameApp.BattleServer.Services;

/// <summary>
/// 战斗事件发布器实现
/// </summary>
public class BattleEventPublisher : IBattleEventPublisher
{
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<BattleEventPublisher> _logger;

    public BattleEventPublisher(IEventPublisher eventPublisher, ILogger<BattleEventPublisher> logger)
    {
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <summary>
    /// 发布战斗状态更新事件
    /// </summary>
    public async Task PublishBattleStateUpdateAsync(string battleId, BattleStateUpdateEvent eventData)
    {
        try
        {
            // 发送给所有客户端
            await _eventPublisher.PublishEventAsync("BattleStateUpdate", eventData);

            _logger.LogDebug("Published battle state update: {BattleId} -> Status: {Status}, Players: {PlayerCount}",
                battleId, eventData.Status, eventData.Players.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing battle state update: {BattleId}", battleId);
        }
    }

    /// <summary>
    /// 发布技能使用事件
    /// </summary>
    public async Task PublishSkillUsedAsync(string battleId, SkillUsedEvent eventData)
    {
        try
        {
            // 发送给所有客户端
            await _eventPublisher.PublishEventAsync("SkillUsed", eventData);

            _logger.LogDebug("Published skill used event: {BattleId} -> Player {PlayerId} used skill {SkillId} ({SkillName})",
                battleId, eventData.PlayerId, eventData.Skill.SkillId, eventData.Skill.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing skill used event: {BattleId}", battleId);
        }
    }

    /// <summary>
    /// 发布伤害事件
    /// </summary>
    public async Task PublishDamageDealtAsync(string battleId, DamageDealtEvent eventData)
    {
        try
        {
            // 发送给所有客户端
            await _eventPublisher.PublishEventAsync("DamageDealt", eventData);

            var totalDamage = eventData.DamageResults.Sum(d => d.Damage);
            var targetCount = eventData.DamageResults.Count;

            _logger.LogDebug("Published damage dealt event: {BattleId} -> {SourcePlayerName} dealt {TotalDamage} damage to {TargetCount} targets",
                battleId, eventData.SourcePlayerName, totalDamage, targetCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing damage dealt event: {BattleId}", battleId);
        }
    }

    /// <summary>
    /// 发布玩家击败事件
    /// </summary>
    public async Task PublishPlayerDefeatedAsync(string battleId, PlayerDefeatedEvent eventData)
    {
        try
        {
            // 发送给所有客户端
            await _eventPublisher.PublishEventAsync("PlayerDefeated", eventData);

            _logger.LogInformation("Published player defeated event: {BattleId} -> {PlayerName} defeated by {KillerPlayerName}",
                battleId, eventData.PlayerName, eventData.KillerPlayerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing player defeated event: {BattleId}", battleId);
        }
    }

    /// <summary>
    /// 发布战斗结束事件
    /// </summary>
    public async Task PublishBattleEndedAsync(string battleId, BattleEndedEvent eventData)
    {
        try
        {
            // 发送给所有客户端
            await _eventPublisher.PublishEventAsync("BattleEnded", eventData);

            _logger.LogInformation("Published battle ended event: {BattleId} -> Winner: Team {WinnerTeam}, Reason: {EndReason}, Duration: {Duration}s",
                battleId, eventData.WinnerTeam, eventData.EndReason, eventData.Statistics.Duration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing battle ended event: {BattleId}", battleId);
        }
    }
}
