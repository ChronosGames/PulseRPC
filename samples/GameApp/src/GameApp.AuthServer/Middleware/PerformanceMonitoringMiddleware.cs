using GameApp.Infrastructure.Performance;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace GameApp.AuthServer.Middleware
{
    /// <summary>
    /// 性能监控中间件 - 监控请求响应时间和系统资源使用
    /// </summary>
    public class PerformanceMonitoringMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<PerformanceMonitoringMiddleware> _logger;
        private readonly IPerformanceService _performanceService;

        public PerformanceMonitoringMiddleware(
            RequestDelegate next,
            ILogger<PerformanceMonitoringMiddleware> logger,
            IPerformanceService performanceService)
        {
            _next = next;
            _logger = logger;
            _performanceService = performanceService;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            var requestPath = context.Request.Path.Value ?? "unknown";
            var method = context.Request.Method;
            var operation = $"{method} {requestPath}";

            // 记录请求开始
            var requestSize = GetRequestSize(context.Request);
            await _performanceService.RecordMetricAsync("request.size", requestSize,
                new Dictionary<string, string>
                {
                    { "method", method },
                    { "path", requestPath }
                });

            // 记录并发请求数
            await _performanceService.RecordMetricAsync("concurrent.requests", 1);

            Exception? exception = null;
            try
            {
                // 执行下一个中间件
                await _next(context);
            }
            catch (Exception ex)
            {
                exception = ex;
                await _performanceService.RecordErrorAsync(operation, ex);
                throw;
            }
            finally
            {
                stopwatch.Stop();

                // 记录响应时间
                var success = exception == null && context.Response.StatusCode < 400;
                await _performanceService.RecordResponseTimeAsync(operation, stopwatch.Elapsed, success);

                // 记录响应大小
                var responseSize = GetResponseSize(context.Response);
                await _performanceService.RecordMetricAsync("response.size", responseSize,
                    new Dictionary<string, string>
                    {
                        { "method", method },
                        { "path", requestPath },
                        { "status_code", context.Response.StatusCode.ToString() }
                    });

                // 记录HTTP状态码
                await _performanceService.RecordMetricAsync("http.status_code", context.Response.StatusCode,
                    new Dictionary<string, string>
                    {
                        { "method", method },
                        { "path", requestPath }
                    });

                // 记录并发请求数减少
                await _performanceService.RecordMetricAsync("concurrent.requests", -1);

                // 记录慢请求
                if (stopwatch.ElapsedMilliseconds > 1000) // 超过1秒的请求
                {
                    _logger.LogWarning("慢请求检测: {Operation} 耗时 {Duration}ms, 状态码: {StatusCode}",
                        operation, stopwatch.ElapsedMilliseconds, context.Response.StatusCode);

                    await _performanceService.RecordMetricAsync("slow.request", stopwatch.ElapsedMilliseconds,
                        new Dictionary<string, string>
                        {
                            { "operation", operation },
                            { "status_code", context.Response.StatusCode.ToString() }
                        });
                }

                // 详细日志记录
                _logger.LogDebug("请求完成: {Operation} | 耗时: {Duration}ms | 状态码: {StatusCode} | 请求大小: {RequestSize}B | 响应大小: {ResponseSize}B",
                    operation, stopwatch.ElapsedMilliseconds, context.Response.StatusCode, requestSize, responseSize);
            }
        }

        private long GetRequestSize(HttpRequest request)
        {
            try
            {
                long size = 0;

                // Headers size
                foreach (var header in request.Headers)
                {
                    size += Encoding.UTF8.GetByteCount($"{header.Key}: {string.Join(", ", header.Value.AsEnumerable())}\r\n");
                }

                // Content length
                if (request.ContentLength.HasValue)
                {
                    size += request.ContentLength.Value;
                }

                return size;
            }
            catch
            {
                return 0;
            }
        }

        private long GetResponseSize(HttpResponse response)
        {
            try
            {
                long size = 0;

                // Headers size
                foreach (var header in response.Headers)
                {
                    size += Encoding.UTF8.GetByteCount($"{header.Key}: {string.Join(", ", header.Value.AsEnumerable())}\r\n");
                }

                // Content length
                if (response.ContentLength.HasValue)
                {
                    size += response.ContentLength.Value;
                }

                return size;
            }
            catch
            {
                return 0;
            }
        }
    }
}
