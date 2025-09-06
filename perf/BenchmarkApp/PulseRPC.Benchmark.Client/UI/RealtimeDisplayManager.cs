using Microsoft.Extensions.Logging;
using Spectre.Console;
using PulseRPC.Benchmark.Client.Engine;
using PulseRPC.Benchmark.Client.UI.Models;
using PulseRPC.Benchmark.Client.UI.DisplayComponents;
using PulseRPC.Benchmark.Client.Configuration;

namespace PulseRPC.Benchmark.Client.UI;

/// <summary>
/// 实时显示管理器
/// 负责管理整个控制台实时监控界面
/// </summary>
public class RealtimeDisplayManager(
    DisplayConfiguration? configuration = null,
    ILogger<RealtimeDisplayManager>? logger = null)
    : IDisposable
{
    private readonly DisplayConfiguration _configuration = configuration ?? new DisplayConfiguration();
    private readonly DisplayData _displayData = new();

    private readonly HeaderComponent _headerComponent = new();
    private readonly ProgressComponent _progressComponent = new();
    private readonly StatisticsComponent _statisticsComponent = new();
    private readonly ConnectionComponent _connectionComponent = new();
    private readonly SystemResourceComponent _systemResourceComponent = new();
    private readonly LatencyComponent _latencyComponent = new();

    private Layout? _layout;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _displayTask;
    private volatile bool _isRunning;

    // 初始化显示组件

    /// <summary>
    /// 启动实时显示
    /// </summary>
    public async Task StartDisplayAsync(TestConfiguration testConfig, CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            logger?.LogWarning("实时显示已在运行中");
            return;
        }

        try
        {
            _displayData.Configuration = testConfig;
            _displayData.StartTime = DateTime.UtcNow;

            // 检查控制台能力
            if (!ConsoleCapabilities.SupportsAnsi())
            {
                logger?.LogWarning("控制台不支持ANSI，将使用简化显示模式");
                await StartSimpleDisplayAsync(cancellationToken);
                return;
            }

            // 初始化布局
            InitializeLayout();

            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isRunning = true;

            // 启动显示任务
            _displayTask = Task.Run(async () =>
            {
                try
                {
                    await DisplayLoopAsync(_cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    // 正常取消，不记录错误
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "实时显示循环发生错误");
                }
            }, _cancellationTokenSource.Token);

            logger?.LogDebug("实时显示已启动");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "启动实时显示失败");
            throw;
        }
    }

    /// <summary>
    /// 更新测试进度
    /// </summary>
    public void UpdateProgress(TestProgress progress)
    {
        if (!_isRunning) return;

        _displayData.Progress = progress;

        // 更新系统资源信息（这里可以集成真实的系统监控）
        UpdateSystemResources();
    }

    /// <summary>
    /// 更新测试状态
    /// </summary>
    public void UpdateState(TestState state)
    {
        if (!_isRunning) return;

        _displayData.CurrentState = state;
    }

    /// <summary>
    /// 设置错误信息
    /// </summary>
    public void SetError(string errorMessage)
    {
        _displayData.ErrorMessage = errorMessage;
    }

    /// <summary>
    /// 停止实时显示
    /// </summary>
    public async Task StopDisplayAsync()
    {
        if (!_isRunning) return;

        try
        {
            _isRunning = false;
            _cancellationTokenSource?.Cancel();

            if (_displayTask != null)
            {
                await _displayTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            logger?.LogDebug("实时显示已停止");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "停止实时显示时发生错误");
        }
    }

    /// <summary>
    /// 显示完成摘要
    /// </summary>
    public void ShowCompletionSummary(TestResults results)
    {
        AnsiConsole.Clear();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Green)
            .Title("[bold green]🎉 测试完成摘要[/]");

        table.AddColumn("[bold]指标[/]");
        table.AddColumn("[bold]数值[/]");

        table.AddRow("测试场景", _displayData.Configuration.ScenarioName);
        table.AddRow("总耗时", results.TotalDuration.ToString(@"hh\:mm\:ss"));
        table.AddRow("总请求数", results.TotalRequests.ToString("N0"));
        table.AddRow("成功请求", $"{results.SuccessfulRequests:N0} ({results.SuccessRate:P1})");
        table.AddRow("失败请求", results.FailedRequests.ToString("N0"));
        table.AddRow("平均QPS", results.RequestsPerSecond.ToString("N1"));
        table.AddRow("平均延迟", $"{results.AverageLatencyMs:F3} ms");
        table.AddRow("P95 延迟", $"{results.P95LatencyMs:F3} ms");
        table.AddRow("P99 延迟", $"{results.P99LatencyMs:F3} ms");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private void InitializeLayout()
    {
        _layout = new Layout("Root")
            .SplitRows(
                new Layout("Header").Size(3),
                new Layout("Main").SplitRows(
                    new Layout("Progress").Size(4),
                    new Layout("Content").SplitColumns(
                        new Layout("Stats"),
                        new Layout("Right").SplitRows(
                            new Layout("Connection").Size(6),
                            new Layout("Resources").Size(8)
                        )
                    )
                )
            );
    }

    private async Task DisplayLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _isRunning)
        {
            try
            {
                // 更新各个组件
                if (_layout != null)
                {
                    _layout["Header"].Update(_headerComponent.Render(_displayData));
                    _layout["Progress"].Update(_progressComponent.Render(_displayData));
                    _layout["Stats"].Update(_statisticsComponent.Render(_displayData));
                    _layout["Connection"].Update(_connectionComponent.Render(_displayData));
                    _layout["Resources"].Update(_systemResourceComponent.Render(_displayData));

                    // 清屏并渲染布局
                    AnsiConsole.Clear();
                    AnsiConsole.Write(_layout);
                }

                // 按配置的刷新频率等待
                await Task.Delay(_configuration.RefreshIntervalMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "显示循环中发生错误");
                await Task.Delay(1000, cancellationToken); // 错误后等待1秒再继续
            }
        }
    }

    private Task StartSimpleDisplayAsync(CancellationToken cancellationToken)
    {
        // 简化显示模式的实现，用于不支持ANSI的终端
        _isRunning = true;
        _displayTask = Task.Run(async () =>
        {
            var lastUpdate = DateTime.MinValue;
            while (!cancellationToken.IsCancellationRequested && _isRunning)
            {
                if (DateTime.UtcNow - lastUpdate > TimeSpan.FromSeconds(5))
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 请求: {_displayData.Progress.TotalRequests}, " +
                                    $"QPS: {_displayData.Progress.RequestsPerSecond:F1}, " +
                                    $"延迟: {_displayData.Progress.AverageLatencyMs:F3}ms");
                    lastUpdate = DateTime.UtcNow;
                }

                await Task.Delay(1000, cancellationToken);
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    private void UpdateSystemResources()
    {
        // 这里实现系统资源监控的逻辑
        // 为了演示，使用模拟数据
        var random = new Random();
        _displayData.SystemResources.CpuUsagePercent = random.NextDouble() * 50 + 10; // 10-60%
        _displayData.SystemResources.MemoryUsageMB = random.Next(100, 500);
        _displayData.SystemResources.NetworkSendMBps = random.NextDouble() * 100;
        _displayData.SystemResources.NetworkReceiveMBps = random.NextDouble() * 100;

        // 获取实际的GC和线程信息
        _displayData.SystemResources.GCCollectionCount = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);

        ThreadPool.GetAvailableThreads(out var workerThreads, out var completionPortThreads);
        _displayData.SystemResources.WorkerThreads = workerThreads;
        _displayData.SystemResources.CompletionPortThreads = completionPortThreads;
    }

    public void Dispose()
    {
        _isRunning = false;
        _cancellationTokenSource?.Cancel();
        _displayTask?.Wait(TimeSpan.FromSeconds(5));
        _cancellationTokenSource?.Dispose();
    }
}

/// <summary>
/// 控制台能力检查
/// </summary>
public static class ConsoleCapabilities
{
    /// <summary>
    /// 检查是否支持ANSI颜色
    /// </summary>
    public static bool SupportsAnsi()
    {
        try
        {
            // 基本的ANSI支持检查
            return !Console.IsOutputRedirected &&
                   Environment.GetEnvironmentVariable("TERM") != null ||
                   Environment.GetEnvironmentVariable("WT_SESSION") != null || // Windows Terminal
                   Environment.OSVersion.Platform == PlatformID.Unix;
        }
        catch
        {
            return false;
        }
    }
}
