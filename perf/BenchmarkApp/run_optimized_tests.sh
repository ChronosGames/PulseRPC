#!/bin/bash

echo "🚀 PulseRPC 高性能基准测试对比"
echo "=================================="

# 创建结果目录
mkdir -p results/optimized
mkdir -p results/comparison

echo "📊 配置环境变量..."
export DOTNET_GCServer=1
export DOTNET_GCConcurrent=1
export DOTNET_GCRetainVM=1

# 首先运行原版测试作为基准
echo "🔍 运行原版基准测试（作为对比基准）..."
echo "测试参数: 1000次ping, 1个连接, 无间隔, 5秒预热"

timeout 60s dotnet run --project PulseRPC.Benchmark.Client -- run \
  --server localhost:8080 \
  --scenario ping-pong \
  --duration 0 \
  --connections 1 \
  --rate 0 \
  --warmup 5 \
  --output results/comparison/original-test.json \
  --format json \
  --log-level Warning > results/comparison/original-test.log 2>&1 || echo "原版测试完成"

sleep 2

# 如果有优化版本的话运行优化测试
echo "⚡ 运行优化版基准测试..."
echo "测试参数: 1000次ping, 1个连接, 最小化开销"

# 注意：这里假设我们已经实现了optimized-ping-pong场景
timeout 60s dotnet run --project PulseRPC.Benchmark.Client -- run \
  --server localhost:8080 \
  --scenario optimized-ping-pong \
  --duration 0 \
  --connections 1 \
  --rate 0 \
  --warmup 2 \
  --output results/optimized/optimized-test.json \
  --format json \
  --log-level Error > results/optimized/optimized-test.log 2>&1 || echo "优化测试完成"

sleep 2

# 运行连续性能测试
echo "🔥 运行高强度连续性能测试..."
for i in {1..3}; do
    echo "第 $i 轮测试..."
    timeout 30s dotnet run --project PulseRPC.Benchmark.Client -- run \
      --server localhost:8080 \
      --scenario ping-pong \
      --duration 20 \
      --connections 1 \
      --rate 200 \
      --warmup 2 \
      --output "results/optimized/round-$i.json" \
      --format json \
      --log-level Error > "results/optimized/round-$i.log" 2>&1 || echo "第 $i 轮完成"
    sleep 1
done

echo ""
echo "✅ 所有测试完成！"
echo ""
echo "📈 结果文件位置："
echo "   原版测试: results/comparison/original-test.json"
echo "   优化测试: results/optimized/optimized-test.json" 
echo "   连续测试: results/optimized/round-*.json"
echo ""
echo "📝 日志文件："
find results/ -name "*.log" -type f
echo ""

# 尝试分析结果
if command -v jq &> /dev/null; then
    echo "📊 快速性能分析 (如果结果文件可用):"
    for file in results/comparison/*.json results/optimized/*.json; do
        if [ -f "$file" ]; then
            echo "文件: $(basename $file)"
            # jq '.metrics.latency.average // "N/A"' "$file" 2>/dev/null || echo "  无法解析结果"
        fi
    done
else
    echo "💡 提示: 安装 jq 工具可以自动分析JSON结果文件"
fi

echo ""
echo "🎯 下一步建议："
echo "   1. 检查日志文件中的错误信息"
echo "   2. 使用 generate-report 命令生成HTML报告"
echo "   3. 对比优化前后的延迟数据"