# Quickstart: Running Your First PulseRPC Benchmark

**Feature**: Complete BenchmarkApp for PulseRPC Performance Testing
**Version**: 1.0
**Date**: 2025-10-10

## Overview

This quickstart guide walks you through running your first PulseRPC performance benchmark, from starting the test server to analyzing detailed HTML reports. By the end of this guide, you'll have baseline performance metrics for your PulseRPC deployment.

**Time Required**: 10-15 minutes
**Skill Level**: Beginner

---

## Prerequisites

### Required
- ✅ **.NET 9.0 SDK** installed ([Download](https://dotnet.microsoft.com/download/dotnet/9.0))
- ✅ **Terminal/Command Prompt** with administrator privileges (for port binding)
- ✅ **Web browser** (for viewing HTML reports)

### Optional
- 📊 **Basic understanding of performance testing** (latency, throughput, percentiles)
- 🔧 **Familiarity with command-line tools**

### Verify Installation
```bash
dotnet --version
# Expected output: 9.0.xxx or higher
```

---

## Step 1: Build the BenchmarkApp

Navigate to the repository root and build the solution:

```bash
cd D:\Projects\PulseRPC
dotnet build perf/BenchmarkApp/PulseRPC.Benchmark.sln
```

**Expected Output**:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

**⚠️ Troubleshooting**:
- If build fails with missing SDK: Update `global.json` to match your installed SDK version
- If missing dependencies: Run `dotnet restore` first

---

## Step 2: Start the Benchmark Server

The benchmark server provides optimized test endpoints for accurate performance measurement.

**Terminal 1** (keep this running):
```bash
dotnet run --project perf/BenchmarkApp/PulseRPC.Benchmark.Server
```

**Expected Output**:
```
info: PulseRPC.Benchmark.Server.BenchmarkServerHost[0]
      Starting PulseRPC Benchmark Server...
info: PulseRPC.Benchmark.Server.BenchmarkServerHost[0]
      Server listening on:
        - TCP: 0.0.0.0:8080
        - KCP: 0.0.0.0:9001
info: PulseRPC.Benchmark.Server.BenchmarkServerHost[0]
      Benchmark server ready. Press Ctrl+C to stop.
```

**✅ Validation Checkpoint**:
- [ ] Console shows "Server listening on TCP: 0.0.0.0:8080"
- [ ] Console shows "Server listening on KCP: 0.0.0.0:9001"
- [ ] No error messages displayed

**⚠️ Troubleshooting**:
- **Port already in use**: Change ports in `appsettings.json` or stop conflicting process
- **Permission denied**: Run terminal as administrator
- **Firewall blocking**: Allow .NET through Windows Firewall

---

## Step 3: Run Your First Benchmark (Latency Test)

Open a **second terminal** (keep server running in first terminal):

```bash
dotnet run --project perf/BenchmarkApp/PulseRPC.Benchmark.Client -- run \
  --server localhost:8080 \
  --scenario ping-pong \
  --duration 30 \
  --connections 5 \
  --rate 50 \
  --message-size 1024
```

**Command Explanation**:
- `--server localhost:8080`: Connect to local benchmark server (TCP)
- `--scenario ping-pong`: Run latency measurement (echo pattern)
- `--duration 30`: Test for 30 seconds
- `--connections 5`: Use 5 concurrent connections
- `--rate 50`: Send 50 requests/second per connection (250 total req/s)
- `--message-size 1024`: Send 1KB messages

**Expected Output** (real-time display):
```
╭────────────────────────────────────────────────────────────╮
│  PulseRPC Benchmark - Ping Pong Latency Test              │
│  Server: localhost:8080 (TCP)                              │
│  Duration: 00:00:30 | Connections: 5 | Rate: 50 req/s     │
╰────────────────────────────────────────────────────────────╯

Progress: ████████████████████ 100% [00:00:30/00:00:30]

┌─ Latency (ms) ────────────────────────────────────────────┐
│  Min: 5.2  │  P50: 18.3  │  P95: 42.1  │  P99: 78.4      │
│  Max: 125.7│  Mean: 19.5 │  P99.9: 110.2               │
└───────────────────────────────────────────────────────────┘

┌─ Throughput ──────────────────────────────────────────────┐
│  Messages/s: 248.7  │  Sent: 86.2 MB/s                   │
│  Total Msgs: 7,461  │  Recv: 80.5 MB/s                   │
└───────────────────────────────────────────────────────────┘

┌─ System Resources ────────────────────────────────────────┐
│  CPU: 52.3%  │  Memory: 245 MB  │  Threads: 28           │
└───────────────────────────────────────────────────────────┘

┌─ Results ─────────────────────────────────────────────────┐
│  Success Rate: 99.8%  │  Failed: 15  │  Timeouts: 0      │
│  Status: ✅ PASS                                          │
└───────────────────────────────────────────────────────────┘

Report saved to: results/benchmark-20251010-100530.json
```

**✅ Validation Checkpoint**:
- [ ] Progress bar reaches 100%
- [ ] Latency P95 < 100ms (should be ~20-50ms on local network)
- [ ] Success rate > 95%
- [ ] Report saved message displayed

**⚠️ Troubleshooting**:
- **Connection refused**: Verify server is running and port 8080 is correct
- **High latency (>500ms)**: Check for other processes consuming CPU/network
- **Low success rate (<95%)**: Server may be overloaded, reduce `--rate` or `--connections`

---

## Step 4: Generate HTML Report

Convert the JSON results to an interactive HTML report with charts:

```bash
dotnet run --project perf/BenchmarkApp/PulseRPC.Benchmark.Client -- report \
  --input results/benchmark-20251010-100530.json \
  --format html \
  --output reports/my-first-benchmark.html
```

**Note**: Replace `benchmark-20251010-100530.json` with your actual filename from Step 3.

**Expected Output**:
```
Generating HTML report...
✅ Report generated successfully: reports/my-first-benchmark.html
Open in browser to view detailed analysis and charts.
```

**✅ Validation Checkpoint**:
- [ ] HTML file created in `reports/` directory
- [ ] File size > 100KB (contains embedded charts)

---

## Step 5: View the Report

Open the generated HTML report in your web browser:

**Windows**:
```bash
start reports/my-first-benchmark.html
```

**macOS/Linux**:
```bash
open reports/my-first-benchmark.html  # macOS
xdg-open reports/my-first-benchmark.html  # Linux
```

**Expected Report Sections**:

1. **Executive Summary**
   - Test scenario and configuration
   - Overall pass/fail status
   - Key metrics at a glance

2. **Latency Analysis**
   - 📊 Latency percentile chart (line graph)
   - 📊 Latency distribution histogram
   - Table with P50, P95, P99, P99.9 values

3. **Throughput Analysis**
   - 📊 Messages per second over time (line graph)
   - 📊 Network bandwidth utilization
   - Total messages sent/received

4. **Resource Utilization**
   - 📊 CPU usage over time
   - 📊 Memory usage over time
   - GC collection statistics

5. **Environment Details**
   - OS, CPU, memory specifications
   - .NET version
   - Test timestamp

**✅ Validation Checkpoint**:
- [ ] All charts render correctly
- [ ] Latency chart shows consistent values (not flat line)
- [ ] Throughput chart shows target rate (~250 req/s)
- [ ] No JavaScript errors in browser console (F12)

---

## Step 6: Run Additional Scenarios (Optional)

### Throughput Test (Maximum Performance)
```bash
dotnet run --project perf/BenchmarkApp/PulseRPC.Benchmark.Client -- run \
  --server localhost:8080 \
  --scenario throughput \
  --duration 60 \
  --connections 10 \
  --rate 100 \
  --message-size 512
```

**Expected Metrics**:
- Throughput: 500-1000 messages/second
- P95 latency: 20-100ms
- Success rate: >99%

### Concurrent Connection Test
```bash
dotnet run --project perf/BenchmarkApp/PulseRPC.Benchmark.Client -- run \
  --server localhost:8080 \
  --scenario concurrent \
  --duration 30 \
  --connections 100 \
  --rate 10
```

**Expected Metrics**:
- Active connections: 100
- Connection establishment time: <50ms
- No connection failures

### KCP Protocol Test (Low Latency)
```bash
dotnet run --project perf/BenchmarkApp/PulseRPC.Benchmark.Client -- run \
  --server localhost:9001 \
  --protocol kcp \
  --scenario ping-pong \
  --duration 30 \
  --connections 5 \
  --rate 50
```

**Expected Metrics**:
- P95 latency: ~10-30% lower than TCP
- Throughput: Similar to TCP
- Success rate: >99%

---

## Step 7: Clean Up

1. **Stop the benchmark server**: Press `Ctrl+C` in the server terminal

2. **View all results**:
   ```bash
   ls results/
   ls reports/
   ```

3. **Archive results** (optional):
   ```bash
   mkdir archive/$(date +%Y%m%d)
   mv results/*.json archive/$(date +%Y%m%d)/
   mv reports/*.html archive/$(date +%Y%m%d)/
   ```

---

## Next Steps

### 1. Create a Performance Baseline
Save your current results as a baseline for future comparisons:

```bash
dotnet run --project perf/BenchmarkApp/PulseRPC.Benchmark.Client -- baseline save \
  --input results/benchmark-20251010-100530.json \
  --name "v1.0-initial" \
  --description "Initial baseline before optimizations"
```

### 2. Run Baseline Comparison
After making changes, compare new results to baseline:

```bash
dotnet run --project perf/BenchmarkApp/PulseRPC.Benchmark.Client -- run \
  --server localhost:8080 \
  --scenario ping-pong \
  --duration 30 \
  --baseline "v1.0-initial"
```

**Report will include**:
- 📊 Side-by-side metric comparison
- 🔴 Performance regressions (slower)
- 🟢 Performance improvements (faster)
- Percentage change for each metric

### 3. Configure Performance Thresholds
Create a threshold configuration file `thresholds.json`:

```json
{
  "thresholds": [
    {
      "metricName": "Latency.P95",
      "operator": "LessThan",
      "targetValue": 50.0,
      "severity": "Error"
    },
    {
      "metricName": "SuccessRate",
      "operator": "GreaterThan",
      "targetValue": 99.5,
      "severity": "Error"
    },
    {
      "metricName": "Throughput.MessagesPerSecond",
      "operator": "GreaterThan",
      "targetValue": 200.0,
      "severity": "Warning"
    }
  ]
}
```

Run with thresholds:
```bash
dotnet run --project perf/BenchmarkApp/PulseRPC.Benchmark.Client -- run \
  --server localhost:8080 \
  --scenario ping-pong \
  --duration 30 \
  --thresholds thresholds.json
```

### 4. Explore Advanced Features
- **Streaming tests**: `--scenario streaming`
- **Stability tests**: `--duration 3600` (1 hour test)
- **Protocol comparison**: Run same test on TCP and KCP, generate comparison report
- **CSV export**: `--format csv` for custom analysis in Excel/Python
- **JSON export**: `--format json` for CI/CD integration

---

## Success Criteria

You have successfully completed the quickstart if:

- ✅ Benchmark server starts without errors
- ✅ Latency test completes with >95% success rate
- ✅ HTML report generates and displays charts correctly
- ✅ P95 latency < 100ms (local network)
- ✅ Throughput > 200 messages/second
- ✅ Resource usage stable (no memory leaks during test)

---

## Common Issues and Solutions

### Issue: "Port 8080 already in use"
**Solution**: Find and stop the conflicting process:
```bash
# Windows
netstat -ano | findstr :8080
taskkill /PID <pid> /F

# Linux/macOS
lsof -i :8080
kill <pid>
```

### Issue: Very high latency (>500ms)
**Causes**:
- Other CPU-intensive processes running
- Network congestion (if testing over network)
- Debug build (use Release build for accurate results)

**Solution**:
```bash
# Build in Release mode
dotnet build -c Release perf/BenchmarkApp/PulseRPC.Benchmark.sln

# Run Release build
dotnet run -c Release --project perf/BenchmarkApp/PulseRPC.Benchmark.Server
```

### Issue: Low success rate (<95%)
**Causes**:
- Request rate too high for server capacity
- Too many concurrent connections
- Network packet loss

**Solution**: Reduce load:
```bash
# Lower connection count
--connections 3

# Lower request rate
--rate 20
```

### Issue: HTML report doesn't render charts
**Causes**:
- JavaScript disabled in browser
- File opened from restricted location
- Corrupted report generation

**Solution**:
1. Check browser console (F12) for errors
2. Try different browser (Chrome, Firefox, Edge)
3. Regenerate report with `--verbose` flag

---

## Additional Resources

- **Full Documentation**: `docs/BenchmarkApp-Guide.md`
- **Configuration Reference**: `docs/BenchmarkApp-Configuration.md`
- **CI/CD Integration**: `docs/BenchmarkApp-CI-CD.md`
- **Performance Tuning**: `docs/HotPath-Optimization-Guide.md`
- **Issue Tracker**: [GitHub Issues](https://github.com/your-org/PulseRPC/issues)

---

## Feedback

Encountered issues or have suggestions? Please:
1. Check existing issues: [GitHub Issues](https://github.com/your-org/PulseRPC/issues)
2. Create new issue with:
   - Quickstart step where issue occurred
   - Complete error message
   - Environment details (OS, .NET version)
   - Logs from `results/benchmark-*.log`

---

**Version**: 1.0
**Last Updated**: 2025-10-10
**Estimated Completion Time**: 10-15 minutes
