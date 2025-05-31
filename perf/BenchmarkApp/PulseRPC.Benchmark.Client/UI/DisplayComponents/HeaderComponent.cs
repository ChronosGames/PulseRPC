using Spectre.Console;
using Spectre.Console.Rendering;
using PulseRPC.Benchmark.Client.UI.Models;

namespace PulseRPC.Benchmark.Client.UI.DisplayComponents;

/// <summary>
/// 标题区域显示组件
/// </summary>
public class HeaderComponent
{
    /// <summary>
    /// 渲染标题区域
    /// </summary>
    public IRenderable Render(DisplayData data)
    {
        var rule = new Rule($"[bold blue]🚀 PulseRPC Benchmark Test: {data.Configuration.ScenarioName}[/]")
        {
            Style = Style.Parse("blue"),
            Justification = Justify.Left
        };

        return new Rows(
            rule,
            new Text($"🌐 服务器: {data.Configuration.ServerAddress} | " +
                    $"⏱️ 时长: {data.Configuration.DurationSeconds}s | " +
                    $"🔗 连接: {data.Configuration.ConcurrentConnections} | " +
                    $"📊 速率: {data.Configuration.RequestRate} QPS",
                    new Style(Color.Grey))
        );
    }
} 