using System;
using Spectre.Console;
using Spectre.Console.Rendering;
using PulseRPC.Benchmark.Client.Engine;
using PulseRPC.Benchmark.Client.UI.Models;

namespace PulseRPC.Benchmark.Client.UI.DisplayComponents;

/// <summary>
/// 进度条和时间显示组件
/// </summary>
public class ProgressComponent
{
    /// <summary>
    /// 渲染进度区域
    /// </summary>
    public IRenderable Render(DisplayData data)
    {
        var stateEmoji = GetStateEmoji(data.CurrentState);
        var stateText = GetStateText(data.CurrentState);
        var stateColor = GetStateColor(data.CurrentState);

        var statusPanel = new Panel(
            new Rows(
                new Text($"{stateEmoji} {stateText}", new Style(stateColor)),
                new Text($"[{data.Progress.ElapsedTime:hh\\:mm\\:ss} / {TimeSpan.FromSeconds(data.Configuration.DurationSeconds):hh\\:mm\\:ss}]", 
                        new Style(Color.Grey))
            ))
        {
            Header = new PanelHeader("执行状态"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(stateColor)
        };

        var progressBar = GenerateProgressBarText(data.ProgressPercentage);

        return new Rows(statusPanel, new Text(progressBar));
    }

    private static string GetStateEmoji(TestState state) => state switch
    {
        TestState.Idle => "⏸️",
        TestState.Starting => "🚀",
        TestState.Connecting => "🔗",
        TestState.WarmingUp => "🔥",
        TestState.Running => "▶️",
        TestState.Collecting => "📊",
        TestState.Completed => "✅",
        TestState.Cancelled => "⏹️",
        TestState.Failed => "❌",
        _ => "❓"
    };

    private static string GetStateText(TestState state) => state switch
    {
        TestState.Idle => "空闲",
        TestState.Starting => "启动中",
        TestState.Connecting => "连接中",
        TestState.WarmingUp => "预热中",
        TestState.Running => "执行测试中",
        TestState.Collecting => "收集结果中",
        TestState.Completed => "测试完成",
        TestState.Cancelled => "测试取消",
        TestState.Failed => "测试失败",
        _ => "未知状态"
    };

    private static Color GetStateColor(TestState state) => state switch
    {
        TestState.Idle => Color.Grey,
        TestState.Starting => Color.Yellow,
        TestState.Connecting => Color.Blue,
        TestState.WarmingUp => Color.Orange1,
        TestState.Running => Color.Green,
        TestState.Collecting => Color.Cyan1,
        TestState.Completed => Color.Green,
        TestState.Cancelled => Color.Orange1,
        TestState.Failed => Color.Red,
        _ => Color.Grey
    };

    private static string GenerateProgressBarText(double percentage)
    {
        const int barLength = 50;
        var filled = (int)(percentage / 100.0 * barLength);
        var bar = new string('█', filled) + new string('░', barLength - filled);
        return $"[blue]{bar}[/] [bold]{percentage:F1}%[/]";
    }
} 