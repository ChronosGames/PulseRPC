# UpdateManyAsync 使用指南

## 概述

`UpdateManyAsync` 方法用于批量更新 MongoDB 中符合条件的多个文档。该方法使用 MongoDB 原生的 `UpdateDefinition<TEntity>` 类型，提供类型安全和高性能的批量更新操作。

## 方法签名

```csharp
Task<long> UpdateManyAsync(
    Expression<Func<TEntity, bool>> filter,
    UpdateDefinition<TEntity> update,
    CancellationToken cancellationToken = default);
```

### 参数说明

- **filter**: LINQ 表达式,用于筛选需要更新的文档
- **update**: MongoDB UpdateDefinition,定义如何更新文档
- **cancellationToken**: 可选的取消令牌
- **返回值**: 实际更新的文档数量

## 使用示例

### 1. 更新单个字段

```csharp
// 将所有在线玩家的状态更新为离线
var filter = c => c.Status == "Online";
var update = Builders<Character>.Update.Set(c => c.Status, "Offline");
var count = await characterRepository.UpdateManyAsync(filter, update);
Console.WriteLine($"更新了 {count} 个角色的状态");
```

### 2. 更新多个字段

```csharp
// 批量更新角色的等级和经验值
var filter = c => c.Level < 10;
var update = Builders<Character>.Update
    .Set(c => c.Level, 10)
    .Set(c => c.Experience, 0)
    .Set(c => c.UpdatedAt, DateTime.UtcNow);
var count = await characterRepository.UpdateManyAsync(filter, update);
```

### 3. 增加数值字段

```csharp
// 给所有公会成员增加 100 贡献度
var filter = gm => gm.GuildId == guildId;
var update = Builders<GuildMember>.Update.Inc(gm => gm.Contribution, 100);
var count = await guildMemberRepository.UpdateManyAsync(filter, update);
```

### 4. 组合多个更新操作

```csharp
// 批量更新排行榜条目：重置分数并更新赛季
var filter = e => e.LeaderboardType == "Arena" && e.SeasonId == oldSeasonId;
var update = Builders<LeaderboardEntry>.Update
    .Set(e => e.SeasonId, newSeasonId)
    .Set(e => e.Score, 0)
    .Set(e => e.Rank, 0)
    .Set(e => e.UpdatedAt, DateTime.UtcNow);
var count = await leaderboardRepository.UpdateManyAsync(filter, update);
```

### 5. 使用 $push 添加数组元素

```csharp
// 给所有角色添加新的成就
var filter = c => c.Level >= 50;
var achievement = new Achievement
{
    Id = "veteran_achievement",
    Name = "资深玩家",
    UnlockedAt = DateTime.UtcNow
};
var update = Builders<Character>.Update.Push(c => c.Achievements, achievement);
var count = await characterRepository.UpdateManyAsync(filter, update);
```

### 6. 使用 $pull 删除数组元素

```csharp
// 从所有好友列表中移除指定玩家
var filter = f => true; // 所有文档
var update = Builders<Friend>.Update.Pull(f => f.FriendIds, deletedPlayerId);
var count = await friendRepository.UpdateManyAsync(filter, update);
```

### 7. 条件更新（$min, $max）

```csharp
// 更新最低分数记录（仅当新分数更低时）
var filter = e => e.LeaderboardType == "BestTime" && e.PlayerId == playerId;
var update = Builders<LeaderboardEntry>.Update.Min(e => e.Score, newScore);
var count = await leaderboardRepository.UpdateManyAsync(filter, update);
```

### 8. 删除字段

```csharp
// 移除所有角色的临时标记字段
var filter = c => c.Status == "Offline";
var update = Builders<Character>.Update.Unset(c => c.TemporaryFlag);
var count = await characterRepository.UpdateManyAsync(filter, update);
```

### 9. 使用当前日期时间

