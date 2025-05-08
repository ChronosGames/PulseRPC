using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Server;

/// <summary>
/// TcpServer的托管服务实现
/// </summary>
public class TcpServerHostedService : IHostedService
{
    private readonly TcpServer _server;
    private readonly ILogger<TcpServerHostedService> _logger;
    private Task? _serverTask;
    private CancellationTokenSource? _stoppingCts;

    /// <summary>
    /// 初始化TcpServer托管服务
    /// </summary>
    /// <param name="server">TCP服务器实例</param>
    /// <param name="logger">日志记录器</param>
    public TcpServerHostedService(TcpServer server, ILogger<TcpServerHostedService> logger)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 启动托管服务
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("启动TcpServer托管服务");

        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // 启动服务器，但不等待它完成
        _serverTask = RunServerAsync(_stoppingCts.Token);

        // 如果任务已完成，它可能因错误而完成
        if (_serverTask.IsCompleted)
        {
            return _serverTask;
        }

        // 否则返回已完成的任务，指示服务已成功启动
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止托管服务
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_serverTask == null)
        {
            return;
        }

        _logger.LogInformation("停止TcpServer托管服务");

        try
        {
            // 尝试取消服务器任务
            if (_stoppingCts != null)
            {
                // 通知服务器停止
                await _server.StopAsync();

                // 取消令牌
                await _stoppingCts.CancelAsync();
            }

            // 等待服务器任务完成
            var timeout = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            var completedTask = await Task.WhenAny(_serverTask, timeout);

            if (completedTask == timeout)
            {
                _logger.LogWarning("停止TcpServer时超时");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止TcpServer时发生错误");
        }
        finally
        {
            // 处理所有资源
            _stoppingCts?.Dispose();
        }
    }

    /// <summary>
    /// 运行服务器并处理异常
    /// </summary>
    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("TcpServer开始运行");
            await _server.StartAsync();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("TcpServer被取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TcpServer运行时发生未处理的异常");
            throw;
        }
    }
}
