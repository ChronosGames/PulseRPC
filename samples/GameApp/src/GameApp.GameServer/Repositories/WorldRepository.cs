using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using GameApp.Shared.Services;

namespace GameApp.GameServer.Repositories;

/// <summary>
/// 世界数据仓储实现
/// </summary>
public class WorldRepository : IWorldRepository
{
    private readonly IMongoCollection<WorldState> _worldCollection;
    private readonly ILogger<WorldRepository> _logger;

    public WorldRepository(IMongoDatabase database, ILogger<WorldRepository> logger)
    {
        _worldCollection = database.GetCollection<WorldState>("worlds");
        _logger = logger;
    }

    /// <summary>
    /// 根据世界ID获取世界信息
    /// </summary>
    public async Task<WorldState?> GetWorldByIdAsync(string worldId)
    {
        try
        {
            var filter = Builders<WorldState>.Filter.Eq(w => w.WorldId, worldId);
            var world = await _worldCollection.Find(filter).FirstOrDefaultAsync();

            if (world != null)
            {
                _logger.LogDebug("Found world by ID: {WorldId}", worldId);
            }

            return world;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting world by ID: {WorldId}", worldId);
            throw;
        }
    }

    /// <summary>
    /// 获取所有世界列表
    /// </summary>
    public async Task<List<WorldState>> GetAllWorldsAsync()
    {
        try
        {
            var worlds = await _worldCollection.Find(_ => true).ToListAsync();

            _logger.LogDebug("Retrieved {Count} worlds", worlds.Count);

            return worlds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all worlds");
            throw;
        }
    }

    /// <summary>
    /// 更新世界状态
    /// </summary>
    public async Task UpdateWorldStateAsync(WorldState world)
    {
        try
        {
            var filter = Builders<WorldState>.Filter.Eq(w => w.WorldId, world.WorldId);
            var options = new ReplaceOptions { IsUpsert = true };

            await _worldCollection.ReplaceOneAsync(filter, world, options);

            _logger.LogDebug("Updated world state: {WorldId} -> Status: {Status}, Players: {CurrentPlayers}/{MaxPlayers}",
                world.WorldId, world.Status, world.CurrentPlayers, world.MaxPlayers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating world state: {WorldId}", world.WorldId);
            throw;
        }
    }

    /// <summary>
    /// 创建新世界
    /// </summary>
    public async Task<WorldState> CreateWorldAsync(WorldState world)
    {
        try
        {
            await _worldCollection.InsertOneAsync(world);

            _logger.LogInformation("Created new world: {WorldName} (ID: {WorldId})",
                world.Name, world.WorldId);

            return world;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating world: {WorldId}", world.WorldId);
            throw;
        }
    }

    /// <summary>
    /// 删除世界
    /// </summary>
    public async Task DeleteWorldAsync(string worldId)
    {
        try
        {
            var filter = Builders<WorldState>.Filter.Eq(w => w.WorldId, worldId);
            var result = await _worldCollection.DeleteOneAsync(filter);

            if (result.DeletedCount > 0)
            {
                _logger.LogInformation("Deleted world: {WorldId}", worldId);
            }
            else
            {
                _logger.LogWarning("World not found for deletion: {WorldId}", worldId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting world: {WorldId}", worldId);
            throw;
        }
    }
}
