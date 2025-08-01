#!/bin/bash

# GameApp 开发环境启动脚本

set -e

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# 项目根目录
PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOCKER_DIR="$PROJECT_ROOT/docker"

echo -e "${BLUE}=== GameApp 开发环境启动 ===${NC}"
echo "项目根目录: $PROJECT_ROOT"

# 检查 Docker 是否运行
if ! docker info > /dev/null 2>&1; then
    echo -e "${RED}错误: Docker 未运行，请先启动 Docker${NC}"
    exit 1
fi

# 检查 docker-compose 是否存在
if ! command -v docker-compose &> /dev/null; then
    echo -e "${RED}错误: docker-compose 未安装${NC}"
    exit 1
fi

cd "$DOCKER_DIR"

echo -e "${YELLOW}正在启动基础设施服务...${NC}"

# 启动基础设施服务 (MongoDB, Redis, Consul)
docker-compose up -d mongodb-dev redis-dev consul-dev

echo -e "${YELLOW}等待基础设施服务启动...${NC}"
sleep 10

# 检查服务状态
echo -e "${YELLOW}检查服务状态...${NC}"

# 检查 MongoDB
if docker-compose exec -T mongodb-dev mongosh --eval "db.adminCommand('ping')" > /dev/null 2>&1; then
    echo -e "${GREEN}✓ MongoDB 已就绪${NC}"
else
    echo -e "${RED}✗ MongoDB 启动失败${NC}"
fi

# 检查 Redis
if docker-compose exec -T redis-dev redis-cli -a dev_password ping > /dev/null 2>&1; then
    echo -e "${GREEN}✓ Redis 已就绪${NC}"
else
    echo -e "${RED}✗ Redis 启动失败${NC}"
fi

# 检查 Consul
if curl -s http://localhost:8500/v1/status/leader > /dev/null 2>&1; then
    echo -e "${GREEN}✓ Consul 已就绪${NC}"
else
    echo -e "${RED}✗ Consul 启动失败${NC}"
fi

echo -e "${BLUE}=== 基础设施服务启动完成 ===${NC}"
echo
echo -e "${YELLOW}访问地址:${NC}"
echo "  MongoDB: mongodb://admin:dev_password@localhost:27017/gameapp_dev"
echo "  Redis: redis://localhost:6379 (密码: dev_password)"
echo "  Consul UI: http://localhost:8500"
echo
echo -e "${YELLOW}下一步:${NC}"
echo "  1. 构建并启动应用服务: ./start-services.sh"
echo "  2. 查看服务状态: docker-compose ps"
echo "  3. 查看日志: docker-compose logs -f [service-name]"
echo "  4. 停止所有服务: ./stop-dev.sh"
