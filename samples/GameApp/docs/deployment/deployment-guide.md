# GameApp 部署指南

## 概述

本文档详细介绍了 GameApp 在不同环境下的部署方法，包括开发环境、测试环境和生产环境的完整部署流程。

## 系统要求

### 最低硬件要求

#### 开发环境
- **CPU**: 4 核心，2.5GHz+
- **内存**: 8GB RAM
- **存储**: 50GB 可用空间
- **网络**: 宽带网络连接

#### 生产环境（单节点）
- **CPU**: 8 核心，3.0GHz+
- **内存**: 16GB RAM
- **存储**: 200GB 可用空间（SSD推荐）
- **网络**: 千兆网络，低延迟

#### 生产环境（集群）
- **负载均衡节点**: 2核4GB，高可用部署
- **应用服务节点**: 4核8GB，多实例部署
- **数据库节点**: 8核16GB，主从配置
- **缓存节点**: 4核8GB，集群配置

### 软件依赖

#### 基础环境
- **操作系统**: Linux (Ubuntu 20.04+/CentOS 8+) / Windows Server 2019+ / macOS 12+
- **.NET Runtime**: .NET 9.0 Runtime
- **Docker**: 24.0+ (推荐)
- **Docker Compose**: 2.0+ (开发环境)

#### 数据库和中间件
- **MongoDB**: 7.0+
- **Redis**: 7.0+
- **Consul**: 1.15+
- **Nginx**: 1.24+ (生产环境)

## 环境准备

### 1. 安装 .NET 9 Runtime

#### Linux (Ubuntu/Debian)
```bash
# 添加 Microsoft 包源
wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

# 安装 .NET 9 Runtime
sudo apt-get update
sudo apt-get install -y dotnet-runtime-9.0
```

#### Windows
```powershell
# 下载并安装 .NET 9 Runtime
# 访问: https://dotnet.microsoft.com/download/dotnet/9.0
# 下载 ASP.NET Core Runtime 9.0.x (x64)
```

#### macOS
```bash
# 使用 Homebrew
brew install --cask dotnet

# 或下载安装包
# 访问: https://dotnet.microsoft.com/download/dotnet/9.0
```

### 2. 安装 Docker 和 Docker Compose

#### Linux
```bash
# 安装 Docker
curl -fsSL https://get.docker.com -o get-docker.sh
sudo sh get-docker.sh

# 安装 Docker Compose
sudo curl -L "https://github.com/docker/compose/releases/download/v2.21.0/docker-compose-$(uname -s)-$(uname -m)" -o /usr/local/bin/docker-compose
sudo chmod +x /usr/local/bin/docker-compose

# 启动 Docker 服务
sudo systemctl start docker
sudo systemctl enable docker

# 添加用户到 docker 组
sudo usermod -aG docker $USER
```

#### Windows
```powershell
# 安装 Docker Desktop
# 访问: https://www.docker.com/products/docker-desktop
# 下载并安装 Docker Desktop for Windows
```

#### macOS
```bash
# 安装 Docker Desktop
# 访问: https://www.docker.com/products/docker-desktop
# 下载并安装 Docker Desktop for Mac

# 或使用 Homebrew
brew install --cask docker
```

## 开发环境部署

### 1. 获取源代码

```bash
# 克隆仓库
git clone <repository-url>
cd GameApp

# 切换到最新稳定分支
git checkout main
```

### 2. 构建应用

```bash
# 恢复依赖包
dotnet restore

# 构建整个解决方案
dotnet build

# 运行测试（可选）
dotnet test
```

### 3. 启动基础设施服务

```bash
# 启动 MongoDB、Redis、Consul
cd docker
docker-compose up -d mongodb-dev redis-dev consul-dev

# 等待服务启动完成
sleep 30

# 验证服务状态
docker-compose ps
```

### 4. 启动应用服务

#### 方式1: Docker Compose（推荐）
```bash
# 启动所有服务
docker-compose up -d

# 查看日志
docker-compose logs -f authserver-dev
docker-compose logs -f gameserver-dev
docker-compose logs -f battleserver-dev
```

