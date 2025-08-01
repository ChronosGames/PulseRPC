using GameApp.Infrastructure.Performance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Threading.Tasks;

namespace GameApp.AuthServer.Controllers
{
    /// <summary>
    /// 性能监控控制器 - 提供性能数据和健康检查端点
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class PerformanceController : ControllerBase
    {
        private readonly IPerformanceService _performanceService;

        public PerformanceController(IPerformanceService performanceService)
        {
            _performanceService = performanceService;
        }

        /// <summary>
        /// 获取性能统计信息
        /// </summary>
        /// <param name="hours">统计时间范围（小时）</param>
        [HttpGet("stats")]
        public async Task<ActionResult<PerformanceStats>> GetPerformanceStats([FromQuery] int hours = 1)
        {
            if (hours < 1 || hours > 24)
            {
                return BadRequest("统计时间范围必须在1-24小时之间");
            }

            var stats = await _performanceService.GetStatsAsync(TimeSpan.FromHours(hours));
            return Ok(stats);
        }

        /// <summary>
        /// 获取当前系统资源使用情况
        /// </summary>
        [HttpGet("resources")]
        public ActionResult<SystemResourceUsage> GetSystemResources()
        {
            var resources = _performanceService.GetSystemResourceUsage();
            return Ok(resources);
        }

        /// <summary>
        /// 记录自定义性能指标
        /// </summary>
        [HttpPost("metrics")]
        public async Task<ActionResult> RecordMetric([FromBody] MetricRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest("指标名称不能为空");
            }

            await _performanceService.RecordMetricAsync(request.Name, request.Value, request.Tags);
            return Ok(new { message = "指标记录成功" });
        }

        /// <summary>
        /// 健康检查端点 - 包含性能健康状态
        /// </summary>
        [HttpGet("health")]
        public async Task<ActionResult<HealthCheckResult>> HealthCheck()
        {
            var resources = _performanceService.GetSystemResourceUsage();
            var stats = await _performanceService.GetStatsAsync(TimeSpan.FromMinutes(5));

            var health = new HealthCheckResult
            {
                Status = DetermineHealthStatus(resources, stats),
                Timestamp = DateTime.UtcNow,
                Resources = resources,
                RecentStats = stats
            };

            var statusCode = health.Status switch
            {
                HealthStatus.Healthy => 200,
                HealthStatus.Warning => 200,
                HealthStatus.Critical => 503,
                _ => 500
            };

            return StatusCode(statusCode, health);
        }

        /// <summary>
        /// 性能基准测试端点 - 测试服务器响应能力
        /// </summary>
        [HttpGet("benchmark")]
        public async Task<ActionResult<BenchmarkResult>> RunBenchmark()
        {
            var startTime = DateTime.UtcNow;

            // 执行一些基准测试操作
            await Task.Delay(1); // 模拟最小延迟

            var cpuTest = await RunCpuBenchmark();
            var memoryTest = RunMemoryBenchmark();
            var diskTest = await RunDiskBenchmark();

            var endTime = DateTime.UtcNow;
            var totalTime = endTime - startTime;

            var result = new BenchmarkResult
            {
                StartTime = startTime,
                EndTime = endTime,
                TotalDuration = totalTime,
                CpuScore = cpuTest,
                MemoryScore = memoryTest,
                DiskScore = diskTest,
                OverallScore = (cpuTest + memoryTest + diskTest) / 3
            };

            return Ok(result);
        }

        private HealthStatus DetermineHealthStatus(SystemResourceUsage resources, PerformanceStats stats)
        {
            // CPU使用率检查
            if (resources.CpuUsagePercent > 90)
                return HealthStatus.Critical;
            if (resources.CpuUsagePercent > 70)
                return HealthStatus.Warning;

            // 内存使用率检查
            if (resources.MemoryUsagePercent > 90)
                return HealthStatus.Critical;
            if (resources.MemoryUsagePercent > 80)
                return HealthStatus.Warning;

            // 错误率检查
            if (stats.ErrorRate > 0.1) // 10%错误率
                return HealthStatus.Critical;
            if (stats.ErrorRate > 0.05) // 5%错误率
                return HealthStatus.Warning;

            // 响应时间检查
            if (stats.AverageResponseTime > 2000) // 2秒
                return HealthStatus.Critical;
            if (stats.AverageResponseTime > 1000) // 1秒
                return HealthStatus.Warning;

            return HealthStatus.Healthy;
        }

        private async Task<double> RunCpuBenchmark()
        {
            var startTime = DateTime.UtcNow;

            // 简单的CPU密集型操作
            double result = 0;
            for (int i = 0; i < 100000; i++)
            {
                result += Math.Sqrt(i) * Math.Sin(i);
            }

            var duration = DateTime.UtcNow - startTime;

            // 分数基于完成时间（越快分数越高）
            return Math.Max(0, 100 - duration.TotalMilliseconds / 10);
        }

        private double RunMemoryBenchmark()
        {
            try
            {
                var arrays = new List<byte[]>();
                var startMemory = GC.GetTotalMemory(false);

                // 分配和释放内存
                for (int i = 0; i < 100; i++)
                {
                    arrays.Add(new byte[10240]); // 10KB each
                }

                var peakMemory = GC.GetTotalMemory(false);
                arrays.Clear();
                GC.Collect();

                var finalMemory = GC.GetTotalMemory(true);
                var memoryReclaimed = peakMemory - finalMemory;

                // 分数基于内存回收效率
                return Math.Min(100, (double)memoryReclaimed / (peakMemory - startMemory) * 100);
            }
            catch
            {
                return 50; // 默认分数
            }
        }

        private async Task<double> RunDiskBenchmark()
        {
            try
            {
                var startTime = DateTime.UtcNow;

                // 简单的I/O操作基准
                var tempFile = Path.GetTempFileName();
                var data = new byte[1024]; // 1KB

                await System.IO.File.WriteAllBytesAsync(tempFile, data);
                var readData = await System.IO.File.ReadAllBytesAsync(tempFile);
                System.IO.File.Delete(tempFile);

                var duration = DateTime.UtcNow - startTime;

                // 分数基于I/O速度
                return Math.Max(0, 100 - duration.TotalMilliseconds);
            }
            catch
            {
                return 50; // 默认分数
            }
        }
    }

    // DTOs
    public class MetricRequest
    {
        public string Name { get; set; } = string.Empty;
        public double Value { get; set; }
        public Dictionary<string, string>? Tags { get; set; }
    }

    public class HealthCheckResult
    {
        public HealthStatus Status { get; set; }
        public DateTime Timestamp { get; set; }
        public SystemResourceUsage Resources { get; set; } = new();
        public PerformanceStats RecentStats { get; set; } = new();
    }

    public enum HealthStatus
    {
        Healthy,
        Warning,
        Critical
    }

    public class BenchmarkResult
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public double CpuScore { get; set; }
        public double MemoryScore { get; set; }
        public double DiskScore { get; set; }
        public double OverallScore { get; set; }
    }
}
