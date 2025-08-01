#!/bin/bash

# GameApp 开发环境停止脚本

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

echo -e "${BLUE}=== GameApp 开发环境停止 ===${NC}"

cd "$DOCKER_DIR"

echo -e "${YELLOW}正在停止所有服务...${NC}"

# 停止所有服务
docker-compose down

echo -e "${GREEN}✓ 所有服务已停止${NC}"

# 询问是否清理数据
echo
read -p "是否清理所有数据卷? (y/N): " -n 1 -r
echo
if [[ $REPLY =~ ^[Yy]$ ]]; then
    echo -e "${YELLOW}正在清理数据卷...${NC}"
    docker-compose down -v
    docker volume rm gameapp-mongodb-dev-data gameapp-redis-dev-data gameapp-consul-dev-data 2>/dev/null || true
    echo -e "${GREEN}✓ 数据卷已清理${NC}"
fi

# 询问是否清理镜像
echo
read -p "是否清理构建的镜像? (y/N): " -n 1 -r
echo
if [[ $REPLY =~ ^[Yy]$ ]]; then
    echo -e "${YELLOW}正在清理镜像...${NC}"
    docker image prune -f
    docker images | grep "gameapp" | awk '{print $3}' | xargs -r docker rmi
    echo -e "${GREEN}✓ 镜像已清理${NC}"
fi

echo -e "${BLUE}=== 环境清理完成 ===${NC}"
