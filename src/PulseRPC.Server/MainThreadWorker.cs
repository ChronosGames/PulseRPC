using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Server;

public class MainThreadWorker(
    HandlerThreadPoolManager threadPoolManager,
    ILogger<MainThreadWorker> logger)
    : IHostedService, IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _backgroundTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // 初始化线程池管理器
        threadPoolManager.Initialize();

        // 启动任务处理循环
        _backgroundTask = Task.Run(async () => {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    // 处理队列中的任务
                    threadPoolManager.ProcessTasks(20);

                    // 小睡一会以避免CPU占用过高
                    await Task.Delay(16, _cts.Token);
                }
                catch (OperationCanceledException) when (_cts.Token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "任务处理器处理任务时出错");
                }
            }
        }, _cts.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_backgroundTask != null)
        {
            if (_cts != null)
            {
                await _cts.CancelAsync();
            }

            try
            {
                await _backgroundTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (TimeoutException)
            {
                logger.LogWarning("任务处理器关闭超时");
            }
        }
    }

    public void Dispose() => _cts?.Dispose();
}
