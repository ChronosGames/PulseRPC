#!/bin/bash

# GameApp 开发环境重置脚本

set -e

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# 项目根目录
PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SCRIPTS_DIR="$PROJECT_ROOT/scripts"

echo -e "${BLUE}=== GameApp 开发环境重置 ===${NC}"
echo -e "${RED}警告: 这将删除所有数据和容器!${NC}"

read -p "确定要重置开发环境吗? (y/N): " -n 1 -r
echo
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    echo "操作已取消"
    exit 0
fi

echo -e "${YELLOW}正在重置开发环境...${NC}"

# 停止并清理环境
"$SCRIPTS_DIR/stop-dev.sh"

# 强制清理所有相关容器和卷
echo -e "${YELLOW}强制清理资源...${NC}"
docker ps -a | grep gameapp | awk '{print $1}' | xargs -r docker rm -f
docker volume ls | grep gameapp | awk '{print $2}' | xargs -r docker volume rm
docker network ls | grep gameapp | awk '{print $2}' | xargs -r docker network rm

echo -e "${GREEN}✓ 环境重置完成${NC}"
echo
echo -e "${YELLOW}重新启动环境:${NC}"
echo "  ./start-dev.sh"