```csharp
// 批量更新最后活跃时间
var filter = a => a.Status == "Online";
var update = Builders<Account>.Update.CurrentDate(a => a.LastLoginTime);
var count = await accountRepository.UpdateManyAsync(filter, update);
```

### 10. 复杂的组合更新

```csharp
// 批量处理公会解散：更新成员状态并清理公会ID
var filter = gm => gm.GuildId == disbandedGuildId;
var update = Builders<GuildMember>.Update
    .Set(gm => gm.GuildId, null)
    .Set(gm => gm.Role, "None")
    .Set(gm => gm.LeftAt, DateTime.UtcNow)
    .Inc(gm => gm.GuildChangeCount, 1)
    .Unset(gm => gm.GuildRank);
var count = await guildMemberRepository.UpdateManyAsync(filter, update);
```

## 常用 UpdateDefinition 操作符

### 字段操作

| 操作符 | 说明 | 示例 |
|--------|------|------|
| `Set` | 设置字段值 | `.Set(x => x.Name, "新名称")` |
| `Unset` | 删除字段 | `.Unset(x => x.TempData)` |
| `Rename` | 重命名字段 | `.Rename("OldField", "NewField")` |
| `SetOnInsert` | 仅在插入时设置 | `.SetOnInsert(x => x.CreatedAt, DateTime.UtcNow)` |

### 数值操作

| 操作符 | 说明 | 示例 |
|--------|------|------|
| `Inc` | 增加数值 | `.Inc(x => x.Score, 10)` |
| `Mul` | 乘以数值 | `.Mul(x => x.Multiplier, 1.5)` |
| `Min` | 保留较小值 | `.Min(x => x.BestTime, newTime)` |
| `Max` | 保留较大值 | `.Max(x => x.MaxScore, newScore)` |

### 数组操作

| 操作符 | 说明 | 示例 |
|--------|------|------|
| `Push` | 添加元素到数组 | `.Push(x => x.Items, newItem)` |
| `PushEach` | 添加多个元素 | `.PushEach(x => x.Items, newItems)` |
| `Pull` | 删除匹配的元素 | `.Pull(x => x.Items, item)` |
| `PullAll` | 删除多个元素 | `.PullAll(x => x.Items, itemsToRemove)` |
| `PullFilter` | 按条件删除元素 | `.PullFilter(x => x.Items, i => i.IsExpired)` |
| `Pop` | 删除第一个或最后一个元素 | `.PopFirst(x => x.Items)` |
| `AddToSet` | 添加唯一元素 | `.AddToSet(x => x.Tags, "new-tag")` |

### 时间操作

| 操作符 | 说明 | 示例 |
|--------|------|------|
| `CurrentDate` | 设置为当前日期 | `.CurrentDate(x => x.UpdatedAt)` |

## 性能优化建议

### 1. 使用索引

确保过滤条件中使用的字段有合适的索引：

```csharp
// 在 CharacterRepository 构造函数中创建索引
await Collection.Indexes.CreateOneAsync(
    new CreateIndexModel<Character>(
        Builders<Character>.IndexKeys.Ascending(c => c.Status)
    )
);
```

### 2. 批量操作限制

虽然 `UpdateManyAsync` 可以更新大量文档,但建议：
- 单次更新不超过 10,000 条文档
- 对于超大批量,考虑分批处理

```csharp
// 分批更新示例
const int batchSize = 5000;
var processed = 0;
while (true)
{
    var filter = c => c.Status == "Pending";
    var limitedFilter = Builders<Character>.Filter.And(
        Builders<Character>.Filter.Where(filter),
        Builders<Character>.Filter.Lte(c => c.Id, lastProcessedId)
    );

    var update = Builders<Character>.Update.Set(c => c.Status, "Processed");
    var count = await repository.UpdateManyAsync(limitedFilter, update);

    if (count == 0) break;
    processed += (int)count;
}
```

### 3. 使用投影减少数据传输

虽然 `UpdateManyAsync` 不返回完整文档,但在更新后如需读取,使用投影：