#### 方式2: 直接运行
```bash
# Terminal 1: 启动 AuthServer
cd src/GameApp.AuthServer
dotnet run

# Terminal 2: 启动 GameServer
cd src/GameApp.GameServer
dotnet run

# Terminal 3: 启动 BattleServer
cd src/GameApp.BattleServer
dotnet run
```

### 5. 验证部署

```bash
# 检查 AuthServer
curl http://localhost:5000/api/auth/health

# 检查服务发现
curl http://localhost:8500/v1/agent/services

# 查看监控数据
curl http://localhost:5000/api/monitoring/health
```

## 测试环境部署

### 1. 环境配置

```bash
# 创建测试环境配置目录
mkdir -p /opt/gameapp/test
cd /opt/gameapp/test

# 创建环境变量文件
cat > .env.test << EOF
ASPNETCORE_ENVIRONMENT=Testing
ConnectionStrings__MongoDB=mongodb://admin:test_password@mongodb-test:27017/gameapp_test?authSource=admin
ConnectionStrings__Redis=redis-test:6379
Redis__Password=test_password
JWT_SECRET_KEY=test_secret_key_change_in_production_environment
Consul__Host=consul-test
Consul__Port=8500
EOF
```

### 2. Docker Compose 配置

```yaml
# docker-compose.test.yml
version: '3.8'

services:
  mongodb-test:
    image: mongo:7.0
    container_name: gameapp-mongodb-test
    environment:
      MONGO_INITDB_ROOT_USERNAME: admin
      MONGO_INITDB_ROOT_PASSWORD: test_password
      MONGO_INITDB_DATABASE: gameapp_test
    volumes:
      - mongodb_test_data:/data/db
    networks:
      - gameapp-test
    restart: unless-stopped

  redis-test:
    image: redis:7.0-alpine
    container_name: gameapp-redis-test
    command: redis-server --requirepass test_password
    volumes:
      - redis_test_data:/data
    networks:
      - gameapp-test
    restart: unless-stopped

  consul-test:
    image: hashicorp/consul:1.15
    container_name: gameapp-consul-test
    command: agent -server -ui -node=server-1 -bootstrap-expect=1 -client=0.0.0.0
    volumes:
      - consul_test_data:/consul/data
    networks:
      - gameapp-test
    restart: unless-stopped

  authserver-test:
    image: gameapp/authserver:latest
    container_name: gameapp-authserver-test
    ports:
      - "5000:5000"
    env_file:
      - .env.test
    depends_on:
      - mongodb-test
      - redis-test
      - consul-test
    networks:
      - gameapp-test
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:5000/api/auth/health"]
      interval: 30s
      timeout: 10s
      retries: 3

volumes:
  mongodb_test_data:
  redis_test_data:
  consul_test_data:

networks:
  gameapp-test:
    driver: bridge
```

### 3. 部署测试环境

```bash
# 构建镜像
docker-compose -f docker-compose.test.yml build

# 启动服务
docker-compose -f docker-compose.test.yml up -d

# 等待服务健康检查通过
docker-compose -f docker-compose.test.yml ps

# 运行集成测试
docker run --network gameapp-test gameapp/tests:latest
```

## 生产环境部署

### 1. 服务器准备

#### 单机部署

```bash
# 创建应用目录
sudo mkdir -p /opt/gameapp/production
sudo chown -R $USER:$USER /opt/gameapp

# 创建数据目录
sudo mkdir -p /data/mongodb /data/redis /data/logs
sudo chown -R 1001:1001 /data/mongodb
sudo chown -R 999:999 /data/redis
```

#### 集群部署

```bash
# 节点规划
# Node1: 192.168.1.10 (MongoDB Primary, Redis Master, AuthServer)
# Node2: 192.168.1.11 (MongoDB Secondary, Redis Slave, GameServer)
# Node3: 192.168.1.12 (MongoDB Secondary, Consul, BattleServer)
# LB:    192.168.1.100 (Nginx Load Balancer)

# 在每个节点创建目录结构
for node in 192.168.1.10 192.168.1.11 192.168.1.12; do
  ssh user@$node "sudo mkdir -p /opt/gameapp/production /data/{mongodb,redis,logs}"
done
```

### 2. 安全配置

