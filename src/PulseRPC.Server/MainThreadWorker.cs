using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Server;
public class MainThreadWorker : IHostedService, IDisposable
{
    private readonly HandlerThreadPoolManager _threadPoolManager;
    private readonly ILogger<MainThreadWorker> _logger;
    private CancellationTokenSource? _cts;
    private Task? _backgroundTask;

    public MainThreadWorker(
        HandlerThreadPoolManager threadPoolManager,
        ILogger<MainThreadWorker> logger)
    {
        _threadPoolManager = threadPoolManager;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // 初始化线程池管理器
        _threadPoolManager.Initialize();

        // 启动任务处理循环
        _backgroundTask = Task.Run(async () => {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    // 处理队列中的任务
                    _threadPoolManager.ProcessTasks(20);

                    // 小睡一会以避免CPU占用过高
                    await Task.Delay(16, _cts.Token);
                }
                catch (OperationCanceledException) when (_cts.Token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "任务处理器处理任务时出错");
                }
            }
        }, _cts.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_backgroundTask != null)
        {
            _cts?.Cancel();

            try
            {
                await _backgroundTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("任务处理器关闭超时");
            }
        }
    }

    public void Dispose()
    {
        _cts?.Dispose();
    }
}
