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

// 创建索引
db.accounts.createIndex({ userId: 1 }, { unique: true });
db.accounts.createIndex({ email: 1 }, { unique: true, sparse: true });
db.accounts.createIndex({ provider: 1, providerUserId: 1 });

// 插入测试账号数据
print('插入测试账号...');

const testAccounts = [
  {
    userId: 'testuser',
    username: 'TestUser',
    email: 'testuser@example.com',
    provider: 'local',
    providerUserId: '',
    status: { Code: 0, Name: 'Normal' },
    createdAt: new Date(),
    lastLoginAt: new Date(),
    lastLoginIp: '127.0.0.1'
  },
  {
    userId: 'player1',
    username: 'Player1',
    email: 'player1@example.com',
    provider: 'local',
    providerUserId: '',
    status: { Code: 0, Name: 'Normal' },
    createdAt: new Date(),
    lastLoginAt: new Date(),
    lastLoginIp: '127.0.0.1'
  },
  {
    userId: 'player2',
    username: 'Player2',
    email: 'player2@example.com',
    provider: 'local',
    providerUserId: '',
    status: { Code: 0, Name: 'Normal' },
    createdAt: new Date(),
    lastLoginAt: new Date(),
    lastLoginIp: '127.0.0.1'
  },
  {
    userId: 'admin',
    username: 'Admin',
    email: 'admin@example.com',
    provider: 'local',
    providerUserId: '',
    status: { Code: 0, Name: 'Normal' },
    createdAt: new Date(),
    lastLoginAt: new Date(),
    lastLoginIp: '127.0.0.1'
  }
];

db.accounts.insertMany(testAccounts);
print('已插入 ' + testAccounts.length + ' 个测试账号');

// ==================== 初始化 game_characters 数据库 ====================
print('初始化 game_characters 数据库...');

db = db.getSiblingDB('game_characters');

// 创建 characters 集合
db.createCollection('characters');

// 创建索引
db.characters.createIndex({ characterId: 1 }, { unique: true });
db.characters.createIndex({ userId: 1 });
db.characters.createIndex({ name: 1 });

print('game_characters 数据库初始化完成');

// ==================== 初始化 game_social 数据库 ====================
print('初始化 game_social 数据库...');

db = db.getSiblingDB('game_social');

// 创建集合
db.createCollection('friends');
db.createCollection('mails');

// 创建索引
db.friends.createIndex({ userId: 1 });
db.friends.createIndex({ friendId: 1 });

db.mails.createIndex({ receiverId: 1 });
db.mails.createIndex({ senderId: 1 });
db.mails.createIndex({ isRead: 1 });

print('game_social 数据库初始化完成');

// ==================== 初始化 game_guilds 数据库 ====================
print('初始化 game_guilds 数据库...');

db = db.getSiblingDB('game_guilds');

// 创建 guilds 集合
db.createCollection('guilds');

// 创建索引
db.guilds.createIndex({ guildId: 1 }, { unique: true });
db.guilds.createIndex({ name: 1 });
db.guilds.createIndex({ leaderId: 1 });

print('game_guilds 数据库初始化完成');

// ==================== 初始化 game_battles 数据库 ====================
print('初始化 game_battles 数据库...');

db = db.getSiblingDB('game_battles');

// 创建集合
db.createCollection('battle_rooms');
db.createCollection('battle_records');

// 创建索引
db.battle_rooms.createIndex({ battleId: 1 }, { unique: true });
db.battle_rooms.createIndex({ status: 1 });
db.battle_rooms.createIndex({ createdAt: 1 });

db.battle_records.createIndex({ battleId: 1 });
db.battle_records.createIndex({ playerId: 1 });
db.battle_records.createIndex({ createdAt: 1 });

print('game_battles 数据库初始化完成');

// ==================== 初始化 game_leaderboards 数据库 ====================
print('初始化 game_leaderboards 数据库...');

db = db.getSiblingDB('game_leaderboards');

// 创建 leaderboards 集合
db.createCollection('leaderboards');

// 创建索引
db.leaderboards.createIndex({ leaderboardType: 1, score: -1 });
db.leaderboards.createIndex({ playerId: 1 });
db.leaderboards.createIndex({ season: 1 });

print('game_leaderboards 数据库初始化完成');

print('==================== MongoDB 初始化完成 ====================');
print('');
print('测试账号列表:');
print('  - testuser (TestUser) - testuser@example.com');
print('  - player1  (Player1)  - player1@example.com');
print('  - player2  (Player2)  - player2@example.com');
print('  - admin    (Admin)    - admin@example.com');
print('');
print('注意: 当前登录实现未验证密码，任意密码均可登录');
print('');
