using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using PulseRPC.Server.Observability;

namespace DistributedGameApp.Infrastructure.Health;

/// <summary>
/// 托管服务：运行内嵌的 Kestrel HTTP 服务器，用于健康检查和 Prometheus metrics
/// </summary>
public sealed class HealthEndpointHostedService : IHostedService, IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly HttpEndpointOptions _options;
    private readonly ILogger<HealthEndpointHostedService> _logger;
    private WebApplication? _app;

    public HealthEndpointHostedService(
        IServiceProvider serviceProvider,
        IOptions<HttpEndpointOptions> options,
        ILogger<HealthEndpointHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("HTTP endpoint is disabled");
            return;
        }

        try
        {
            var builder = WebApplication.CreateSlimBuilder();

            // 配置 Kestrel
            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                serverOptions.ListenAnyIP(_options.Port);
            });

            // 配置日志（使用父服务的日志配置）
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();

            // 配置 OpenTelemetry Metrics
            if (_options.EnableMetrics)
            {
                builder.Services.AddOpenTelemetry()
                    .WithMetrics(metrics =>
                    {
                        // 添加 PulseRPC 服务指标
                        metrics.AddMeter(ServiceMetrics.MeterName);

                        // 添加 .NET 运行时指标
                        metrics.AddRuntimeInstrumentation();

                        // 添加 Prometheus 导出器
                        metrics.AddPrometheusExporter();
                    });
            }

            _app = builder.Build();

            // 健康检查端点
            _app.MapGet(_options.HealthPath, async (HttpContext context) =>
            {
                var healthProvider = _serviceProvider.GetService<IHealthCheckProvider>();
                if (healthProvider == null)
                {
                    return Results.Ok(new
                    {
                        status = "healthy",
                        timestamp = DateTime.UtcNow,
                        message = "No health check provider configured"
                    });
                }

                try
                {
                    var result = await healthProvider.CheckHealthAsync(context.RequestAborted);

                    if (result.IsHealthy)
                    {
                        return Results.Ok(new
                        {
                            status = "healthy",
                            timestamp = DateTime.UtcNow,
                            details = result.Details
                        });
                    }
                    else
                    {
                        return Results.Json(
                            new
                            {
                                status = "unhealthy",
                                reason = result.Status,
                                timestamp = DateTime.UtcNow,
                                details = result.Details
                            },
                            statusCode: 503);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Health check failed with exception");
                    return Results.Json(
                        new
                        {
                            status = "unhealthy",
                            reason = "HealthCheckException",
                            timestamp = DateTime.UtcNow,
                            error = ex.Message
                        },
                        statusCode: 503);
                }
            });

            // Prometheus metrics 端点
            if (_options.EnableMetrics)
            {
                _app.UseOpenTelemetryPrometheusScrapingEndpoint(_options.MetricsPath);
            }

            // 简单的 readiness 端点
            _app.MapGet("/ready", () => Results.Ok(new { status = "ready", timestamp = DateTime.UtcNow }));

            _logger.LogInformation(
                "Starting HTTP endpoint on http://{Host}:{Port} (Health: {HealthPath}, Metrics: {MetricsPath})",
                _options.Host,
                _options.Port,
                _options.HealthPath,
                _options.EnableMetrics ? _options.MetricsPath : "disabled");

            await _app.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start HTTP endpoint on port {Port}", _options.Port);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_app != null)
        {
            _logger.LogInformation("Stopping HTTP endpoint");
            await _app.StopAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app != null)
        {
            await _app.DisposeAsync();
            _app = null;
        }
    }
}