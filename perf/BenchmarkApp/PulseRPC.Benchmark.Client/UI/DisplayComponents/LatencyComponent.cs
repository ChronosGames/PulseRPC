using Spectre.Console;
using Spectre.Console.Rendering;
using PulseRPC.Benchmark.Client.UI.Models;

namespace PulseRPC.Benchmark.Client.UI.DisplayComponents;

/// <summary>
/// 延迟统计显示组件
/// </summary>
public class LatencyComponent
{
    /// <summary>
    /// 渲染延迟统计区域
    /// </summary>
    public IRenderable Render(DisplayData data)
    {
        // 这个组件当前未在主布局中使用，但为了完整性保留
        var latencyChart = new Panel(
            new Text("延迟分布图表（未实现）", new Style(Color.Grey)))
        {
            Header = new PanelHeader("⚡ 延迟分析"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Green)
        };

        return latencyChart;
    }
} 