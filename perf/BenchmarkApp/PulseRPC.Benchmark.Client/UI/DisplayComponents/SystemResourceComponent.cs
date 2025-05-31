using System;
using Spectre.Console;
using Spectre.Console.Rendering;
using PulseRPC.Benchmark.Client.UI.Models;

namespace PulseRPC.Benchmark.Client.UI.DisplayComponents;

/// <summary>
/// 系统资源监控显示组件
/// </summary>
public class SystemResourceComponent
{
    /// <summary>
    /// 渲染系统资源区域
    /// </summary>
    public IRenderable Render(DisplayData data)
    {
        var resources = data.SystemResources;

        var cpuProgress = new ProgressBarColumn();
        var memoryProgress = new ProgressBarColumn();

        var resourcePanel = new Panel(
            new Rows(
                new Text($"CPU: [bold]{resources.CpuUsagePercent:F1}%[/] " + GenerateProgressBar(resources.CpuUsagePercent)),
                new Text($"内存: [bold]{resources.MemoryUsageMB} MB[/] " + GenerateProgressBar(Math.Min(resources.MemoryUsageMB / 1024.0 * 100, 100))),
                Text.Empty,
                new Text($"📤 网络发送: [yellow]{resources.NetworkSendMBps:F1} MB/s[/]"),
                new Text($"📥 网络接收: [yellow]{resources.NetworkReceiveMBps:F1} MB/s[/]"),
                new Text($"🧹 GC回收: [grey]{resources.GCCollectionCount}[/]"),
                new Text($"🧵 工作线程: [grey]{resources.WorkerThreads}[/]")
            ))
        {
            Header = new PanelHeader("💾 系统资源"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Yellow)
        };

        return resourcePanel;
    }

    private static string GenerateProgressBar(double percentage)
    {
        const int barLength = 20;
        var filled = (int)(percentage / 100.0 * barLength);
        var bar = new string('█', filled) + new string('░', barLength - filled);
        return $"[blue]{bar}[/]";
    }
} 