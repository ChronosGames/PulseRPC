// MongoDB 初始化脚本 - 开发环境
// 这个脚本会在 MongoDB 容器启动时自动执行

// 切换到 gameapp_dev 数据库
db = db.getSiblingDB('gameapp_dev');

// 创建用户管理员
db.createUser({
  user: 'gameapp_admin',
  pwd: 'gameapp_admin_password',
  roles: [
    { role: 'readWrite', db: 'gameapp_dev' },
    { role: 'dbAdmin', db: 'gameapp_dev' }, // For creating indexes, collections
    { role: 'clusterMonitor', db: 'admin' }  // For listDatabases
  ]
});

// 创建基础集合
db.createCollection('users');
db.createCollection('players');
db.createCollection('worlds');
db.createCollection('battles');
db.createCollection('items');
db.createCollection('guilds');
db.createCollection('logs');

// 创建索引
// 用户集合索引
db.users.createIndex({ "userId": 1 }, { unique: true });
db.users.createIndex({ "username": 1 }, { unique: true });
db.users.createIndex({ "email": 1 }, { unique: true });
db.users.createIndex({ "status": 1 });
db.users.createIndex({ "lastLoginTime": -1 });

// 玩家集合索引
db.players.createIndex({ "playerId": 1 }, { unique: true });
db.players.createIndex({ "userId": 1, "zoneId": 1 }, { unique: true });
db.players.createIndex({ "characterName": 1, "zoneId": 1 }, { unique: true });
db.players.createIndex({ "level": -1 });
db.players.createIndex({ "position.worldId": 1 });
db.players.createIndex({ "social.guildId": 1 });
db.players.createIndex({ "statistics.lastActiveTime": -1 });

// 世界集合索引
db.worlds.createIndex({ "worldId": 1 }, { unique: true });
db.worlds.createIndex({ "status": 1 });

// 战斗集合索引
db.battles.createIndex({ "battleId": 1 }, { unique: true });
db.battles.createIndex({ "status": 1 });
db.battles.createIndex({ "participants.playerId": 1 });
db.battles.createIndex({ "startTime": -1 });

// 道具集合索引
db.items.createIndex({ "itemId": 1 }, { unique: true });
db.items.createIndex({ "type": 1 });
db.items.createIndex({ "rarity": 1 });

// 公会集合索引
db.guilds.createIndex({ "guildId": 1 }, { unique: true });
db.guilds.createIndex({ "name": 1 }, { unique: true });
db.guilds.createIndex({ "members.playerId": 1 });
db.guilds.createIndex({ "level": -1 });

// 日志集合索引
db.logs.createIndex({ "timestamp": -1 });
db.logs.createIndex({ "type": 1 });
db.logs.createIndex({ "level": 1 });
db.logs.createIndex({ "context.userId": 1 });

// 插入初始数据
// 插入区服配置数据
db.zones = db.getSiblingDB('gameapp_dev').zones;
db.zones.insertMany([
  {
    zoneId: "zone001",
    name: "华夏一区",
    status: "online",
    maxPlayers: 5000,
    currentPlayers: 0,
    loadLevel: "low",
    recommendation: true,
    newPlayerAllowed: true,
    description: "推荐新手服务器",
    features: ["pvp", "guild_war", "world_boss"],
    servers: {
      authServer: "http://authserver-dev:8080",
      gameServer: "gameserver-dev:7000",
      battleServer: "battleserver-dev:7000"
    },
    createdAt: new Date(),
    updatedAt: new Date()
  },
  {
    zoneId: "zone002",
    name: "华夏二区",
    status: "online",
    maxPlayers: 5000,
    currentPlayers: 0,
    loadLevel: "low",
    recommendation: false,
    newPlayerAllowed: true,
    description: "常规服务器",
    features: ["pvp", "guild_war"],
    servers: {
      authServer: "http://authserver-dev:8080",
      gameServer: "gameserver-dev:7000",
      battleServer: "battleserver-dev:7000"
    },
    createdAt: new Date(),
    updatedAt: new Date()
  }
]);

// 插入基础道具数据
db.items.insertMany([
  {
    itemId: "sword_001",
    name: "新手之剑",
    description: "适合新手使用的基础武器",
    type: "weapon",
    subType: "sword",
    rarity: "common",
    level: 1,
    attributes: {
      attack: 50,
      defense: 0,
      durability: 100,
      weight: 2.5
    },
    requirements: {
      level: 1,
      class: ["warrior"],
      strength: 10
    },
    trading: {
      tradeable: true,
      vendorPrice: 100,
      stackSize: 1
    },
    icon: "icons/sword_001.png",
    createdAt: new Date(),
    updatedAt: new Date()
  },
  {
    itemId: "potion_001",
    name: "小型生命药水",
    description: "恢复少量生命值的药水",
    type: "consumable",
    subType: "potion",
    rarity: "common",
    level: 1,
    attributes: {
      healAmount: 100
    },
    effects: [
      {
        type: "heal",
        value: 100,
        duration: 0
      }
    ],
    trading: {
      tradeable: true,
      vendorPrice: 10,
      stackSize: 50
    },
    icon: "icons/potion_001.png",
    createdAt: new Date(),
    updatedAt: new Date()
  }
]);

// 插入基础世界数据
db.worlds.insertOne({
  worldId: "world001",
  name: "新手村",
  description: "适合新手玩家的安全区域",
  type: "town",
  maxPlayers: 1000,
  currentPlayers: 0,
  level: {
    min: 1,
    max: 10
  },
  maps: [
    {
      mapId: "map_001",
      name: "中央广场",
      size: {
        width: 2000,
        height: 2000
      },
      spawnPoints: [
        { x: 1000, y: 0, z: 1000 },
        { x: 1200, y: 0, z: 800 }
      ]
    }
  ],
  status: "active",
  createdAt: new Date(),
  updatedAt: new Date()
});

print("GameApp MongoDB 初始化完成!");
print("数据库: gameapp_dev");
print("集合数量: " + db.getCollectionNames().length);
print("用户: gameapp_admin");
