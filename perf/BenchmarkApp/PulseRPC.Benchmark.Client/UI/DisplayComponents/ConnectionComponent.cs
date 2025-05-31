using Spectre.Console;
using Spectre.Console.Rendering;
using PulseRPC.Benchmark.Client.UI.Models;

namespace PulseRPC.Benchmark.Client.UI.DisplayComponents;

/// <summary>
/// 连接状态显示组件
/// </summary>
public class ConnectionComponent
{
    /// <summary>
    /// 渲染连接状态区域
    /// </summary>
    public IRenderable Render(DisplayData data)
    {
        var connectionPanel = new Panel(
            new Rows(
                new Text($"🔗 活跃连接: [bold green]{data.Progress.ActiveConnections}[/]"),
                new Text($"🏊 连接池状态: [bold green]健康[/]"),
                new Text($"📡 目标服务器: [grey]{data.Configuration.ServerAddress}[/]")
            ))
        {
            Header = new PanelHeader("🔗 连接状态"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Cyan1)
        };

        return connectionPanel;
    }
} 