// MongoDB 初始化脚本 - 创建数据库、集合和索引
// 使用方式：
//   docker run -v ./init.js:/docker-entrypoint-initdb.d/init.js mongo
//   或在 docker-compose.yml 中挂载此文件

print("========================================");
print("PulseRPC MongoDB 初始化脚本");
print("========================================");

// ============================================
// 数据库配置
// ============================================

const GUILDS_DB = "game_guilds";        // 公会数据库
const CHARACTERS_DB = "game_characters"; // 角色数据库
const GAME_DB = "game_data";            // 游戏数据

// ============================================
// 1. 公会数据库 (game_guilds)
// ============================================

print("\n[1/3] 初始化公会数据库: " + GUILDS_DB);

db = db.getSiblingDB(GUILDS_DB);

// -------------------- guilds 集合 --------------------
print("  → 创建 guilds 集合索引...");

db.guilds.createIndex(
    { "GuildId": 1 },
    { unique: true, name: "idx_guild_id" }
);
print("    ✓ GuildId 唯一索引");

db.guilds.createIndex(
    { "Name": 1 },
    { unique: true, name: "idx_guild_name" }
);
print("    ✓ Name 唯一索引");

db.guilds.createIndex(
    { "Tag": 1 },
    { name: "idx_guild_tag" }
);
print("    ✓ Tag 索引");

db.guilds.createIndex(
    { "LeaderId": 1 },
    { name: "idx_guild_leader" }
);
print("    ✓ LeaderId 索引");

db.guilds.createIndex(
    { "Level": -1, "Exp": -1 },
    { name: "idx_guild_level_exp" }
);
print("    ✓ Level + Exp 复合索引（倒序）");

// -------------------- guild_members 集合 --------------------
print("  → 创建 guild_members 集合索引...");

db.guild_members.createIndex(
    { "UserId": 1 },
    { unique: true, name: "idx_member_userid" }
);
print("    ✓ UserId 唯一索引");

db.guild_members.createIndex(
    { "GuildId": 1 },
    { name: "idx_member_guildid" }
);
print("    ✓ GuildId 索引");

db.guild_members.createIndex(
    { "GuildId": 1, "Role": 1 },
    { name: "idx_member_guild_role" }
);
print("    ✓ GuildId + Role 复合索引");

db.guild_members.createIndex(
    { "GuildId": 1, "Contribution": -1 },
    { name: "idx_member_contribution" }
);
print("    ✓ GuildId + Contribution 复合索引（贡献度排行）");

// -------------------- guild_join_requests 集合 --------------------
print("  → 创建 guild_join_requests 集合索引...");

db.guild_join_requests.createIndex(
    { "GuildId": 1, "UserId": 1 },
    { name: "idx_request_guild_user" }
);
print("    ✓ GuildId + UserId 复合索引");

db.guild_join_requests.createIndex(
    { "GuildId": 1, "Status": 1 },
    { name: "idx_request_guild_status" }
);
print("    ✓ GuildId + Status 复合索引");

db.guild_join_requests.createIndex(
    { "UserId": 1, "Status": 1 },
    { name: "idx_request_user_status" }
);
print("    ✓ UserId + Status 复合索引");

// -------------------- guild_messages 集合 --------------------
print("  → 创建 guild_messages 集合索引...");

db.guild_messages.createIndex(
    { "GuildId": 1, "Timestamp": -1 },
    { name: "idx_message_guild_time" }
);
print("    ✓ GuildId + Timestamp 复合索引（倒序）");

db.guild_messages.createIndex(
    { "Timestamp": 1 },
    { name: "idx_message_timestamp", expireAfterSeconds: 2592000 } // 30 天 TTL
);
print("    ✓ Timestamp TTL 索引（30 天自动清理）");

// -------------------- guild_activities 集合 --------------------
print("  → 创建 guild_activities 集合索引...");

db.guild_activities.createIndex(
    { "GuildId": 1, "Timestamp": -1 },
    { name: "idx_activity_guild_time" }
);
print("    ✓ GuildId + Timestamp 复合索引（倒序）");

db.guild_activities.createIndex(
    { "GuildId": 1, "ActivityType": 1, "Timestamp": -1 },
    { name: "idx_activity_guild_type_time" }
);
print("    ✓ GuildId + ActivityType + Timestamp 复合索引");

db.guild_activities.createIndex(
    { "Timestamp": 1 },
    { name: "idx_activity_timestamp", expireAfterSeconds: 7776000 } // 90 天 TTL
);
print("    ✓ Timestamp TTL 索引（90 天自动清理）");

// -------------------- guild_announcements 集合 --------------------
print("  → 创建 guild_announcements 集合索引...");

