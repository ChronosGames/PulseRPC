# PulseRPC Benchmark

PulseRPC 性能基准测试工具。

## 项目结构

```
PulseRPC.Benchmark/           # 主项目
├── Program.cs                # CLI 入口
├── Models/                   # 配置和结果模型
├── Scenarios/                # 测试场景
├── Clustering/               # 真实三节点 TCP 拓扑与基准
├── Server/                   # 服务端实现
├── Client/                   # 客户端运行器
└── Reports/                  # 报告生成

PulseRPC.Benchmark.Contracts/ # 契约定义
├── IBenchmarkHub.cs          # Hub 接口
└── Messages.cs               # 消息定义
```

## 构建

```bash
cd perf/BenchmarkApp
dotnet build
```

## 使用方法

### 1. 启动服务端

```bash
dotnet run -c release --project PulseRPC.Benchmark -- server --tcp-port 12345
```

### 2. 运行客户端测试

**查看可用场景：**
```bash
dotnet run --project PulseRPC.Benchmark -- list
```

**延迟测试（Latency）：**
```bash
dotnet run -c release --project PulseRPC.Benchmark -- client latency --port 12345 --iterations 100000 --warmup 100
```

```
═══════════════════════════════════════════════════════════════
  基准测试结果: latency
═══════════════════════════════════════════════════════════════

  状态: 成功
  开始时间: 2026-01-22 15:20:52
  结束时间: 2026-01-22 15:21:04
  总时长: 11.39 秒

  ─── 延迟指标 ───────────────────────────────────────────────
  样本数:     100,000
  平均延迟:   0.104 ms
  最小延迟:   0.072 ms
  最大延迟:   9.237 ms
  P50:        0.098 ms
  P90:        0.120 ms
  P95:        0.131 ms
  P99:        0.193 ms
  标准差:     0.054 ms

  ─── 吞吐量指标 ─────────────────────────────────────────────
  总操作数:   100,000
  成功:       100,000
  失败:       0
  成功率:     100.00%
  OPS:        8776.11 ops/s

═══════════════════════════════════════════════════════════════
```

**吞吐量测试（Throughput）：**
```bash
dotnet run -c release --project PulseRPC.Benchmark -- client throughput --port 12345 --connections 10 --duration 30
```

```
═══════════════════════════════════════════════════════════════
  基准测试结果: throughput
═══════════════════════════════════════════════════════════════

  状态: 成功
  开始时间: 2026-01-22 15:19:57
  结束时间: 2026-01-22 15:20:28
  总时长: 30.95 秒

  ─── 延迟指标 ───────────────────────────────────────────────
  样本数:     251,883
  平均延迟:   0.112 ms
  最小延迟:   0.070 ms
  最大延迟:   25.848 ms
  P50:        0.099 ms
  P90:        0.123 ms
  P95:        0.142 ms
  P99:        0.210 ms
  标准差:     0.322 ms

  ─── 吞吐量指标 ─────────────────────────────────────────────
  总操作数:   251,892
  成功:       251,883
  失败:       9
  成功率:     100.00%
  OPS:        8169.40 ops/s
  数据传输:   491.96 MB
  带宽:       15.96 MB/s

═══════════════════════════════════════════════════════════════
```

**上传带宽测试（Upload）：**
```bash
dotnet run -c release --project PulseRPC.Benchmark -- client upload --port 12345 --size 65536 --duration 30
```

**下载带宽测试（Download）：**
```bash
dotnet run -c release --project PulseRPC.Benchmark -- client download --port 12345 --size 65536 --duration 30
```

**稳定性测试（Stability）：**
```bash
dotnet run -c release --project PulseRPC.Benchmark -- client stability --port 12345 --duration 300
```

```
═══════════════════════════════════════════════════════════════
  基准测试结果: stability
═══════════════════════════════════════════════════════════════

  状态: 成功
  开始时间: 2026-01-22 15:57:47
  结束时间: 2026-01-22 16:02:47
  总时长: 300.04 秒

  ─── 延迟指标 ───────────────────────────────────────────────
  样本数:     100,000
  平均延迟:   0.127 ms
  最小延迟:   0.082 ms
  最大延迟:   27.598 ms
  P50:        0.116 ms
  P90:        0.142 ms
  P95:        0.161 ms
  P99:        0.225 ms
  标准差:     0.271 ms

  ─── 吞吐量指标 ─────────────────────────────────────────────
  总操作数:   2,231,850
  成功:       2,231,850
  失败:       0
  成功率:     100.00%
  OPS:        7439.50 ops/s

  ─── 资源指标 ───────────────────────────────────────────────
  平均内存:   6.52 MB
  峰值内存:   9.25 MB
  GC Gen0:    2684
  GC Gen1:    92
  GC Gen2:    3

  ─── 稳定性指标 ─────────────────────────────────────────────
  内存泄漏:   未检测到
  内存增长率: 316.89 KB/sample
  连接失败:   0
  内存样本:   11

═══════════════════════════════════════════════════════════════
```

### 3. 真实三节点 TCP 基准

该命令在同一进程内启动三个独立 PulseServer，外部用户上下文先进入 A 的 `GatewayFrontHub`，节点间使用内置 `TcpNodeTransport`，每次计量请求均完整经过 `Gateway A -> B -> C`，并在 C 校验 claims、角色、权限及 bearer token 剥离：

