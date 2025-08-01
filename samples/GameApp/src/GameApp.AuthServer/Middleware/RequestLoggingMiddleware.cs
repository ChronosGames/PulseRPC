using System.Text;

namespace GameApp.AuthServer.Middleware;

/// <summary>
/// 请求日志中间件
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 记录请求开始时间
        var startTime = DateTime.UtcNow;
        var requestId = context.TraceIdentifier;

        // 记录请求信息
        await LogRequestAsync(context, requestId);

        // 捕获原始响应体流
        var originalResponseBodyStream = context.Response.Body;

        try
        {
            using var responseBodyStream = new MemoryStream();
            context.Response.Body = responseBodyStream;

            // 执行下一个中间件
            await _next(context);

            // 记录响应信息
            await LogResponseAsync(context, requestId, startTime, responseBodyStream);

            // 将响应体写回原始流
            responseBodyStream.Seek(0, SeekOrigin.Begin);
            await responseBodyStream.CopyToAsync(originalResponseBodyStream);
        }
        finally
        {
            context.Response.Body = originalResponseBodyStream;
        }
    }

    private async Task LogRequestAsync(HttpContext context, string requestId)
    {
        try
        {
            var request = context.Request;

            // 基本请求信息
            var requestInfo = new
            {
                RequestId = requestId,
                Method = request.Method,
                Path = request.Path.Value,
                QueryString = request.QueryString.Value,
                UserAgent = request.Headers.UserAgent.ToString(),
                IP = GetRealClientIP(context),
                ContentType = request.ContentType,
                ContentLength = request.ContentLength
            };

            _logger.LogInformation("Request started: {@RequestInfo}", requestInfo);

            // 记录请求体（只对POST/PUT等方法，且排除敏感路径）
            if (ShouldLogRequestBody(request))
            {
                var requestBody = await ReadRequestBodyAsync(request);
                if (!string.IsNullOrEmpty(requestBody))
                {
                    // 屏蔽敏感信息
                    var sanitizedBody = SanitizeRequestBody(requestBody, request.Path);
                    _logger.LogDebug("Request body for {RequestId}: {Body}", requestId, sanitizedBody);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log request information for {RequestId}", requestId);
        }
    }

    private async Task LogResponseAsync(HttpContext context, string requestId, DateTime startTime, MemoryStream responseBodyStream)
    {
        try
        {
            var endTime = DateTime.UtcNow;
            var duration = endTime - startTime;

            // 读取响应体
            responseBodyStream.Seek(0, SeekOrigin.Begin);
            var responseBody = await new StreamReader(responseBodyStream).ReadToEndAsync();

            var responseInfo = new
            {
                RequestId = requestId,
                StatusCode = context.Response.StatusCode,
                ContentType = context.Response.ContentType,
                ContentLength = responseBodyStream.Length,
                Duration = duration.TotalMilliseconds
            };

            // 根据状态码选择日志级别
            if (context.Response.StatusCode >= 400)
            {
                _logger.LogWarning("Request completed with error: {@ResponseInfo}", responseInfo);

                // 记录错误响应体
                if (context.Response.StatusCode >= 500)
                {
                    _logger.LogError("Error response body for {RequestId}: {Body}", requestId, responseBody);
                }
            }
            else
            {
                _logger.LogInformation("Request completed successfully: {@ResponseInfo}", responseInfo);
            }

            // 记录慢请求
            if (duration.TotalMilliseconds > 1000) // 超过1秒
            {
                _logger.LogWarning("Slow request detected: {RequestId}, Duration: {Duration}ms, Path: {Path}",
                    requestId, duration.TotalMilliseconds, context.Request.Path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log response information for {RequestId}", requestId);
        }
    }

    private bool ShouldLogRequestBody(HttpRequest request)
    {
        // 只记录POST、PUT、PATCH方法的请求体
        if (request.Method != "POST" && request.Method != "PUT" && request.Method != "PATCH")
        {
            return false;
        }

        // 排除文件上传等大型请求
        if (request.ContentLength > 10240) // 10KB
        {
            return false;
        }

        // 只记录JSON和表单数据
        return request.ContentType?.Contains("application/json") == true ||
               request.ContentType?.Contains("application/x-www-form-urlencoded") == true;
    }

    private async Task<string> ReadRequestBodyAsync(HttpRequest request)
    {
        try
        {
            request.EnableBuffering();
            request.Body.Position = 0;

            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync();

            request.Body.Position = 0;
            return body;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read request body");
            return string.Empty;
        }
    }

    private string SanitizeRequestBody(string requestBody, PathString path)
    {
        // 屏蔽敏感信息
        if (path.Value?.Contains("/auth/") == true)
        {
            // 屏蔽密码等敏感字段
            requestBody = System.Text.RegularExpressions.Regex.Replace(
                requestBody,
                @"""password""\s*:\s*""[^""]*""",
                @"""password"":""***""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            requestBody = System.Text.RegularExpressions.Regex.Replace(
                requestBody,
                @"""confirmPassword""\s*:\s*""[^""]*""",
                @"""confirmPassword"":""***""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return requestBody;
    }

    private string GetRealClientIP(HttpContext context)
    {
        // 依次检查各种可能的客户端IP头
        var headers = new[]
        {
            "X-Forwarded-For",
            "X-Real-IP",
            "CF-Connecting-IP", // Cloudflare
            "X-Client-IP"
        };

        foreach (var header in headers)
        {
            var value = context.Request.Headers[header].FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
            {
                // X-Forwarded-For 可能包含多个IP，取第一个
                return value.Split(',')[0].Trim();
            }
        }

        // 回退到连接的远程IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
    }
}
