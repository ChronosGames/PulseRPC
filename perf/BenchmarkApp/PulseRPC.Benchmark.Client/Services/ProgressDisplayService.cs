using Microsoft.Extensions.Logging;

namespace PulseRPC.Benchmark.Client.Services;

/// <summary>
/// 进度显示服务
/// 负责在控制台显示测试进度和实时状态
/// </summary>
public class ProgressDisplayService : IDisposable
{
    private readonly ILogger<ProgressDisplayService> _logger;
    private readonly Timer _refreshTimer;
    private readonly Lock _lockObject = new();

    private bool _isDisplaying;
    private DateTime _startTime;
    private ProgressInfo? _currentProgress;
    private string _lastDisplayText = string.Empty;

    /// <summary>
    /// 构造函数
    /// </summary>
    public ProgressDisplayService(ILogger<ProgressDisplayService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _refreshTimer = new Timer(RefreshDisplay, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// 开始显示进度
    /// </summary>
    /// <param name="testName">测试名称</param>
    /// <param name="estimatedDuration">预估持续时间</param>
    public void StartDisplay(string testName, TimeSpan estimatedDuration)
    {
        lock (_lockObject)
        {
            if (_isDisplaying)
            {
                StopDisplay();
            }

            _isDisplaying = true;
            _startTime = DateTime.UtcNow;
            _currentProgress = new ProgressInfo
            {
                TestName = testName,
                EstimatedDuration = estimatedDuration,
                Phase = TestPhase.Connecting
            };

            // 清屏并显示初始状态
            Console.Clear();
            Console.WriteLine($"🚀 开始执行测试: {testName}");
            Console.WriteLine($"⏱️  预计持续时间: {estimatedDuration:hh\\:mm\\:ss}");
            Console.WriteLine();

            // 启动定时刷新（每500ms更新一次）
            _refreshTimer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
        }

        _logger.LogInformation("开始显示进度: {TestName}", testName);
    }

    /// <summary>
    /// 停止显示进度
    /// </summary>
    public void StopDisplay()
    {
        lock (_lockObject)
        {
            if (!_isDisplaying)
            {
                return;
            }

            _isDisplaying = false;
            _refreshTimer.Change(Timeout.Infinite, Timeout.Infinite);

            // 显示最终状态
            if (_currentProgress != null)
            {
                DisplayFinalResult();
            }
        }

        _logger.LogInformation("停止显示进度");
    }

    /// <summary>
    /// 更新进度信息
    /// </summary>
    /// <param name="progress">进度信息</param>
    public void UpdateProgress(ProgressInfo progress)
    {
        lock (_lockObject)
        {
            if (!_isDisplaying) return;

            _currentProgress = progress;
            _currentProgress.ElapsedTime = DateTime.UtcNow - _startTime;
        }
    }

    /// <summary>
    /// 更新测试阶段
    /// </summary>
    /// <param name="phase">测试阶段</param>
    /// <param name="message">可选的状态消息</param>
    public void UpdatePhase(TestPhase phase, string? message = null)
    {
        lock (_lockObject)
        {
            if (!_isDisplaying || _currentProgress == null) return;

            _currentProgress.Phase = phase;
            _currentProgress.PhaseMessage = message;
            _currentProgress.ElapsedTime = DateTime.UtcNow - _startTime;
        }
    }

    /// <summary>
    /// 定时刷新显示
    /// </summary>
    private void RefreshDisplay(object? state)
    {
        lock (_lockObject)
        {
            if (!_isDisplaying || _currentProgress == null) return;

            try
            {
                var displayText = BuildDisplayText(_currentProgress);

                // 只在内容变化时更新显示
                if (displayText != _lastDisplayText)
                {
                    // 移动光标到开始位置并清除之前的内容
                    Console.SetCursorPosition(0, 3);

                    // 清除之前的显示内容
                    for (int i = 0; i < _lastDisplayText.Split('\n').Length; i++)
                    {
                        Console.WriteLine(new string(' ', Console.WindowWidth - 1));
                    }

                    // 重新定位并显示新内容
                    Console.SetCursorPosition(0, 3);
                    Console.Write(displayText);

                    _lastDisplayText = displayText;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "刷新进度显示时发生错误");
            }
        }
    }

    /// <summary>
    /// 构建显示文本
    /// </summary>
    private string BuildDisplayText(ProgressInfo progress)
    {
        var text = new System.Text.StringBuilder();

        // 阶段和状态
        var phaseIcon = GetPhaseIcon(progress.Phase);
        var phaseText = GetPhaseText(progress.Phase);
        text.AppendLine($"{phaseIcon} {phaseText}");

        if (!string.IsNullOrEmpty(progress.PhaseMessage))
        {
            text.AppendLine($"   {progress.PhaseMessage}");
        }

        text.AppendLine();

        // 时间信息
        text.AppendLine($"⏱️  运行时间: {progress.ElapsedTime:hh\\:mm\\:ss}");

        if (progress.EstimatedDuration > TimeSpan.Zero)
        {
            var progressPercent = Math.Min(100, (progress.ElapsedTime.TotalSeconds / progress.EstimatedDuration.TotalSeconds) * 100);
            var remainingTime = progress.EstimatedDuration - progress.ElapsedTime;

            text.AppendLine($"📊 完成进度: {progressPercent:F1}%");
            text.AppendLine($"⏳ 剩余时间: {(remainingTime > TimeSpan.Zero ? remainingTime.ToString(@"hh\:mm\:ss") : "00:00:00")}");

            // 进度条
            var progressBar = BuildProgressBar(progressPercent);
            text.AppendLine($"▓ {progressBar}");
        }

        text.AppendLine();

        // 统计信息
        if (progress.TotalRequests > 0)
        {
            text.AppendLine("📈 实时统计:");
            text.AppendLine($"   总请求数: {progress.TotalRequests:N0}");
            text.AppendLine($"   成功请求: {progress.SuccessfulRequests:N0}");
            text.AppendLine($"   失败请求: {progress.FailedRequests:N0}");
            text.AppendLine($"   成功率: {(progress.TotalRequests > 0 ? (double)progress.SuccessfulRequests / progress.TotalRequests : 0):P2}");
            text.AppendLine($"   当前QPS: {progress.CurrentQPS:F2}");

            if (progress.AverageLatencyMs > 0)
            {
                text.AppendLine($"   平均延迟: {progress.AverageLatencyMs:F2} ms");
            }
        }

        // 连接信息
        if (progress.ActiveConnections > 0)
        {
            text.AppendLine($"🔗 活跃连接: {progress.ActiveConnections}");
        }

        // 错误信息
        if (!string.IsNullOrEmpty(progress.LastError))
        {
            text.AppendLine();
            text.AppendLine($"❌ 最近错误: {progress.LastError}");
        }

        return text.ToString();
    }

    /// <summary>
    /// 构建进度条
    /// </summary>
    private string BuildProgressBar(double percent)
    {
        const int barWidth = 50;
        var filled = (int)(percent / 100.0 * barWidth);
        var bar = new string('█', filled) + new string('░', barWidth - filled);
        return $"{bar} {percent:F1}%";
    }

    /// <summary>
    /// 获取阶段图标
    /// </summary>
    private string GetPhaseIcon(TestPhase phase)
    {
        return phase switch
        {
            TestPhase.Connecting => "🔗",
            TestPhase.WarmingUp => "🔥",
            TestPhase.Running => "▶️",
            TestPhase.Collecting => "📊",
            TestPhase.Completed => "✅",
            TestPhase.Failed => "❌",
            TestPhase.Cancelled => "⏸️",
            _ => "❓"
        };
    }

    /// <summary>
    /// 获取阶段文本
    /// </summary>
    private string GetPhaseText(TestPhase phase)
    {
        return phase switch
        {
            TestPhase.Connecting => "建立连接中...",
            TestPhase.WarmingUp => "预热阶段",
            TestPhase.Running => "执行测试中",
            TestPhase.Collecting => "收集结果中...",
            TestPhase.Completed => "测试完成",
            TestPhase.Failed => "测试失败",
            TestPhase.Cancelled => "测试已取消",
            _ => "未知状态"
        };
    }

    /// <summary>
    /// 显示最终结果
    /// </summary>
    private void DisplayFinalResult()
    {
        if (_currentProgress == null) return;

        Console.WriteLine();
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

        var finalIcon = _currentProgress.Phase == TestPhase.Completed ? "✅" :
                       _currentProgress.Phase == TestPhase.Failed ? "❌" : "⏸️";
        var finalText = GetPhaseText(_currentProgress.Phase);

        Console.WriteLine($"{finalIcon} {finalText}");
        Console.WriteLine($"⏱️  总耗时: {_currentProgress.ElapsedTime:hh\\:mm\\:ss}");

        if (_currentProgress.TotalRequests > 0)
        {
            Console.WriteLine($"📊 总请求数: {_currentProgress.TotalRequests:N0}");
            Console.WriteLine($"✅ 成功请求: {_currentProgress.SuccessfulRequests:N0}");
            Console.WriteLine($"❌ 失败请求: {_currentProgress.FailedRequests:N0}");
            Console.WriteLine($"📈 平均QPS: {(_currentProgress.TotalRequests / Math.Max(_currentProgress.ElapsedTime.TotalSeconds, 1)):F2}");
        }

        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        StopDisplay();
        _refreshTimer?.Dispose();
    }
}

/// <summary>
/// 进度信息
/// </summary>
public class ProgressInfo
{
    public string TestName { get; set; } = string.Empty;
    public TestPhase Phase { get; set; }
    public string? PhaseMessage { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public TimeSpan EstimatedDuration { get; set; }

    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public double CurrentQPS { get; set; }
    public double AverageLatencyMs { get; set; }

    public int ActiveConnections { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// 测试阶段枚举
/// </summary>
public enum TestPhase
{
    Connecting,
    WarmingUp,
    Running,
    Collecting,
    Completed,
    Failed,
    Cancelled
}
