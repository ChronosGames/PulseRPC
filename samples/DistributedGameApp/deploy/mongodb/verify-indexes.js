// MongoDB 索引验证脚本
// 使用方式：mongosh --file verify-indexes.js
// 或在 Docker 中：docker exec -it pulserpc-mongodb mongosh --file /path/to/verify-indexes.js

print("========================================");
print("MongoDB 索引验证脚本");
print("========================================\n");

// 定义期望的索引配置
const expectedIndexes = {
    "game_guilds": {
        "guilds": [
            { name: "idx_guild_id", keys: { "GuildId": 1 }, unique: true },
            { name: "idx_guild_name", keys: { "Name": 1 }, unique: true },
            { name: "idx_guild_tag", keys: { "Tag": 1 } },
            { name: "idx_guild_leader", keys: { "LeaderId": 1 } },
            { name: "idx_guild_level_exp", keys: { "Level": -1, "Exp": -1 } }
        ],
        "guild_members": [
            { name: "idx_member_userid", keys: { "UserId": 1 }, unique: true },
            { name: "idx_member_guildid", keys: { "GuildId": 1 } },
            { name: "idx_member_guild_role", keys: { "GuildId": 1, "Role": 1 } },
            { name: "idx_member_contribution", keys: { "GuildId": 1, "Contribution": -1 } }
        ],
        "guild_join_requests": [
            { name: "idx_request_guild_user", keys: { "GuildId": 1, "UserId": 1 } },
            { name: "idx_request_guild_status", keys: { "GuildId": 1, "Status": 1 } },
            { name: "idx_request_user_status", keys: { "UserId": 1, "Status": 1 } }
        ],
        "guild_messages": [
            { name: "idx_message_guild_time", keys: { "GuildId": 1, "Timestamp": -1 } },
            { name: "idx_message_timestamp", keys: { "Timestamp": 1 }, ttl: true }
        ],
        "guild_activities": [
            { name: "idx_activity_guild_time", keys: { "GuildId": 1, "Timestamp": -1 } },
            { name: "idx_activity_guild_type_time", keys: { "GuildId": 1, "ActivityType": 1, "Timestamp": -1 } },
            { name: "idx_activity_timestamp", keys: { "Timestamp": 1 }, ttl: true }
        ],
        "guild_announcements": [
            { name: "idx_announcement_guild_pin_time", keys: { "GuildId": 1, "IsPinned": -1, "CreatedAt": -1 } },
            { name: "idx_announcement_id", keys: { "Id": 1 }, unique: true }
        ]
    },
    "game_characters": {
        "characters": [
            { name: "idx_character_id", keys: { "CharacterId": 1 }, unique: true },
            { name: "idx_character_userid", keys: { "UserId": 1 } },
            { name: "idx_character_name", keys: { "Name": 1 }, unique: true },
            { name: "idx_character_level_exp", keys: { "Level": -1, "Exp": -1 } },
            { name: "idx_character_lastonline", keys: { "LastOnlineAt": -1 } },
            { name: "idx_character_class_level", keys: { "Class": 1, "Level": -1 } },
            { name: "idx_character_user_created", keys: { "UserId": 1, "CreatedAt": -1 } }
        ]
    }
};

// 验证函数
function verifyIndexes() {
    let totalChecked = 0;
    let totalMissing = 0;
    let totalExtra = 0;

    for (const dbName in expectedIndexes) {
        print(`\n[检查数据库: ${dbName}]`);
        const database = db.getSiblingDB(dbName);

        for (const collName in expectedIndexes[dbName]) {
            print(`  → 集合: ${collName}`);
            const collection = database.getCollection(collName);
            const actualIndexes = collection.getIndexes();
            const expectedIndexList = expectedIndexes[dbName][collName];

            // 检查每个期望的索引
            for (const expectedIdx of expectedIndexList) {
                totalChecked++;
                const found = actualIndexes.find(idx => idx.name === expectedIdx.name);

                if (!found) {
                    print(`    ❌ 缺失索引: ${expectedIdx.name}`);
                    totalMissing++;
                } else {
                    // 验证索引键
                    const keysMatch = JSON.stringify(found.key) === JSON.stringify(expectedIdx.keys);
                    if (!keysMatch) {
                        print(`    ⚠️  索引存在但键不匹配: ${expectedIdx.name}`);
                        print(`       期望: ${JSON.stringify(expectedIdx.keys)}`);
                        print(`       实际: ${JSON.stringify(found.key)}`);
                    } else {
                        print(`    ✓ ${expectedIdx.name}`);
                    }

                    // 验证唯一性约束
                    if (expectedIdx.unique && !found.unique) {
                        print(`    ⚠️  索引应该是唯一的: ${expectedIdx.name}`);
                    }
                }
            }

            // 检查额外的索引
            for (const actualIdx of actualIndexes) {
                if (actualIdx.name === "_id_") continue; // 跳过默认 _id 索引

                const isExpected = expectedIndexList.find(idx => idx.name === actualIdx.name);
                if (!isExpected) {
                    print(`    ℹ️  额外索引: ${actualIdx.name}`);
                    totalExtra++;
                }
            }
        }
    }

    // 统计信息
    print("\n========================================");
    print("验证结果:");
    print("========================================");
    print(`  检查索引总数: ${totalChecked}`);
    print(`  缺失索引数:   ${totalMissing}`);
    print(`  额外索引数:   ${totalExtra}`);

    if (totalMissing === 0) {
        print("\n✓ 所有索引验证通过！");
    } else {
        print("\n❌ 发现缺失索引，请运行 init.js 脚本创建索引");
    }

    return { totalChecked, totalMissing, totalExtra };
}

// 执行验证
const result = verifyIndexes();

// 显示索引使用统计（可选）
print("\n========================================");
print("索引使用统计 (最近的慢查询):");
print("========================================");

db.getSiblingDB("game_guilds").system.profile.find().limit(5).forEach(doc => {
    if (doc.ns && doc.millis > 100) {
        print(`  ${doc.ns}: ${doc.millis}ms`);
    }
});

print("\n========================================");
print("验证完成");
print("========================================\n");
