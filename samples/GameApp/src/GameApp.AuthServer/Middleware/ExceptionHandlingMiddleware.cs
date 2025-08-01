using System.Net;
using System.Text.Json;
using GameApp.AuthServer.Models;

namespace GameApp.AuthServer.Middleware;

/// <summary>
/// 全局异常处理中间件
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var requestId = context.TraceIdentifier;

        _logger.LogError(exception, "Unhandled exception occurred. RequestId: {RequestId}", requestId);

        var (statusCode, message, details) = exception switch
        {
            ArgumentException argEx => (HttpStatusCode.BadRequest, "请求参数无效", argEx.Message),
            ArgumentNullException nullEx => (HttpStatusCode.BadRequest, "必需参数不能为空", nullEx.ParamName ?? "Unknown parameter"),
            InvalidOperationException opEx => (HttpStatusCode.BadRequest, opEx.Message, "Operation not allowed"),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "访问未授权", "Authentication required"),
            NotImplementedException => (HttpStatusCode.NotImplemented, "功能尚未实现", "Feature not implemented"),
            TimeoutException => (HttpStatusCode.RequestTimeout, "请求超时", "Request timed out"),
            _ => (HttpStatusCode.InternalServerError, "系统内部错误", "An unexpected error occurred")
        };

        // 构建错误响应
        var response = new ApiResponse<object>
        {
            Success = false,
            Message = message,
            Data = null,
            Errors = new List<string> { details },
            RequestId = requestId,
            Timestamp = DateTime.UtcNow
        };

        // 序列化响应
        var jsonResponse = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        // 设置响应
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(jsonResponse);
    }
}