```bash
# 生成安全密钥
JWT_SECRET=$(openssl rand -base64 64)
MONGODB_PASSWORD=$(openssl rand -base64 32)
REDIS_PASSWORD=$(openssl rand -base64 32)

# 创建生产环境配置
cat > /opt/gameapp/production/.env.prod << EOF
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__MongoDB=mongodb://admin:${MONGODB_PASSWORD}@mongodb-prod:27017/gameapp_prod?authSource=admin
ConnectionStrings__Redis=redis-prod:6379
Redis__Password=${REDIS_PASSWORD}
JWT_SECRET_KEY=${JWT_SECRET}
Consul__Host=consul-prod
Consul__Port=8500
ASPNETCORE_URLS=http://0.0.0.0:5000
EOF

# 设置文件权限
chmod 600 /opt/gameapp/production/.env.prod
```

### 3. SSL/TLS 证书配置

```bash
# 使用 Let's Encrypt 生成证书
sudo apt-get install certbot

# 生成证书（替换为实际域名）
sudo certbot certonly --standalone -d api.gameapp.com -d game.gameapp.com

# 创建证书目录
mkdir -p /opt/gameapp/production/certs

# 复制证书文件
sudo cp /etc/letsencrypt/live/api.gameapp.com/fullchain.pem /opt/gameapp/production/certs/
sudo cp /etc/letsencrypt/live/api.gameapp.com/privkey.pem /opt/gameapp/production/certs/
sudo chown $USER:$USER /opt/gameapp/production/certs/*
```

### 4. 生产环境 Docker Compose

```yaml
# docker-compose.prod.yml
version: '3.8'

services:
  nginx:
    image: nginx:1.24-alpine
    container_name: gameapp-nginx
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./nginx.conf:/etc/nginx/nginx.conf:ro
      - ./certs:/etc/nginx/certs:ro
    depends_on:
      - authserver-prod
    networks:
      - gameapp-prod
    restart: unless-stopped

  mongodb-prod:
    image: mongo:7.0
    container_name: gameapp-mongodb-prod
    environment:
      MONGO_INITDB_ROOT_USERNAME: admin
      MONGO_INITDB_ROOT_PASSWORD: ${MONGODB_PASSWORD}
      MONGO_INITDB_DATABASE: gameapp_prod
    volumes:
      - /data/mongodb:/data/db
      - ./mongodb.conf:/etc/mongod.conf:ro
    command: mongod --config /etc/mongod.conf
    networks:
      - gameapp-prod
    restart: unless-stopped
    logging:
      driver: "json-file"
      options:
        max-size: "100m"
        max-file: "5"

  redis-prod:
    image: redis:7.0-alpine
    container_name: gameapp-redis-prod
    command: redis-server --requirepass ${REDIS_PASSWORD} --appendonly yes
    volumes:
      - /data/redis:/data
    networks:
      - gameapp-prod
    restart: unless-stopped
    logging:
      driver: "json-file"
      options:
        max-size: "100m"
        max-file: "5"

  authserver-prod:
    image: gameapp/authserver:v1.0.0
    container_name: gameapp-authserver-prod
    env_file:
      - .env.prod
    volumes:
      - /data/logs:/app/logs
    depends_on:
      - mongodb-prod
      - redis-prod
    networks:
      - gameapp-prod
    restart: unless-stopped
    deploy:
      resources:
        limits:
          memory: 1G
          cpus: '1.0'
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:5000/api/auth/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 60s
    logging:
      driver: "json-file"
      options:
        max-size: "100m"
        max-file: "5"

networks:
  gameapp-prod:
    driver: bridge

volumes:
  mongodb_prod_data:
    driver: local
  redis_prod_data:
    driver: local
```

### 5. Nginx 负载均衡配置

