# Docker 环境设置指南

## Windows 用户 Docker Desktop 安装

### 1. 下载和安装
1. 访问 [Docker Desktop for Windows](https://www.docker.com/products/docker-desktop/)
2. 下载并安装 Docker Desktop
3. 重启计算机（如果需要）

### 2. 验证安装
```bash
docker --version
docker-compose --version
```

### 3. 启动 GameApp 开发环境
```bash
# 进入 GameApp 目录
cd samples/GameApp

# 启动所有基础设施服务
docker-compose -f docker/docker-compose.yml up -d

# 查看服务状态
docker-compose -f docker/docker-compose.yml ps

# 查看日志
docker-compose -f docker/docker-compose.yml logs -f
```

### 4. 停止服务
```bash
docker-compose -f docker/docker-compose.yml down
```

## 服务访问地址

| 服务 | 地址 | 凭据 |
|------|------|------|
| MongoDB | `localhost:27017` | admin/dev_password |
| Redis | `localhost:6379` | 密码: dev_password |
| Consul | `localhost:8500` | 无需认证 |
| AuthServer | `localhost:8080` | - |
| GameServer TCP | `localhost:9000` | - |
| GameServer KCP | `localhost:9001` | - |
| BattleServer TCP | `localhost:8000` | - |
| BattleServer KCP | `localhost:8001` | - |

## 故障排除

### 端口冲突
如果遇到端口冲突，可以修改 `docker/docker-compose.yml` 中的端口映射。

### 权限问题
确保 Docker Desktop 以管理员权限运行。

### WSL2 相关问题
如果使用 WSL2，确保 WSL2 集成已启用。
