# DistributedGameApp 部署指南

## 快速开始

### 使用 Docker Compose（推荐）

```bash
# 1. 启动所有服务
cd deploy
docker-compose up -d

# 2. 查看服务状态
docker-compose ps

# 3. 查看 MongoDB 初始化日志
docker logs pulserpc-mongodb | grep "索引"

# 4. 访问服务
# - MongoDB: localhost:27017
# - Mongo Express: http://localhost:8081 (admin/admin123)
# - Consul: http://localhost:8500
```

### 验证索引

```bash
# Windows
.\scripts\mongodb-init.ps1 verify

# Linux/Mac
./scripts/mongodb-init.sh verify

# 或使用 Docker
docker exec -it pulserpc-mongodb mongosh < mongodb/verify-indexes.js
```

---

## 目录结构

```
deploy/
├── mongodb/
│   ├── init.js                 # MongoDB 索引初始化脚本
│   ├── verify-indexes.js       # 索引验证脚本
│   └── mongod.conf             # MongoDB 配置文件
├── scripts/
│   ├── mongodb-init.sh         # Linux/Mac 初始化脚本
│   └── mongodb-init.ps1        # Windows 初始化脚本
├── k8s/
│   └── mongodb-init-job.yaml   # Kubernetes 初始化 Job
├── docker-compose.yml          # Docker Compose 配置
└── README.md                   # 本文件
```

---

## 服务说明

### MongoDB (pulserpc-mongodb)

- **端口**: 27017
- **认证**: admin / password123
- **数据库**:
  - `game_guilds` - 公会数据（6 个集合）
  - `game_characters` - 角色数据
  - `game_data` - 游戏数据
- **索引**: 自动创建（通过 init.js）
- **TTL**: 消息 30 天、活动 90 天、会话 24 小时

### Consul (pulserpc-consul)

- **端口**: 8500 (HTTP), 8600 (DNS)
- **用途**: 服务发现、配置中心、分布式协调
- **Web UI**: http://localhost:8500

### Mongo Express (pulserpc-mongo-express)

- **端口**: 8081
- **认证**: admin / admin123
- **用途**: MongoDB Web 管理界面

---

## 索引管理

### 索引列表

#### game_guilds 数据库

**guilds 集合** (5 个索引):
- `idx_guild_id` - GuildId (唯一)
- `idx_guild_name` - Name (唯一)
- `idx_guild_tag` - Tag
- `idx_guild_leader` - LeaderId
- `idx_guild_level_exp` - Level + Exp (倒序)

**guild_members 集合** (4 个索引):
- `idx_member_userid` - UserId (唯一)
- `idx_member_guildid` - GuildId
- `idx_member_guild_role` - GuildId + Role
- `idx_member_contribution` - GuildId + Contribution (贡献度排行)

**guild_join_requests 集合** (3 个索引):
- `idx_request_guild_user` - GuildId + UserId
- `idx_request_guild_status` - GuildId + Status
- `idx_request_user_status` - UserId + Status

**guild_messages 集合** (2 个索引):
- `idx_message_guild_time` - GuildId + Timestamp (倒序)
- `idx_message_timestamp` - Timestamp (TTL: 30 天)

**guild_activities 集合** (3 个索引):
- `idx_activity_guild_time` - GuildId + Timestamp (倒序)
- `idx_activity_guild_type_time` - GuildId + ActivityType + Timestamp
- `idx_activity_timestamp` - Timestamp (TTL: 90 天)

**guild_announcements 集合** (2 个索引):
- `idx_announcement_guild_pin_time` - GuildId + IsPinned + CreatedAt
- `idx_announcement_id` - Id (唯一)

#### game_characters 数据库

**characters 集合** (7 个索引):
- `idx_character_id` - CharacterId (唯一)
- `idx_character_userid` - UserId
- `idx_character_name` - Name (唯一)
- `idx_character_level_exp` - Level + Exp (等级排行)
- `idx_character_lastonline` - LastOnlineAt (倒序)
- `idx_character_class_level` - Class + Level (职业排行)
- `idx_character_user_created` - UserId + CreatedAt

### 手动管理索引

```bash
# 创建索引
mongosh
> use game_guilds
> db.guilds.createIndex({ "GuildId": 1 }, { unique: true })

# 查看索引
> db.guilds.getIndexes()

# 删除索引
> db.guilds.dropIndex("idx_guild_id")

# 查看索引使用统计
> db.guilds.aggregate([{ $indexStats: {} }])
```

---

## 常见操作

### 重新初始化索引

