// MongoDB 初始化脚本
// 该脚本在 MongoDB 容器首次启动时自动执行
// 用于创建数据库、集合和测试数据

print('==================== 开始初始化 MongoDB ====================');

// 切换到 admin 数据库（已通过 MONGO_INITDB_ROOT_USERNAME 和 MONGO_INITDB_ROOT_PASSWORD 创建）
db = db.getSiblingDB('admin');

// 创建各个数据库的用户
print('创建数据库用户...');

// game_accounts 数据库用户
db.createUser({
  user: 'game_accounts_user',
  pwd: 'game_accounts_pass',
  roles: [
    { role: 'readWrite', db: 'game_accounts' }
  ]
});

// game_characters 数据库用户
db.createUser({
  user: 'game_characters_user',
  pwd: 'game_characters_pass',
  roles: [
    { role: 'readWrite', db: 'game_characters' }
  ]
});

// game_social 数据库用户
db.createUser({
  user: 'game_social_user',
  pwd: 'game_social_pass',
  roles: [
    { role: 'readWrite', db: 'game_social' }
  ]
});

// game_guilds 数据库用户
db.createUser({
  user: 'game_guilds_user',
  pwd: 'game_guilds_pass',
  roles: [
    { role: 'readWrite', db: 'game_guilds' }
  ]
});

// game_battles 数据库用户
db.createUser({
  user: 'game_battles_user',
  pwd: 'game_battles_pass',
  roles: [
    { role: 'readWrite', db: 'game_battles' }
  ]
});

// game_leaderboards 数据库用户
db.createUser({
  user: 'game_leaderboards_user',
  pwd: 'game_leaderboards_pass',
  roles: [
    { role: 'readWrite', db: 'game_leaderboards' }
  ]
});

print('数据库用户创建完成');

// ==================== 初始化 game_accounts 数据库 ====================
print('初始化 game_accounts 数据库...');

db = db.getSiblingDB('game_accounts');

// 创建 accounts 集合
db.createCollection('accounts');

// 创建索引 - 使用显式名称
db.accounts.createIndex({ userId: 1 }, { unique: true, name: 'userId_unique' });
db.accounts.createIndex({ email: 1 }, { unique: true, sparse: true, name: 'email_unique' });
// provider + providerUserId 索引（仅用于第三方登录账户）
db.accounts.createIndex({ provider: 1, providerUserId: 1 }, {
  unique: true,
  sparse: true,
  partialFilterExpression: { provider: { $ne: "local" } },
  name: 'provider_providerUserId_unique'
});

// 注意：账户系统已启用密码验证
// 请使用客户端的 register 命令创建新账户
// 示例: register <用户名> <密码> <邮箱>
print('accounts 集合已创建，请使用客户端注册新账户');

// ==================== 初始化 game_characters 数据库 ====================
print('初始化 game_characters 数据库...');

db = db.getSiblingDB('game_characters');

// 创建 characters 集合
db.createCollection('characters');

// 创建索引 - 使用显式名称
db.characters.createIndex({ characterId: 1 }, { unique: true, name: 'characterId_unique' });
db.characters.createIndex({ userId: 1 }, { name: 'userId_index' });
db.characters.createIndex({ name: 1 }, { unique: true, name: 'name_unique' });
db.characters.createIndex({ level: -1, exp: -1 }, { name: 'level_exp_index' });
db.characters.createIndex({ lastOnlineAt: -1 }, { name: 'lastOnlineAt_index' });

print('game_characters 数据库初始化完成');

// ==================== 初始化 game_social 数据库 ====================
print('初始化 game_social 数据库...');

db = db.getSiblingDB('game_social');

// 创建集合
db.createCollection('friends');
db.createCollection('mails');

// 创建索引 - 使用显式名称
db.friends.createIndex({ userId: 1 }, { name: 'userId_index' });
db.friends.createIndex({ friendId: 1 }, { name: 'friendId_index' });

