using DistributedGameApp.Shared.Hubs;
using DistributedGameApp.Shared.Messages;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.GameServer.Services.Player;

/// <summary>
/// PlayerService - IPlayerHub 实现
/// </summary>
public partial class PlayerService : IPlayerHub
{
    // ════════════════════════════════════════════════════════════════════════
    // IPlayerHub 实现
    // 注意：这些方法由 Hub 通过 EnqueueAsync 调用，已经在服务队列中执行
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 获取玩家信息
    /// </summary>
    public Task<PlayerInfo?> GetPlayerInfoAsync()
    {
        var character = GetCurrentCharacter();
        if (character == null)
        {
            return Task.FromResult<PlayerInfo?>(null);
        }

        var playerInfo = new PlayerInfo
        {
            PlayerId = PlayerId,
            PlayerName = character.Name,
            Level = character.Level,
            Exp = character.Exp,
            PositionX = _position.X,
            PositionY = _position.Y,
            PositionZ = _position.Z
        };

        return Task.FromResult<PlayerInfo?>(playerInfo);
    }

    /// <summary>
    /// 移动玩家
    /// </summary>
    public Task<MoveResult> MoveAsync(MoveRequest request)
    {
        var character = GetCurrentCharacter();
        if (character == null)
        {
            return Task.FromResult(new MoveResult
            {
                Success = false,
                ErrorMessage = "No character selected"
            });
        }

        // 验证移动距离（简单的反作弊检查）
        var distance = Math.Sqrt(
            Math.Pow(request.TargetX - _position.X, 2) +
            Math.Pow(request.TargetY - _position.Y, 2) +
            Math.Pow(request.TargetZ - _position.Z, 2));

        var maxMoveDistance = character.Attributes.Speed * 2; // 基于角色速度
        if (distance > maxMoveDistance)
        {
            _logger.LogWarning("Player {PlayerId} attempted to move too far: {Distance} > {Max}",
                PlayerId, distance, maxMoveDistance);
            return Task.FromResult(new MoveResult
            {
                Success = false,
                ErrorMessage = "Move distance too large"
            });
        }

        // 更新位置
        _position = new Position
        {
            MapId = _position.MapId,
            X = request.TargetX,
            Y = request.TargetY,
            Z = request.TargetZ
        };

        _logger.LogDebug("Player {PlayerId} moved to ({X}, {Y}, {Z})",
            PlayerId, _position.X, _position.Y, _position.Z);

        return Task.FromResult(new MoveResult
        {
            Success = true,
            CurrentX = _position.X,
            CurrentY = _position.Y,
            CurrentZ = _position.Z
        });
    }

    /// <summary>
    /// 升级玩家
    /// </summary>
    public async Task<PlayerInfo?> LevelUpAsync()
    {
        var character = GetCurrentCharacter();
        if (character == null)
        {
            return null;
        }

        // 检查经验值是否足够
        var requiredExp = CalculateRequiredExp(character.Level);
        if (character.Exp < requiredExp)
        {
            _logger.LogWarning("Player {PlayerId} attempted to level up without enough exp: {Current}/{Required}",
                PlayerId, character.Exp, requiredExp);
            return null;
        }

        // 升级
        character.Level++;
        character.Exp -= requiredExp;

        // 增加属性
        character.Attributes.MaxHp += 10;
        character.Attributes.Hp = character.Attributes.MaxHp;
        character.Attributes.MaxMp += 5;
        character.Attributes.Mp = character.Attributes.MaxMp;
        character.Attributes.Attack += 2;
        character.Attributes.Defense += 1;
        character.Attributes.Speed += 1;

        // 保存到数据库
        await _characterRepository.UpdateAsync(character);

        _logger.LogInformation("Player {PlayerId} leveled up to {Level}",
            PlayerId, character.Level);

        return new PlayerInfo
        {
            PlayerId = PlayerId,
            PlayerName = character.Name,
            Level = character.Level,
            Exp = character.Exp,
            PositionX = _position.X,
            PositionY = _position.Y,
            PositionZ = _position.Z
        };
    }

    /// <summary>
    /// 增加经验值
    /// </summary>
    public async Task<PlayerInfo?> AddExpAsync(long exp)
    {
        var character = GetCurrentCharacter();
        if (character == null)
        {
            return null;
        }

        if (exp <= 0)
        {
            return null;
        }

        character.Exp += exp;

        // 检查是否可以自动升级
        while (character.Exp >= CalculateRequiredExp(character.Level))
        {
            var requiredExp = CalculateRequiredExp(character.Level);
            character.Level++;
            character.Exp -= requiredExp;

            // 增加属性
            character.Attributes.MaxHp += 10;
            character.Attributes.Hp = character.Attributes.MaxHp;
            character.Attributes.Attack += 2;

            _logger.LogInformation("Player {PlayerId} auto leveled up to {Level}",
                PlayerId, character.Level);
        }

        // 保存到数据库
        await _characterRepository.UpdateAsync(character);

        return new PlayerInfo
        {
            PlayerId = PlayerId,
            PlayerName = character.Name,
            Level = character.Level,
            Exp = character.Exp,
            PositionX = _position.X,
            PositionY = _position.Y,
            PositionZ = _position.Z
        };
    }

    // ════════════════════════════════════════════════════════════════════════
    // 内部辅助方法
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 计算升级所需经验
    /// </summary>
    private static long CalculateRequiredExp(int level)
    {
        return (long)(100 * Math.Pow(1.5, level - 1));
    }
}
