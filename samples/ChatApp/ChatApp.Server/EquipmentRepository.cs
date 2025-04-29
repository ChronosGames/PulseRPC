using MongoDB.Driver;
using System;
using System.Threading.Tasks;

namespace ChatApp.Server;

public interface IEquipmentRepository
{
    Task<Equipment> GetEquipmentAsync(Guid equipmentId);
    Task UpdateEquipmentAsync(Equipment equipment);
    Task SaveOperationLogAsync(EnhanceOperationLog operationLog);
    Task UpdateOperationLogAsync(EnhanceOperationLog operationLog);
}

public class EquipmentRepository : IEquipmentRepository
{
    private readonly IMongoCollection<Equipment> _equipmentCollection;
    private readonly IMongoCollection<EnhanceOperationLog> _operationLogCollection;

    public EquipmentRepository(IMongoDatabase database)
    {
        _equipmentCollection = database.GetCollection<Equipment>("Equipments");
        _operationLogCollection = database.GetCollection<EnhanceOperationLog>("EnhanceOperationLogs");
    }

    public async Task<Equipment> GetEquipmentAsync(Guid equipmentId)
    {
        return await _equipmentCollection
            .Find(e => e.Id == equipmentId)
            .FirstOrDefaultAsync();
    }

    public async Task UpdateEquipmentAsync(Equipment equipment)
    {
        await _equipmentCollection.ReplaceOneAsync(
            e => e.Id == equipment.Id,
            equipment);
    }

    public async Task SaveOperationLogAsync(EnhanceOperationLog operationLog)
    {
        await _operationLogCollection.InsertOneAsync(operationLog);
    }

    public async Task UpdateOperationLogAsync(EnhanceOperationLog operationLog)
    {
        var filter = Builders<EnhanceOperationLog>.Filter.Eq(log => log.OperationId, operationLog.OperationId);
        await _operationLogCollection.ReplaceOneAsync(filter, operationLog);
    }
}
