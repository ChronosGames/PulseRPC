using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace GameApp.AuthServer.Middleware
{
    /// <summary>
    /// 响应缓存中间件 - 缓存GET请求的响应以提升性能
    /// </summary>
    public class ResponseCachingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ResponseCachingMiddleware> _logger;

        private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromMinutes(5);

        // 可缓存的路径模式
        private static readonly string[] CacheablePaths =
        {
            "/api/zones",           // 区服列表 - 相对稳定
            "/health",              // 健康检查
            "/ready"                // 就绪检查
        };

        public ResponseCachingMiddleware(
            RequestDelegate next,
            IMemoryCache cache,
            ILogger<ResponseCachingMiddleware> logger)
        {
            _next = next;
            _cache = cache;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // 只缓存GET请求
            if (context.Request.Method != HttpMethods.Get)
            {
                await _next(context);
                return;
            }

            var requestPath = context.Request.Path.Value?.ToLowerInvariant();

            // 检查是否为可缓存的路径
            if (!IsPathCacheable(requestPath))
            {
                await _next(context);
                return;
            }

            // 生成缓存键
            var cacheKey = GenerateCacheKey(context.Request);

            // 尝试从缓存获取响应
            if (_cache.TryGetValue(cacheKey, out CachedResponse cachedResponse))
            {
                _logger.LogDebug("缓存命中: {Path}", requestPath);

                // 设置缓存头
                context.Response.Headers["X-Cache"] = "HIT";
                context.Response.Headers["X-Cache-Key"] = cacheKey;

                // 写入缓存的响应
                await WriteCachedResponse(context, cachedResponse);
                return;
            }

            _logger.LogDebug("缓存未命中: {Path}", requestPath);
            context.Response.Headers["X-Cache"] = "MISS";
            context.Response.Headers["X-Cache-Key"] = cacheKey;

            // 创建响应流的副本来捕获响应内容
            var originalBodyStream = context.Response.Body;
            using var responseBodyStream = new MemoryStream();
            context.Response.Body = responseBodyStream;

            try
            {
                await _next(context);

                // 只缓存成功的响应
                if (context.Response.StatusCode == 200 && responseBodyStream.Length > 0)
                {
                    var responseContent = responseBodyStream.ToArray();

                    // 创建缓存条目
                    var cacheEntry = new CachedResponse
                    {
                        StatusCode = context.Response.StatusCode,
                        ContentType = context.Response.ContentType ?? "application/json",
                        Content = responseContent,
                        Headers = new Dictionary<string, string>()
                    };

                    // 复制响应头（排除一些不应该缓存的头）
                    foreach (var header in context.Response.Headers)
                    {
                        if (!ShouldSkipHeader(header.Key))
                        {
                            cacheEntry.Headers[header.Key] = string.Join(", ", header.Value.AsEnumerable());
                        }
                    }

                    // 确定缓存时间
                    var cacheDuration = GetCacheDuration(requestPath);

                    // 存储到缓存
                    var cacheOptions = new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = cacheDuration,
                        Size = responseContent.Length,
                        Priority = CacheItemPriority.Normal
                    };

                    _cache.Set(cacheKey, cacheEntry, cacheOptions);

                    _logger.LogDebug("响应已缓存: {Path}, 大小: {Size}B, 过期时间: {Duration}",
                        requestPath, responseContent.Length, cacheDuration);
                }

                // 将响应内容复制回原始流
                responseBodyStream.Seek(0, SeekOrigin.Begin);
                await responseBodyStream.CopyToAsync(originalBodyStream);
            }
            finally
            {
                context.Response.Body = originalBodyStream;
            }
        }

        private bool IsPathCacheable(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            foreach (var cacheablePath in CacheablePaths)
            {
                if (path.StartsWith(cacheablePath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string GenerateCacheKey(HttpRequest request)
        {
            var keyBuilder = new StringBuilder();
            keyBuilder.Append(request.Method);
            keyBuilder.Append(":");
            keyBuilder.Append(request.Path.Value);

            // 包含查询参数
            if (request.QueryString.HasValue)
            {
                keyBuilder.Append(request.QueryString.Value);
            }

            // 包含相关的请求头（如Accept-Language用于国际化）
            if (request.Headers.ContainsKey("Accept-Language"))
            {
                keyBuilder.Append(":lang:");
                keyBuilder.Append(request.Headers["Accept-Language"].ToString());
            }

            // 生成MD5哈希来确保键的长度合理
            using var md5 = MD5.Create();
            var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(keyBuilder.ToString()));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        private TimeSpan GetCacheDuration(string? path)
        {
            return path?.ToLowerInvariant() switch
            {
                "/api/zones" => TimeSpan.FromMinutes(10),    // 区服列表缓存10分钟
                "/health" => TimeSpan.FromMinutes(1),        // 健康检查缓存1分钟
                "/ready" => TimeSpan.FromMinutes(1),         // 就绪检查缓存1分钟
                _ => DefaultCacheDuration
            };
        }

        private bool ShouldSkipHeader(string headerName)
        {
            // 不应该缓存的响应头
            var skipHeaders = new[]
            {
                "date",
                "server",
                "x-request-id",
                "x-correlation-id",
                "set-cookie"
            };

            return skipHeaders.Contains(headerName.ToLowerInvariant());
        }

        private async Task WriteCachedResponse(HttpContext context, CachedResponse cachedResponse)
        {
            context.Response.StatusCode = cachedResponse.StatusCode;
            context.Response.ContentType = cachedResponse.ContentType;

            // 设置缓存的响应头
            foreach (var header in cachedResponse.Headers)
            {
                context.Response.Headers[header.Key] = header.Value;
            }

            // 写入响应内容
            await context.Response.Body.WriteAsync(cachedResponse.Content);
        }

        private class CachedResponse
        {
            public int StatusCode { get; set; }
            public string ContentType { get; set; } = string.Empty;
            public byte[] Content { get; set; } = Array.Empty<byte>();
            public Dictionary<string, string> Headers { get; set; } = new();
        }
    }
}
