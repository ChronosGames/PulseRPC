#!/bin/bash

echo "🚀 开始运行PulseRPC基准测试..."

# 创建结果目录
mkdir -p results

# 运行ping-pong测试（简化参数，避免实时显示）
echo "📊 运行ping-pong延迟测试..."
timeout 60s dotnet run --project PulseRPC.Benchmark.Client -- run \
  --server localhost:8080 \
  --scenario ping-pong \
  --duration 20 \
  --connections 3 \
  --rate 50 \
  --output results/ping-pong-test.json \
  --format json \
  --warmup 5 || echo "ping-pong测试完成或超时"

sleep 2

# 运行吞吐量测试
echo "📈 运行吞吐量测试..."
timeout 60s dotnet run --project PulseRPC.Benchmark.Client -- run \
  --server localhost:8080 \
  --scenario throughput \
  --duration 20 \
  --connections 5 \
  --rate 100 \
  --output results/throughput-test.json \
  --format json \
  --warmup 5 || echo "吞吐量测试完成或超时"

sleep 2

# 运行echo延迟测试
echo "🔄 运行echo延迟测试..."
timeout 60s dotnet run --project PulseRPC.Benchmark.Client -- run \
  --server localhost:8080 \
  --scenario echo-latency \
  --duration 15 \
  --connections 2 \
  --rate 30 \
  --output results/echo-latency-test.json \
  --format json \
  --warmup 3 || echo "echo延迟测试完成或超时"

echo "✅ 所有测试完成，结果保存在results/目录"