db.mails.createIndex({ receiverId: 1 }, { name: 'receiverId_index' });
db.mails.createIndex({ senderId: 1 }, { name: 'senderId_index' });
db.mails.createIndex({ isRead: 1 }, { name: 'isRead_index' });

print('game_social 数据库初始化完成');

// ==================== 初始化 game_guilds 数据库 ====================
print('初始化 game_guilds 数据库...');

db = db.getSiblingDB('game_guilds');

// 创建 guilds 集合
db.createCollection('guilds');
db.createCollection('guildMembers');
db.createCollection('guildJoinRequests');
db.createCollection('guildMessages');
db.createCollection('guildActivities');
db.createCollection('guildAnnouncements');

// guilds 集合索引 - 使用显式名称
db.guilds.createIndex({ guildId: 1 }, { unique: true, name: 'guildId_unique' });
db.guilds.createIndex({ name: 1 }, { name: 'name_index' });
db.guilds.createIndex({ leaderId: 1 }, { name: 'leaderId_index' });
db.guilds.createIndex({ tag: 1 }, { name: 'tag_index' });

// guildMembers 集合索引
db.guildMembers.createIndex({ guildId: 1 }, { name: 'guildId_index' });
db.guildMembers.createIndex({ userId: 1 }, { unique: true, name: 'userId_unique' });

// guildJoinRequests 集合索引
db.guildJoinRequests.createIndex({ guildId: 1, status: 1 }, { name: 'guildId_status_index' });
db.guildJoinRequests.createIndex({ userId: 1 }, { name: 'userId_index' });
db.guildJoinRequests.createIndex({ guildId: 1, userId: 1 }, { name: 'guildId_userId_index' });

// guildMessages 集合索引
db.guildMessages.createIndex({ guildId: 1, timestamp: -1 }, { name: 'guildId_timestamp_index' });

// guildActivities 集合索引
db.guildActivities.createIndex({ guildId: 1, timestamp: -1 }, { name: 'guildId_timestamp_index' });

// guildAnnouncements 集合索引
db.guildAnnouncements.createIndex({ guildId: 1, createdAt: -1 }, { name: 'guildId_createdAt_index' });
db.guildAnnouncements.createIndex({ guildId: 1, isPinned: -1 }, { name: 'guildId_isPinned_index' });

print('game_guilds 数据库初始化完成');

// ==================== 初始化 game_battles 数据库 ====================
print('初始化 game_battles 数据库...');

db = db.getSiblingDB('game_battles');

// 创建集合
db.createCollection('battle_rooms');
db.createCollection('battle_records');

// 创建索引 - 使用显式名称
db.battle_rooms.createIndex({ battleId: 1 }, { unique: true, name: 'battleId_unique' });
db.battle_rooms.createIndex({ status: 1 }, { name: 'status_index' });
db.battle_rooms.createIndex({ createdAt: 1 }, { name: 'createdAt_index' });

db.battle_records.createIndex({ battleId: 1 }, { name: 'battleId_index' });
db.battle_records.createIndex({ playerId: 1 }, { name: 'playerId_index' });
db.battle_records.createIndex({ createdAt: 1 }, { name: 'createdAt_index' });

print('game_battles 数据库初始化完成');

// ==================== 初始化 game_leaderboards 数据库 ====================
print('初始化 game_leaderboards 数据库...');

db = db.getSiblingDB('game_leaderboards');

// 创建 leaderboards 集合
db.createCollection('leaderboards');

// 创建索引 - 使用显式名称
db.leaderboards.createIndex({ leaderboardType: 1, score: -1 }, { name: 'type_score_index' });
db.leaderboards.createIndex({ playerId: 1 }, { name: 'playerId_index' });
db.leaderboards.createIndex({ season: 1 }, { name: 'season_index' });

print('game_leaderboards 数据库初始化完成');

print('==================== MongoDB 初始化完成 ====================');
print('');
print('账户系统说明:');
print('  - 已启用密码验证（SHA256 + 固定盐）');
print('  - 请使用客户端 register 命令创建新账户');
print('  - 示例: register myuser mypassword myemail@example.com');
print('');
