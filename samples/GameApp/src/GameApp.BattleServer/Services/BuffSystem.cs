using Microsoft.Extensions.Logging;
using GameApp.Shared.Services;
using System.Collections.Concurrent;

namespace GameApp.BattleServer.Services;

/// <summary>
/// Buff系统实现
/// </summary>
public class BuffSystem : IBuffSystem
{
    private readonly ILogger<BuffSystem> _logger;

    // 玩家Buff缓存
    private readonly ConcurrentDictionary<int, List<BuffEffect>> _playerBuffs = new();

    // Buff更新定时器
    private readonly Timer _buffUpdateTimer;

    public BuffSystem(ILogger<BuffSystem> logger)
    {
        _logger = logger;

        // 每秒更新一次Buff状态
        _buffUpdateTimer = new Timer(UpdateAllBuffs, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// 应用Buff效果
    /// </summary>
    public async Task ApplyBuffAsync(int playerId, BuffEffect buff)
    {
        try
        {
            var playerBuffs = _playerBuffs.GetOrAdd(playerId, _ => new List<BuffEffect>());

            lock (playerBuffs)
            {
                // 检查是否可以叠加
                var existingBuff = playerBuffs.FirstOrDefault(b => b.BuffId == buff.BuffId);

                if (existingBuff != null)
                {
                    if (buff.IsStackable)
                    {
                        // 可叠加：增加层数，刷新持续时间
                        existingBuff.StackCount = Math.Min(10, existingBuff.StackCount + buff.StackCount); // 最大10层
                        existingBuff.StartTime = buff.StartTime;
                        existingBuff.Duration = buff.Duration;

                        _logger.LogDebug("Buff stacked: Player {PlayerId}, Buff {BuffId}, Stacks: {StackCount}",
                            playerId, buff.BuffId, existingBuff.StackCount);
                    }
                    else
                    {
                        // 不可叠加：刷新持续时间
                        existingBuff.StartTime = buff.StartTime;
                        existingBuff.Duration = buff.Duration;

                        _logger.LogDebug("Buff refreshed: Player {PlayerId}, Buff {BuffId}",
                            playerId, buff.BuffId);
                    }
                }
                else
                {
                    // 新Buff
                    playerBuffs.Add(buff);

                    _logger.LogDebug("Buff applied: Player {PlayerId}, Buff {BuffId} ({BuffName}), Duration: {Duration}s",
                        playerId, buff.BuffId, buff.Name, buff.Duration);
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying buff {BuffId} to player {PlayerId}",
                buff.BuffId, playerId);
        }
    }

    /// <summary>
    /// 移除Buff效果
    /// </summary>
    public async Task RemoveBuffAsync(int playerId, int buffId)
    {
        try
        {
            if (!_playerBuffs.TryGetValue(playerId, out var playerBuffs))
            {
                return;
            }

            lock (playerBuffs)
            {
                var buffToRemove = playerBuffs.FirstOrDefault(b => b.BuffId == buffId);
                if (buffToRemove != null)
                {
                    playerBuffs.Remove(buffToRemove);

                    _logger.LogDebug("Buff removed: Player {PlayerId}, Buff {BuffId} ({BuffName})",
                        playerId, buffId, buffToRemove.Name);
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing buff {BuffId} from player {PlayerId}",
                buffId, playerId);
        }
    }

    /// <summary>
    /// 更新所有Buff状态
    /// </summary>
    public async Task UpdateBuffsAsync(int playerId)
    {
        try
        {
            if (!_playerBuffs.TryGetValue(playerId, out var playerBuffs))
            {
                return;
            }

            var currentTime = DateTime.UtcNow;
            var expiredBuffs = new List<BuffEffect>();

            lock (playerBuffs)
            {
                // 找出过期的Buff
                foreach (var buff in playerBuffs.ToList())
                {
                    var elapsedTime = (currentTime - buff.StartTime).TotalSeconds;

                    if (elapsedTime >= buff.Duration)
                    {
                        expiredBuffs.Add(buff);
                        playerBuffs.Remove(buff);
                    }
                }
            }

            // 记录过期的Buff
            foreach (var expiredBuff in expiredBuffs)
            {
                _logger.LogDebug("Buff expired: Player {PlayerId}, Buff {BuffId} ({BuffName})",
                    playerId, expiredBuff.BuffId, expiredBuff.Name);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating buffs for player {PlayerId}", playerId);
        }
    }

    /// <summary>
    /// 获取玩家活跃Buff
    /// </summary>
    public async Task<List<BuffEffect>> GetActiveBuffsAsync(int playerId)
    {
        try
        {
            if (!_playerBuffs.TryGetValue(playerId, out var playerBuffs))
            {
                return new List<BuffEffect>();
            }

            lock (playerBuffs)
            {
                // 返回副本，避免并发修改
                return playerBuffs.ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active buffs for player {PlayerId}", playerId);
            return new List<BuffEffect>();
        }
        finally
        {
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// 清除所有Buff
    /// </summary>
    public async Task ClearAllBuffsAsync(int playerId)
    {
        try
        {
            if (_playerBuffs.TryRemove(playerId, out var removedBuffs))
            {
                _logger.LogDebug("Cleared all buffs for player {PlayerId}, Count: {BuffCount}",
                    playerId, removedBuffs.Count);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing all buffs for player {PlayerId}", playerId);
        }
    }

    #region Private Methods

    /// <summary>
    /// 更新所有玩家的Buff状态
    /// </summary>
    private async void UpdateAllBuffs(object? state)
    {
        try
        {
            var playerIds = _playerBuffs.Keys.ToList();

            foreach (var playerId in playerIds)
            {
                await UpdateBuffsAsync(playerId);

                // 如果玩家没有活跃Buff，从缓存中移除
                if (_playerBuffs.TryGetValue(playerId, out var buffs) && !buffs.Any())
                {
                    _playerBuffs.TryRemove(playerId, out _);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in buff update timer");
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _buffUpdateTimer?.Dispose();
    }

    #endregion
}
