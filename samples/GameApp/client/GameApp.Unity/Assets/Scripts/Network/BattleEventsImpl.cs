using System;
using UnityEngine;
using GameApp.Shared.Services;

namespace GameApp.Unity.Network
{
    /// <summary>
    /// 战斗事件监听器实现
    /// </summary>
    public class BattleEventsImpl : IBattleEvents
    {
        // 内部事件，供 BattleClient 订阅
        public event Action<BattleStateUpdateEvent> BattleStateUpdated;
        public event Action<SkillUsedEvent> SkillUsed;
        public event Action<DamageDealtEvent> DamageDealt;
        public event Action<PlayerDefeatedEvent> PlayerDefeated;
        public event Action<BattleEndedEvent> BattleEnded;

        /// <summary>
        /// 战斗状态更新事件
        /// </summary>
        public void OnBattleStateUpdate(BattleStateUpdateEvent eventData)
        {
            try
            {
                Debug.Log($"Battle state updated: {eventData.BattleInfo?.BattleId} - Status: {eventData.BattleInfo?.Status}");
                BattleStateUpdated?.Invoke(eventData);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error handling battle state update: {ex.Message}");
            }
        }

        /// <summary>
        /// 技能使用事件
        /// </summary>
        public void OnSkillUsed(SkillUsedEvent eventData)
        {
            try
            {
                Debug.Log($"Skill used: Player {eventData.UserId} used skill {eventData.SkillId} - Damage: {eventData.Damage}");
                SkillUsed?.Invoke(eventData);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error handling skill used event: {ex.Message}");
            }
        }

        /// <summary>
        /// 伤害事件
        /// </summary>
        public void OnDamageDealt(DamageDealtEvent eventData)
        {
            try
            {
                Debug.Log($"Damage dealt: {eventData.Damage} from {eventData.AttackerId} to {eventData.DefenderId} - Type: {eventData.DamageType}");
                DamageDealt?.Invoke(eventData);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error handling damage dealt event: {ex.Message}");
            }
        }

        /// <summary>
        /// 玩家失败事件
        /// </summary>
        public void OnPlayerDefeated(PlayerDefeatedEvent eventData)
        {
            try
            {
                Debug.Log($"Player defeated: {eventData.PlayerId} by {eventData.KillerId}");
                PlayerDefeated?.Invoke(eventData);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error handling player defeated event: {ex.Message}");
            }
        }

        /// <summary>
        /// 战斗结束事件
        /// </summary>
        public void OnBattleEnded(BattleEndedEvent eventData)
        {
            try
            {
                Debug.Log($"Battle ended: {eventData.BattleId} - Winner: {eventData.WinnerTeam}");
                BattleEnded?.Invoke(eventData);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error handling battle ended event: {ex.Message}");
            }
        }
    }
}