```nginx
# nginx.conf
user nginx;
worker_processes auto;
error_log /var/log/nginx/error.log;
pid /run/nginx.pid;

events {
    worker_connections 1024;
    use epoll;
    multi_accept on;
}

http {
    log_format main '$remote_addr - $remote_user [$time_local] "$request" '
                    '$status $body_bytes_sent "$http_referer" '
                    '"$http_user_agent" "$http_x_forwarded_for"';

    access_log /var/log/nginx/access.log main;

    sendfile on;
    tcp_nopush on;
    tcp_nodelay on;
    keepalive_timeout 65;
    types_hash_max_size 2048;

    include /etc/nginx/mime.types;
    default_type application/octet-stream;

    # Gzip compression
    gzip on;
    gzip_vary on;
    gzip_min_length 1024;
    gzip_types text/plain text/css application/json application/javascript text/xml application/xml application/xml+rss text/javascript;

    # Rate limiting
    limit_req_zone $binary_remote_addr zone=auth_limit:10m rate=10r/s;
    limit_req_zone $binary_remote_addr zone=api_limit:10m rate=100r/s;

    # Upstream servers
    upstream authserver {
        least_conn;
        server authserver-prod:5000 max_fails=3 fail_timeout=30s;
        # server authserver-prod-2:5000 max_fails=3 fail_timeout=30s;
    }

    # HTTP to HTTPS redirect
    server {
        listen 80;
        server_name api.gameapp.com;
        return 301 https://$server_name$request_uri;
    }

    # HTTPS server
    server {
        listen 443 ssl http2;
        server_name api.gameapp.com;

        ssl_certificate /etc/nginx/certs/fullchain.pem;
        ssl_certificate_key /etc/nginx/certs/privkey.pem;
        ssl_session_cache shared:SSL:1m;
        ssl_session_timeout 10m;
        ssl_protocols TLSv1.2 TLSv1.3;
        ssl_ciphers ECDHE-RSA-AES128-GCM-SHA256:ECDHE-RSA-AES256-GCM-SHA384;
        ssl_prefer_server_ciphers on;

        # Security headers
        add_header X-Frame-Options DENY;
        add_header X-Content-Type-Options nosniff;
        add_header X-XSS-Protection "1; mode=block";
        add_header Strict-Transport-Security "max-age=31536000; includeSubDomains";

        # API routes
        location /api/auth/ {
            limit_req zone=auth_limit burst=20 nodelay;
            proxy_pass http://authserver;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
            proxy_connect_timeout 30s;
            proxy_send_timeout 30s;
            proxy_read_timeout 30s;
        }

        location /api/ {
            limit_req zone=api_limit burst=200 nodelay;
            proxy_pass http://authserver;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
            proxy_connect_timeout 30s;
            proxy_send_timeout 30s;
            proxy_read_timeout 30s;
        }

        # Health check
        location /health {
            access_log off;
            return 200 "healthy\n";
            add_header Content-Type text/plain;
        }
    }
}
```

### 6. 数据库配置

#### MongoDB 生产配置

```yaml
# mongodb.conf
storage:
  dbPath: /data/db
  journal:
    enabled: true

systemLog:
  destination: file
  path: /data/logs/mongod.log
  logAppend: true
  logRotate: reopen

net:
  port: 27017
  bindIp: 0.0.0.0

security:
  authorization: enabled

replication:
  replSetName: "gameapp-replica"

operationProfiling:
  slowOpThresholdMs: 100
  mode: slowOp
```

#### Redis 生产配置

```conf
# redis.conf
bind 0.0.0.0
port 6379
requirepass your_redis_password

# Persistence
save 900 1
save 300 10
save 60 10000

appendonly yes
appendfsync everysec

# Memory management
maxmemory 1gb
maxmemory-policy allkeys-lru

# Security
rename-command FLUSHDB ""
rename-command FLUSHALL ""
rename-command CONFIG ""
```

### 7. 部署执行

```bash
# 切换到生产目录
cd /opt/gameapp/production

# 拉取最新镜像
docker-compose -f docker-compose.prod.yml pull

# 启动服务
docker-compose -f docker-compose.prod.yml up -d

# 等待服务启动
sleep 60

# 验证服务状态
docker-compose -f docker-compose.prod.yml ps
docker-compose -f docker-compose.prod.yml logs authserver-prod

# 健康检查
curl -k https://api.gameapp.com/api/auth/health
curl -k https://api.gameapp.com/api/monitoring/health
```

## 监控和日志

### 1. 服务监控

```bash
# 安装 Prometheus
docker run -d \
  --name prometheus \
  --network gameapp-prod \
  -p 9090:9090 \
  -v ./prometheus.yml:/etc/prometheus/prometheus.yml \
  prom/prometheus

# 安装 Grafana
docker run -d \
  --name grafana \
  --network gameapp-prod \
  -p 3000:3000 \
  -v grafana-storage:/var/lib/grafana \
  grafana/grafana
```

