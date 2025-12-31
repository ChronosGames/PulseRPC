using DistributedGameApp.Infrastructure.MongoDB.Repositories;
using DistributedGameApp.Shared.Domain.Characters;
using DistributedGameApp.Shared.Messages;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Services;
using CreateCharacterRequest = DistributedGameApp.Shared.Messages.CreateCharacterRequest;

namespace DistributedGameApp.GameServer.Services;

/// <summary>
/// 角色管理服务
/// </summary>
/// <remarks>
/// <para><strong>设计模式</strong>:</para>
/// <list type="bullet">
/// <item><description>继承 UnifiedPulseServiceBase，获得生命周期管理和消息队列支持</description></item>
/// <item><description>全局单例，自动启动</description></item>
/// <item><description>无状态服务，操作直接访问数据库</description></item>
/// </list>
/// </remarks>
[PulseService(
    Scenario = ServiceScenario.Actor,  // 单线程顺序执行，保证线程安全
    StartupType = ServiceStartupType.AutoStart,
    InstanceScope = ServiceInstanceScope.Singleton,
    DisplayName = "CharacterService",
    EnableHealthCheck = true)]
public class CharacterService : UnifiedPulseServiceBase
{
    private readonly CharacterRepository _characterRepository;

    // 职业基础属性配置
    private static readonly Dictionary<CharacterClass, (int hp, int mp, int atk, int def, int spd)> ClassBaseStats = new()
    {
        { CharacterClass.Warrior, (150, 30, 15, 10, 8) },
        { CharacterClass.Mage, (80, 100, 20, 5, 10) },
        { CharacterClass.Archer, (100, 50, 18, 7, 12) },
        { CharacterClass.Assassin, (90, 40, 20, 6, 15) },
        { CharacterClass.Priest, (110, 80, 8, 8, 9) }
    };

    public CharacterService(
        CharacterRepository characterRepository,
        ILogger<CharacterService> logger)
        : base("CharacterService", "Global", logger)
    {
        _characterRepository = characterRepository;
    }

    public override Task OnStartingAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("CharacterService starting...");
        return Task.CompletedTask;
    }

    public override Task OnStoppingAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("CharacterService stopping...");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 创建角色
    /// </summary>
    public async Task<CharacterInfo> CreateCharacterAsync(string userId, CreateCharacterRequest request)
    {
        // 验证角色名称是否已存在
        if (await _characterRepository.NameExistsAsync(request.CharacterName))
        {
            throw new InvalidOperationException("角色名称已存在");
        }

        // 检查玩家角色数量限制
        var existingCharacters = await _characterRepository.GetByUserIdAsync(userId);
        if (existingCharacters.Count >= 5) // 最多5个角色
        {
            throw new InvalidOperationException("角色数量已达上限");
        }

        // 获取职业基础属性
        var baseStats = GetClassBaseStats(request.Class);

        // 创建角色
        var character = new Character
        {
            Id = Guid.NewGuid().ToString(),
            CharacterId = Guid.NewGuid().ToString(),
            UserId = userId,
            Name = request.CharacterName,
            Class = request.Class.ToString(),
            Level = 1,
            Exp = 0,
            Gold = 100, // 初始金币
            Diamond = 0,
            Attributes = new CharacterAttributes
            {
                Hp = baseStats.hp,
                MaxHp = baseStats.hp,
                Mp = baseStats.mp,
                MaxMp = baseStats.mp,
                Attack = baseStats.atk,
                Defense = baseStats.def,
                Speed = baseStats.spd
            },
            Inventory = new Inventory
            {
                Capacity = 100,
                Items = new List<InventoryItem>()
            },
            Equipment = new Equipment(),
            CreatedAt = DateTime.UtcNow,
            LastOnlineAt = DateTime.UtcNow
        };

        // 保存到数据库
        await _characterRepository.InsertAsync(character);

        Logger.LogInformation("Character created: {CharacterId} for user {UserId}",
            character.CharacterId, userId);

        // 返回角色信息
        return new CharacterInfo
        {
            CharacterId = character.CharacterId,
            PlayerId = character.UserId,
            CharacterName = character.Name,
            Class = request.Class,
            Gender = request.Gender,
            Level = character.Level,
            Exp = character.Exp,
            Hp = character.Attributes.Hp,
            MaxHp = character.Attributes.MaxHp,
            Mp = character.Attributes.Mp,
            MaxMp = character.Attributes.MaxMp,
            Attack = character.Attributes.Attack,
            Defense = character.Attributes.Defense,
            Speed = character.Attributes.Speed,
            CreatedAt = character.CreatedAt,
            LastLoginAt = character.LastOnlineAt,
            Position = new Position { MapId = "default_map", X = 0, Y = 0, Z = 0 },
            OnlineStatus = OnlineStatus.Online
        };
    }

    /// <summary>
    /// 更新角色等级和经验
    /// </summary>
    public async Task<bool> UpdateCharacterLevelAsync(string characterId, int level, long exp)
    {
        return await _characterRepository.UpdateLevelAsync(characterId, level, exp);
    }

    /// <summary>
    /// 获取职业基础属性
    /// </summary>
    private static (int hp, int mp, int atk, int def, int spd) GetClassBaseStats(CharacterClass characterClass)
    {
        if (ClassBaseStats.TryGetValue(characterClass, out var stats))
        {
            return stats;
        }

        // 默认属性
        return (100, 50, 10, 5, 10);
    }
}
