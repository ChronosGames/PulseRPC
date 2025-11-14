using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Server;

namespace DistributedGameApp.Infrastructure.Hosting;

/// <summary>
/// 命名 PulseRPC 服务器托管服务
/// 负责启动和停止所有已注册的命名服务器实例
/// </summary>
public sealed class NamedPulseServersHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NamedPulseServersHostedService> _logger;
    private readonly List<INamedPulseServer> _servers = new();

    public NamedPulseServersHostedService(
        IServiceProvider serviceProvider,
        ILogger<NamedPulseServersHostedService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("正在启动所有命名 PulseRPC 服务器...");

        try
        {
            // 获取所有已注册的命名服务器
            // 注意：需要使用 GetKeyedServices 来获取所有带键的服务
            // 但由于我们使用了不同的键名（External、Internal），需要逐个获取
            var serverNames = new[] { "External", "Internal" };

            foreach (var serverName in serverNames)
            {
                try
                {
                    var server = _serviceProvider.GetKeyedService<INamedPulseServer>(serverName);
                    if (server != null)
                    {
                        _logger.LogInformation("启动 PulseRPC 服务器: {ServerName}", serverName);
                        await server.StartAsync(cancellationToken);
                        _servers.Add(server);
                        _logger.LogInformation("PulseRPC 服务器 {ServerName} 启动成功", serverName);
                    }
                    else
                    {
                        _logger.LogDebug("未找到名为 {ServerName} 的 PulseRPC 服务器", serverName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "启动 PulseRPC 服务器 {ServerName} 失败", serverName);
                    // 继续启动其他服务器，不中断
                }
            }

            if (_servers.Count > 0)
            {
                _logger.LogInformation("成功启动 {Count} 个 PulseRPC 服务器", _servers.Count);
            }
            else
            {
                _logger.LogWarning("没有找到任何命名 PulseRPC 服务器需要启动");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动命名 PulseRPC 服务器时发生错误");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("正在停止所有命名 PulseRPC 服务器...");

        try
        {
            foreach (var server in _servers)
            {
                try
                {
                    _logger.LogInformation("停止 PulseRPC 服务器: {ServerName}", server.ServerName);
                    await server.StopAsync(cancellationToken);
                    _logger.LogInformation("PulseRPC 服务器 {ServerName} 已停止", server.ServerName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "停止 PulseRPC 服务器 {ServerName} 失败", server.ServerName);
                    // 继续停止其他服务器
                }
            }

            _servers.Clear();
            _logger.LogInformation("所有命名 PulseRPC 服务器已停止");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止命名 PulseRPC 服务器时发生错误");
            throw;
        }
    }
}