### 2. 日志聚合

```bash
# 安装 ELK Stack
docker-compose -f elk-stack.yml up -d

# 配置 Filebeat 收集应用日志
docker run -d \
  --name filebeat \
  --user root \
  --volume /data/logs:/usr/share/filebeat/logs:ro \
  --volume ./filebeat.yml:/usr/share/filebeat/filebeat.yml:ro \
  docker.elastic.co/beats/filebeat:8.8.0
```

## 备份和恢复

### 1. 数据库备份

```bash
#!/bin/bash
# backup.sh

# MongoDB 备份
docker exec gameapp-mongodb-prod mongodump \
  --username admin \
  --password ${MONGODB_PASSWORD} \
  --authenticationDatabase admin \
  --out /backup/mongodb/$(date +%Y%m%d_%H%M%S)

# Redis 备份
docker exec gameapp-redis-prod redis-cli \
  --no-auth-warning -a ${REDIS_PASSWORD} \
  BGSAVE

# 压缩并上传到云存储
tar -czf backup_$(date +%Y%m%d_%H%M%S).tar.gz /backup/
aws s3 cp backup_$(date +%Y%m%d_%H%M%S).tar.gz s3://gameapp-backups/
```

### 2. 自动化备份

```cron
# 添加到 crontab
# 每天凌晨 2 点执行备份
0 2 * * * /opt/gameapp/production/backup.sh

# 每周日执行完整备份
0 1 * * 0 /opt/gameapp/production/full-backup.sh
```

## 故障排除

### 常见问题

#### 1. 服务启动失败

```bash
# 检查容器状态
docker-compose ps

# 查看容器日志
docker-compose logs <service_name>

# 检查资源使用
docker stats

# 验证网络连接
docker network ls
docker network inspect gameapp-prod
```

#### 2. 数据库连接问题

```bash
# 测试 MongoDB 连接
docker exec -it gameapp-mongodb-prod mongo \
  --username admin \
  --password ${MONGODB_PASSWORD} \
  --authenticationDatabase admin

# 测试 Redis 连接
docker exec -it gameapp-redis-prod redis-cli \
  -a ${REDIS_PASSWORD} ping
```

#### 3. 性能问题

```bash
# 检查系统资源
top
htop
iostat -x 1
sar -u 1

# 检查应用性能
curl https://api.gameapp.com/api/monitoring/dashboard
curl https://api.gameapp.com/api/monitoring/metrics/trends?hours=1
```

### 应急处理

#### 1. 服务重启

```bash
# 重启单个服务
docker-compose restart authserver-prod

# 重启所有服务
docker-compose down
docker-compose up -d
```

#### 2. 数据恢复

```bash
# 从备份恢复 MongoDB
docker exec -i gameapp-mongodb-prod mongorestore \
  --username admin \
  --password ${MONGODB_PASSWORD} \
  --authenticationDatabase admin \
  --drop \
  /backup/mongodb/20240101_020000/
```

#### 3. 流量切换

```bash
# 通过 Nginx 配置将流量切换到备用服务器
# 修改 upstream 配置，重载 Nginx
docker exec gameapp-nginx nginx -s reload
```

## 最佳实践

### 1. 安全建议

- 定期更新系统和依赖包
- 使用强密码和密钥轮换
- 启用防火墙和访问控制
- 定期安全审计和漏洞扫描
- 使用 HTTPS 和证书自动更新

### 2. 性能优化

- 合理配置 JVM 参数
- 启用应用缓存和数据库连接池
- 使用 CDN 加速静态资源
- 配置数据库索引和查询优化
- 监控和调优垃圾回收

### 3. 运维自动化

- 使用 Infrastructure as Code
- 实现自动化部署和回滚
- 设置监控告警和自动化响应
- 定期备份和恢复测试
- 文档化运维流程和应急预案

## 总结

本部署指南涵盖了 GameApp 从开发环境到生产环境的完整部署流程。通过遵循本指南，您可以在各种环境中成功部署和运行 GameApp 系统。在生产环境中，请特别注意安全性、可靠性和性能监控，并建立完备的备份和应急处理机制。