db.guild_announcements.createIndex(
    { "GuildId": 1, "IsPinned": -1, "CreatedAt": -1 },
    { name: "idx_announcement_guild_pin_time" }
);
print("    ✓ GuildId + IsPinned + CreatedAt 复合索引");

db.guild_announcements.createIndex(
    { "Id": 1 },
    { unique: true, name: "idx_announcement_id" }
);
print("    ✓ Id 唯一索引");

print("  ✓ 公会数据库索引创建完成\n");

// ============================================
// 2. 角色数据库 (game_characters)
// ============================================

print("[2/3] 初始化角色数据库: " + CHARACTERS_DB);

db = db.getSiblingDB(CHARACTERS_DB);

// -------------------- characters 集合 --------------------
print("  → 创建 characters 集合索引...");

db.characters.createIndex(
    { "CharacterId": 1 },
    { unique: true, name: "idx_character_id" }
);
print("    ✓ CharacterId 唯一索引");

db.characters.createIndex(
    { "UserId": 1 },
    { name: "idx_character_userid" }
);
print("    ✓ UserId 索引");

db.characters.createIndex(
    { "Name": 1 },
    { unique: true, name: "idx_character_name" }
);
print("    ✓ Name 唯一索引");

db.characters.createIndex(
    { "Level": -1, "Exp": -1 },
    { name: "idx_character_level_exp" }
);
print("    ✓ Level + Exp 复合索引（等级排行）");

db.characters.createIndex(
    { "LastOnlineAt": -1 },
    { name: "idx_character_lastonline" }
);
print("    ✓ LastOnlineAt 索引（倒序）");

db.characters.createIndex(
    { "Class": 1, "Level": -1 },
    { name: "idx_character_class_level" }
);
print("    ✓ Class + Level 复合索引（职业排行）");

db.characters.createIndex(
    { "UserId": 1, "CreatedAt": -1 },
    { name: "idx_character_user_created" }
);
print("    ✓ UserId + CreatedAt 复合索引");

print("  ✓ 角色数据库索引创建完成\n");

// ============================================
// 3. 游戏数据库 (game_data) - 可选
// ============================================

print("[3/3] 初始化游戏数据库: " + GAME_DB);

db = db.getSiblingDB(GAME_DB);

// -------------------- game_sessions 集合（示例）--------------------
print("  → 创建 game_sessions 集合索引...");

db.game_sessions.createIndex(
    { "SessionId": 1 },
    { unique: true, name: "idx_session_id" }
);
print("    ✓ SessionId 唯一索引");

db.game_sessions.createIndex(
    { "UserId": 1, "CreatedAt": -1 },
    { name: "idx_session_user_created" }
);
print("    ✓ UserId + CreatedAt 复合索引");

db.game_sessions.createIndex(
    { "CreatedAt": 1 },
    { name: "idx_session_created", expireAfterSeconds: 86400 } // 24 小时 TTL
);
print("    ✓ CreatedAt TTL 索引（24 小时自动清理）");

print("  ✓ 游戏数据库索引创建完成\n");

// ============================================
// 索引创建完成标志
// ============================================

print("========================================");
print("✓ 所有数据库索引创建完成！");
print("========================================");

// 创建初始化完成标志（可选，用于应用检查）
db = db.getSiblingDB("admin");
db.system_info.insertOne({
    _id: "mongodb_indexes_initialized",
    timestamp: new Date(),
    version: "1.0",
    databases: [GUILDS_DB, CHARACTERS_DB, GAME_DB]
});

print("\n初始化完成标志已写入 admin.system_info");

// ============================================
// 索引统计信息
// ============================================

print("\n========================================");
print("索引统计信息:");
print("========================================");

db = db.getSiblingDB(GUILDS_DB);
print("\n[" + GUILDS_DB + "]");
print("  guilds:                " + db.guilds.getIndexes().length + " 个索引");
print("  guild_members:         " + db.guild_members.getIndexes().length + " 个索引");
print("  guild_join_requests:   " + db.guild_join_requests.getIndexes().length + " 个索引");
print("  guild_messages:        " + db.guild_messages.getIndexes().length + " 个索引");
print("  guild_activities:      " + db.guild_activities.getIndexes().length + " 个索引");
print("  guild_announcements:   " + db.guild_announcements.getIndexes().length + " 个索引");

db = db.getSiblingDB(CHARACTERS_DB);
print("\n[" + CHARACTERS_DB + "]");
print("  characters:            " + db.characters.getIndexes().length + " 个索引");

db = db.getSiblingDB(GAME_DB);
print("\n[" + GAME_DB + "]");
print("  game_sessions:         " + db.game_sessions.getIndexes().length + " 个索引");

print("\n========================================");
print("初始化脚本执行完毕");
print("========================================\n");