```bash
# 方式 1: 删除数据卷重启（会清空所有数据！）
docker-compose down -v
docker-compose up -d

# 方式 2: 手动执行 init.js
docker exec -it pulserpc-mongodb mongosh < mongodb/init.js

# 方式 3: 使用脚本
.\scripts\mongodb-init.ps1 docker
```

### 备份和恢复

```bash
# 备份所有数据库
docker exec pulserpc-mongodb mongodump --out /backup
docker cp pulserpc-mongodb:/backup ./backup

# 恢复
docker cp ./backup pulserpc-mongodb:/backup
docker exec pulserpc-mongodb mongorestore /backup
```

### 查看慢查询

```bash
# 启用慢查询分析
mongosh
> db.setProfilingLevel(1, { slowms: 100 })

# 查看慢查询
> db.system.profile.find().sort({ ts: -1 }).limit(10)
```

### 性能监控

```bash
# 查看当前操作
mongosh
> db.currentOp()

# 查看服务器状态
> db.serverStatus()

# 查看数据库统计
> db.stats()
```

---

## Kubernetes 部署

### 使用 Job 初始化索引

```bash
# 1. 创建 ConfigMap
kubectl create configmap mongodb-init-scripts \
  --from-file=mongodb/init.js \
  --from-file=mongodb/verify-indexes.js

# 2. 部署 MongoDB
kubectl apply -f k8s/mongodb-statefulset.yaml

# 3. 运行初始化 Job
kubectl apply -f k8s/mongodb-init-job.yaml

# 4. 查看 Job 状态
kubectl get jobs
kubectl logs job/mongodb-index-init

# 5. 部署应用
kubectl apply -f k8s/backend-deployment.yaml
```

---

## 配置说明

### MongoDB 配置 (mongod.conf)

```yaml
# 关键配置项
net:
  port: 27017
  maxIncomingConnections: 1000

storage:
  engine: wiredTiger
  wiredTiger:
    engineConfig:
      cacheSizeGB: 1  # 调整缓存大小

operationProfiling:
  mode: slowOp
  slowOpThresholdMs: 100  # 慢查询阈值
```

### Docker Compose 环境变量

```yaml
services:
  mongodb:
    environment:
      # 修改默认密码
      MONGO_INITDB_ROOT_USERNAME: admin
      MONGO_INITDB_ROOT_PASSWORD: your_secure_password
```

---

## 故障排查

### 问题 1: 索引未创建

**检查**:
```bash
# 查看容器日志
docker logs pulserpc-mongodb

# 手动执行脚本
docker exec -it pulserpc-mongodb mongosh < mongodb/init.js
```

### 问题 2: 连接被拒绝

**检查**:
```bash
# 检查容器状态
docker-compose ps

# 检查网络
docker network inspect deploy_pulserpc-network

# 测试连接
mongosh mongodb://admin:password123@localhost:27017
```

### 问题 3: 性能问题

**检查**:
```bash
# 查看索引使用情况
mongosh
> db.collection.explain("executionStats").find({ ... })

# 查看慢查询
> db.system.profile.find().sort({ ts: -1 })
```

---

## 生产环境建议

### 安全

- ✅ 修改默认密码
- ✅ 启用 SSL/TLS
- ✅ 限制网络访问（bindIp）
- ✅ 使用 Secrets 管理敏感信息

### 性能

- ✅ 调整 cacheSizeGB（内存的 50%）
- ✅ 使用 SSD 存储
- ✅ 启用慢查询分析
- ✅ 定期检查索引使用情况

### 可用性

- ✅ 使用副本集（Replica Set）
- ✅ 定期备份
- ✅ 监控和告警
- ✅ 灾备方案

### 示例：副本集配置

```yaml
# docker-compose-replica.yml
services:
  mongodb-primary:
    command: mongod --replSet pulserpc-rs --bind_ip_all

  mongodb-secondary:
    command: mongod --replSet pulserpc-rs --bind_ip_all

  mongodb-arbiter:
    command: mongod --replSet pulserpc-rs --bind_ip_all
```

---

## 相关文档

- [MongoDB 初始化脚本](mongodb/init.js)
- [MongoDB 索引验证脚本](mongodb/verify-indexes.js)
- [PowerShell 初始化脚本](scripts/mongodb-init.ps1)
- [Shell 初始化脚本](scripts/mongodb-init.sh)

---

## 获取帮助

```bash
# PowerShell 脚本帮助
.\scripts\mongodb-init.ps1 help

# Shell 脚本帮助
./scripts/mongodb-init.sh help

# MongoDB 文档
mongosh --help
```
