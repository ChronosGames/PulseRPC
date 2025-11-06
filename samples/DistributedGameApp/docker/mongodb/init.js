// MongoDB 初始化脚本
// 创建所有需要的数据库和集合

// 切换到 admin 数据库
db = db.getSiblingDB('admin');

// 创建游戏数据库
const databases = [
  'game_accounts',
  'game_characters',
  'game_social',
  'game_guilds',
  'game_battles'
];

databases.forEach(dbName => {
  const gameDb = db.getSiblingDB(dbName);
  print(`Creating database: ${dbName}`);

  // 创建一个空集合以确保数据库被创建
  gameDb.createCollection('_init');
});

// game_accounts 数据库
db = db.getSiblingDB('game_accounts');
db.createCollection('accounts');
db.accounts.createIndex({ userId: 1 }, { unique: true });
db.accounts.createIndex({ email: 1 }, { unique: true });
db.accounts.createIndex({ provider: 1, providerUserId: 1 }, { unique: true });

// game_characters 数据库
db = db.getSiblingDB('game_characters');
db.createCollection('characters');
db.characters.createIndex({ characterId: 1 }, { unique: true });
db.characters.createIndex({ userId: 1 });
db.characters.createIndex({ name: 1 });

// game_social 数据库
db = db.getSiblingDB('game_social');
db.createCollection('friends');
db.friends.createIndex({ userId: 1, friendUserId: 1 }, { unique: true });
db.createCollection('chat_messages');
db.chat_messages.createIndex({ channelType: 1, channelId: 1, sentAt: -1 });

// game_guilds 数据库
db = db.getSiblingDB('game_guilds');
db.createCollection('guilds');
db.guilds.createIndex({ guildId: 1 }, { unique: true });
db.guilds.createIndex({ name: 1 }, { unique: true });
db.createCollection('guild_members');
db.guild_members.createIndex({ guildId: 1, userId: 1 }, { unique: true });
db.guild_members.createIndex({ userId: 1 });

// game_battles 数据库
db = db.getSiblingDB('game_battles');
db.createCollection('battle_records');
db.battle_records.createIndex({ roomId: 1 });
db.battle_records.createIndex({ 'players.playerId': 1 });
db.battle_records.createIndex({ finishedAt: -1 });

// game_leaderboards 数据库
db = db.getSiblingDB('game_leaderboards');
db.createCollection('leaderboards');
db.leaderboards.createIndex({ leaderboardType: 1, seasonId: 1, rank: 1 });
db.leaderboards.createIndex({ leaderboardType: 1, seasonId: 1, entityId: 1 }, { unique: true });

print('MongoDB initialization completed!');