```bash
dotnet run -c Release --project PulseRPC.Benchmark -- cluster-three-hop --warmup 200 --iterations 10000 --concurrency 8
```

CI 或本地快速验证使用 smoke 参数；它会把规模限制为最多 5 次预热、30 次计量和 2 并发：

```bash
dotnet run -c Release --project PulseRPC.Benchmark -- cluster-three-hop --smoke
```

输出包含 `ops/s` 以及端到端延迟 `p50/p95/p99`。

### 4. 传输与 Actor 高并发架构基线

该命令覆盖批处理传输的 `Block` 背压、热 Actor 查找、Actor mailbox 背压，以及并发创建/移除。两个背压场景使用固定 200 微秒的受限消费者来稳定制造队列压力，不代表真实网络 RTT。报告同时记录吞吐量、端到端 `p50/p95/p99/max`、进程级分配字节/操作，以及从提交到开始消费的等待分布。

仓库参考负载固定为 2,000 次操作和 32 个 worker；正式模式默认重复 3 轮，并逐指标报告中位数：

```bash
dotnet run -c Release --project PulseRPC.Benchmark -- architecture-baseline \
  --operations 2000 --concurrency 32 \
  --output baselines/architecture-reference-windows-x64.json
```

在同一台空闲机器、相同 Runtime/OS/GC 配置下比较新结果：

```bash
dotnet run -c Release --project PulseRPC.Benchmark -- architecture-baseline \
  --operations 2000 --concurrency 32 \
  --compare baselines/architecture-reference-windows-x64.json \
  --output architecture-current.json
```

`--max-regression-percent` 可在同机重复跑稳定后启用门禁；默认值 `0` 只报告差异。命令会拒绝 workload 或 schema 不一致的比较，并禁止在 Runtime、OS、CPU、进程架构或 GC 模式不同的环境中启用百分比门禁。分配量使用 `GC.GetTotalAllocatedBytes`，包含并发调度驱动开销，因此适合观察同环境趋势，不应当解释为单个 API 的纯分配量。CI 使用 `--smoke` 验证场景可运行，不把共享 runner 的易波动性能数值设为硬门禁。

### 5. 导出结果

添加 `--output` 参数将结果保存为 JSON 文件：

```bash
dotnet run -c release --project PulseRPC.Benchmark -- client latency --port 12345 --output result.json
```

## 命令行参数

### 服务端参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `--tcp-port` | 12345 | TCP 监听端口 |

### 客户端参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `--host` | localhost | 服务端主机地址 |
| `--port` | 12345 | 服务端端口 |
| `--iterations` | 10000 | 迭代次数 |
| `--duration` | 30 | 持续时间（秒） |
| `--connections` | 1 | 并发连接数 |
| `--size` | 1024 | 消息大小（字节） |
| `--warmup` | 100 | 预热迭代次数 |
| `--output` | - | 输出文件路径（JSON） |

### 三节点 TCP 参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `--warmup` | 200 | 预热请求数 |
| `--iterations` | 10000 | 计量请求数 |
| `--concurrency` | CPU 核数 | 并发请求数 |
| `--smoke` | false | 启用短跑上限 |

### 架构基线参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `--operations` | 2000 | 两个受限背压场景的操作数；非 smoke 时热查找至少 20 万次，生命周期至少 5,000 次 |
| `--concurrency` | `CPU * 4`，限制为 8–64 | 并发 worker 数 |
| `--repetitions` | 3 | 正式基准重复次数（1–9），报告逐指标中位数；smoke 固定为 1 |
| `--smoke` | false | 将规模限制为最多 250 次操作和 8 并发 |
| `--output` | - | JSON 结果输出路径 |
| `--compare` | - | 旧 JSON 基线路径 |
| `--max-regression-percent` | 0 | P95、分配、等待或吞吐回归超过阈值时失败；0 仅报告 |

## 测试场景说明

| 场景 | 说明 |
|------|------|
| `latency` | 测量单次 RPC 调用的往返时间（RTT） |
| `throughput` | 测量系统的吞吐量和处理能力 |
| `upload` | 测试客户端到服务端的数据传输带宽 |
| `download` | 测试服务端到客户端的数据传输带宽 |
| `stability` | 长时间运行测试，监控内存泄漏和连接稳定性 |

## 测试结果

```
═══════════════════════════════════════════════════════════════
  基准测试结果: latency
═══════════════════════════════════════════════════════════════

  状态: 成功
  开始时间: 2026-01-22 15:07:30
  结束时间: 2026-01-22 15:07:42
  总时长: 12.39 秒

  ─── 延迟指标 ───────────────────────────────────────────────
  样本数:     100,000
  平均延迟:   0.113 ms
  最小延迟:   0.076 ms
  最大延迟:   16.300 ms
  P50:        0.105 ms
  P90:        0.134 ms
  P95:        0.148 ms
  P99:        0.218 ms
  标准差:     0.101 ms

  ─── 吞吐量指标 ─────────────────────────────────────────────
  总操作数:   100,000
  成功:       100,000
  失败:       0
  成功率:     100.00%
  OPS:        8073.19 ops/s

═══════════════════════════════════════════════════════════════
```
