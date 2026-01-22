# PulseRPC Benchmark

PulseRPC 性能基准测试工具。

## 项目结构

```
PulseRPC.Benchmark/           # 主项目
├── Program.cs                # CLI 入口
├── Models/                   # 配置和结果模型
├── Scenarios/                # 测试场景
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

**吞吐量测试（Throughput）：**
```bash
dotnet run -c release --project PulseRPC.Benchmark -- client throughput --port 12345 --connections 10 --duration 30
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

### 3. 导出结果

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

## 测试场景说明

| 场景 | 说明 |
|------|------|
| `latency` | 测量单次 RPC 调用的往返时间（RTT） |
| `throughput` | 测量系统的吞吐量和处理能力 |
| `upload` | 测试客户端到服务端的数据传输带宽 |
| `download` | 测试服务端到客户端的数据传输带宽 |
| `stability` | 长时间运行测试，监控内存泄漏和连接稳定性 |
