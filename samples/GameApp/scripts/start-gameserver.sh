#!/bin/bash

# GameApp GameServer 启动脚本

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

echo -e "${BLUE}=== GameApp GameServer 启动 ===${NC}"
echo "项目根目录: $PROJECT_ROOT"

# 检查基础设施是否运行
echo -e "${YELLOW}检查基础设施服务状态...${NC}"

# 检查 MongoDB
if ! docker-compose -f "$DOCKER_DIR/docker-compose.yml" exec -T mongodb-dev mongosh --eval "db.adminCommand('ping')" > /dev/null 2>&1; then
    echo -e "${RED}MongoDB 未运行，请先启动基础设施服务：${NC}"
    echo "./scripts/start-dev.sh"
    exit 1
fi

# 检查 Redis
if ! docker-compose -f "$DOCKER_DIR/docker-compose.yml" exec -T redis-dev redis-cli -a dev_password ping > /dev/null 2>&1; then
    echo -e "${RED}Redis 未运行，请先启动基础设施服务：${NC}"
    echo "./scripts/start-dev.sh"
    exit 1
fi

# 检查 Consul
if ! curl -s http://localhost:8500/v1/status/leader > /dev/null 2>&1; then
    echo -e "${RED}Consul 未运行，请先启动基础设施服务：${NC}"
    echo "./scripts/start-dev.sh"
    exit 1
fi

echo -e "${GREEN}✓ 基础设施服务运行正常${NC}"

cd "$DOCKER_DIR"

echo -e "${YELLOW}启动 AuthServer...${NC}"
docker-compose up -d authserver-dev

echo -e "${YELLOW}等待 AuthServer 启动...${NC}"
sleep 10

# 检查 AuthServer 健康状态
if curl -s http://localhost:8080/health > /dev/null 2>&1; then
    echo -e "${GREEN}✓ AuthServer 已就绪${NC}"
else
    echo -e "${RED}✗ AuthServer 启动失败${NC}"
    docker-compose logs authserver-dev
    exit 1
fi

echo -e "${YELLOW}启动 GameServer...${NC}"
docker-compose up -d gameserver-dev

echo -e "${YELLOW}等待 GameServer 启动...${NC}"
sleep 15

# 检查 GameServer 端口是否可用
if nc -z localhost 7000 && nc -z localhost 7001; then
    echo -e "${GREEN}✓ GameServer 已就绪${NC}"
    echo "  TCP 端口: 7000"
    echo "  KCP 端口: 7001"
else
    echo -e "${RED}✗ GameServer 启动失败${NC}"
    docker-compose logs gameserver-dev
    exit 1
fi

echo -e "${BLUE}=== GameApp 服务启动完成 ===${NC}"
echo
echo -e "${YELLOW}服务状态:${NC}"
echo "  AuthServer: http://localhost:8080"
echo "  GameServer TCP: localhost:7000"
echo "  GameServer KCP: localhost:7001"
echo "  Consul UI: http://localhost:8500"
echo
echo -e "${YELLOW}有用的命令:${NC}"
echo "  查看服务状态: docker-compose ps"
echo "  查看 AuthServer 日志: docker-compose logs -f authserver-dev"
echo "  查看 GameServer 日志: docker-compose logs -f gameserver-dev"
echo "  停止服务: ./stop-dev.sh"
