using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;

namespace DistributedGameApp.Infrastructure.Health;

/// <summary>
/// HTTP 健康检查服务
/// 提供一个轻量级的 HTTP 端点用于 Consul 健康检查
/// </summary>
public class HttpHealthCheckService : BackgroundService
{
    private readonly ILogger<HttpHealthCheckService> _logger;
    private readonly int _port;
    private HttpListener? _listener;

    public HttpHealthCheckService(
        ILogger<HttpHealthCheckService> logger,
        int port = 5000)
    {
        _logger = logger;
        _port = port;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{_port}/");
            _listener.Start();

            _logger.LogInformation("HTTP 健康检查服务已启动，监听端口: {Port}", _port);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequestAsync(context, stoppingToken), stoppingToken);
                }
                catch (HttpListenerException ex) when (ex.ErrorCode == 995) // Operation aborted
                {
                    // 正常关闭
                    break;
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "处理健康检查请求异常");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP 健康检查服务启动失败");
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;

            _logger.LogDebug("收到健康检查请求: {Method} {Url} from {RemoteEndpoint}",
                request.HttpMethod, request.Url?.AbsolutePath, request.RemoteEndPoint);

            // 处理 /health 端点
            if (request.Url?.AbsolutePath == "/health")
            {
                var healthStatus = new
                {
                    status = "healthy",
                    timestamp = DateTime.UtcNow,
                    uptime = TimeSpan.FromMilliseconds(Environment.TickCount64)
                };

                var json = JsonSerializer.Serialize(healthStatus);
                var buffer = Encoding.UTF8.GetBytes(json);

                response.StatusCode = 200;
                response.ContentType = "application/json; charset=utf-8";
                response.ContentLength64 = buffer.Length;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
            }
            else
            {
                // 其他路径返回 404
                response.StatusCode = 404;
                var buffer = Encoding.UTF8.GetBytes("Not Found");
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
            }

            response.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理健康检查响应异常");
            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch
            {
                // Ignore
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("正在停止 HTTP 健康检查服务...");

        if (_listener != null)
        {
            _listener.Stop();
            _listener.Close();
        }

        await base.StopAsync(cancellationToken);

        _logger.LogInformation("HTTP 健康检查服务已停止");
    }

    public override void Dispose()
    {
        _listener?.Close();
        base.Dispose();
    }
}