```csharp
// 更新后查询受影响的文档
var count = await repository.UpdateManyAsync(filter, update);
if (count > 0)
{
    var updatedDocs = await Collection
        .Find(filter)
        .Project(c => new { c.Id, c.Status })
        .ToListAsync();
}
```

## 注意事项

### 1. 返回值含义

返回值是实际修改的文档数量,而不是匹配的文档数量：

```csharp
var count = await repository.UpdateManyAsync(filter, update);
// count 是实际被修改的文档数量
// 如果字段值已经是目标值，该文档不会被计入
```

### 2. 原子性

每个 `UpdateManyAsync` 调用对单个文档是原子性的,但对多个文档不是事务性的。如需事务,使用 MongoDB 事务：

```csharp
using var session = await mongoClient.StartSessionAsync();
session.StartTransaction();
try
{
    await repository.UpdateManyAsync(filter1, update1, session);
    await repository.UpdateManyAsync(filter2, update2, session);
    await session.CommitTransactionAsync();
}
catch
{
    await session.AbortTransactionAsync();
    throw;
}
```

### 3. 验证更新结果

建议检查返回值以确认更新是否成功：

```csharp
var count = await repository.UpdateManyAsync(filter, update);
if (count == 0)
{
    _logger.LogWarning("No documents were updated");
}
else
{
    _logger.LogInformation($"Successfully updated {count} documents");
}
```

## 实际应用场景

### 场景 1: 每日重置

```csharp
public async Task ResetDailyQuestsAsync()
{
    var filter = c => c.DailyQuestsCompletedAt < DateTime.UtcNow.Date;
    var update = Builders<Character>.Update
        .Set(c => c.DailyQuestsCompleted, 0)
        .Set(c => c.DailyQuestsAvailable, true)
        .Set(c => c.DailyQuestsCompletedAt, DateTime.UtcNow);

    var count = await _characterRepository.UpdateManyAsync(filter, update);
    _logger.LogInformation($"Reset daily quests for {count} characters");
}
```

### 场景 2: 赛季结算

```csharp
public async Task EndSeasonAsync(string seasonId)
{
    // 归档当前赛季数据
    var filter = e => e.SeasonId == seasonId;
    var update = Builders<LeaderboardEntry>.Update
        .Set(e => e.IsArchived, true)
        .Set(e => e.ArchivedAt, DateTime.UtcNow);

    var count = await _leaderboardRepository.UpdateManyAsync(filter, update);

    // 为新赛季重置排名
    var newSeasonUpdate = Builders<LeaderboardEntry>.Update
        .Set(e => e.SeasonId, newSeasonId)
        .Set(e => e.Score, 0)
        .Set(e => e.Rank, 0);

    await _leaderboardRepository.UpdateManyAsync(_ => !_.IsArchived, newSeasonUpdate);
}
```

### 场景 3: 批量奖励发放

```csharp
public async Task GrantEventRewardAsync(string eventId, int rewardGold)
{
    // 给参与活动的玩家发放奖励
    var filter = c => c.ParticipatedEvents.Contains(eventId) && !c.ClaimedEventRewards.Contains(eventId);
    var update = Builders<Character>.Update
        .Inc(c => c.Gold, rewardGold)
        .Push(c => c.ClaimedEventRewards, eventId)
        .CurrentDate(c => c.UpdatedAt);

    var count = await _characterRepository.UpdateManyAsync(filter, update);
    _logger.LogInformation($"Granted rewards to {count} players for event {eventId}");
}
```

## 总结

`UpdateManyAsync` 方法提供了强大而灵活的批量更新能力:

✅ 类型安全 - 使用 `UpdateDefinition<TEntity>` 提供编译时类型检查
✅ 高性能 - 单次数据库操作更新多个文档
✅ 功能丰富 - 支持所有 MongoDB 更新操作符
✅ 易于使用 - 流畅的 API 设计,链式调用

合理使用该方法可以显著提升应用性能,减少数据库往返次数。
