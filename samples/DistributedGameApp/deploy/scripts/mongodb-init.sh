#!/bin/bash
# MongoDB 索引初始化脚本（Linux/Mac）

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
MONGODB_DIR="$PROJECT_ROOT/deploy/mongodb"

echo "========================================="
echo "MongoDB 索引初始化"
echo "========================================="

# 检查 MongoDB 是否在运行
check_mongodb() {
    echo "→ 检查 MongoDB 连接..."
    if mongosh --eval "db.adminCommand('ping')" --quiet > /dev/null 2>&1; then
        echo "  ✓ MongoDB 连接成功"
        return 0
    else
        echo "  ❌ 无法连接到 MongoDB"
        return 1
    fi
}

# 初始化索引
init_indexes() {
    echo ""
    echo "→ 执行索引初始化脚本..."

    # 方式 1: 直接执行 init.js
    if [ -f "$MONGODB_DIR/init.js" ]; then
        mongosh < "$MONGODB_DIR/init.js"
        echo "  ✓ 索引初始化完成"
    else
        echo "  ❌ 找不到 init.js 文件: $MONGODB_DIR/init.js"
        exit 1
    fi
}

# 验证索引
verify_indexes() {
    echo ""
    echo "→ 验证索引..."

    if [ -f "$MONGODB_DIR/verify-indexes.js" ]; then
        mongosh < "$MONGODB_DIR/verify-indexes.js"
    else
        echo "  ⚠️  找不到 verify-indexes.js 文件"
    fi
}

# Docker 环境初始化
init_docker() {
    echo ""
    echo "→ 在 Docker 容器中初始化索引..."

    local CONTAINER_NAME="${1:-pulserpc-mongodb}"

    if docker ps | grep -q "$CONTAINER_NAME"; then
        echo "  → 容器 $CONTAINER_NAME 正在运行"

        # 复制脚本到容器
        docker cp "$MONGODB_DIR/init.js" "$CONTAINER_NAME:/tmp/init.js"

        # 在容器中执行
        docker exec -it "$CONTAINER_NAME" mongosh < /tmp/init.js

        echo "  ✓ Docker 容器中的索引初始化完成"
    else
        echo "  ❌ 容器 $CONTAINER_NAME 未运行"
        echo "  提示: 使用 docker-compose up -d 启动容器"
        exit 1
    fi
}

# 显示使用方法
usage() {
    echo "用法: $0 [选项]"
    echo ""
    echo "选项:"
    echo "  local         初始化本地 MongoDB"
    echo "  docker        初始化 Docker 容器中的 MongoDB"
    echo "  verify        验证索引"
    echo "  help          显示此帮助信息"
    echo ""
    echo "示例:"
    echo "  $0 local      # 初始化本地 MongoDB"
    echo "  $0 docker     # 初始化 Docker MongoDB"
    echo "  $0 verify     # 验证索引"
}

# 主逻辑
main() {
    case "${1:-local}" in
        local)
            if check_mongodb; then
                init_indexes
                verify_indexes
            fi
            ;;
        docker)
            init_docker "${2:-pulserpc-mongodb}"
            ;;
        verify)
            if check_mongodb; then
                verify_indexes
            fi
            ;;
        help|--help|-h)
            usage
            ;;
        *)
            echo "❌ 未知选项: $1"
            usage
            exit 1
            ;;
    esac
}

main "$@"

echo ""
echo "========================================="
echo "完成"
echo "========================================="
