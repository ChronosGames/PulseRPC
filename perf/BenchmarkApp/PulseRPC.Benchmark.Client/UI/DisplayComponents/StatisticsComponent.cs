using Spectre.Console;
using Spectre.Console.Rendering;
using PulseRPC.Benchmark.Client.UI.Models;

namespace PulseRPC.Benchmark.Client.UI.DisplayComponents;

/// <summary>
/// 实时统计信息显示组件
/// </summary>
public class StatisticsComponent
{
    /// <summary>
    /// 渲染统计信息区域
    /// </summary>
    public IRenderable Render(DisplayData data)
    {
        var progress = data.Progress;
        var successRate = progress.TotalRequests > 0 
            ? (double)progress.SuccessfulRequests / progress.TotalRequests * 100 
            : 0;

        var statisticsTable = new Table()
            .BorderColor(Color.Blue)
            .Title("[bold blue]📊 实时统计[/]")
            .AddColumn("[bold]指标[/]")
            .AddColumn("[bold]数值[/]");

        statisticsTable.Border = TableBorder.Rounded;
        
        statisticsTable.AddRow("总请求数", progress.TotalRequests.ToString("N0"));
        statisticsTable.AddRow("成功请求", $"{progress.SuccessfulRequests:N0} ([green]{successRate:F1}%[/])");
        statisticsTable.AddRow("失败请求", $"[red]{progress.FailedRequests:N0}[/]");
        statisticsTable.AddRow("当前QPS", $"[yellow]{progress.RequestsPerSecond:F1}[/]");
        
        var latencyTable = new Table()
            .BorderColor(Color.Green)
            .Title("[bold green]⚡ 延迟统计[/]")
            .AddColumn("[bold]类型[/]")
            .AddColumn("[bold]延迟[/]");

        latencyTable.Border = TableBorder.Rounded;
        
        latencyTable.AddRow("平均延迟", $"{progress.AverageLatencyMs:F1} ms");
        
        // 如果有更多延迟数据，可以在这里显示
        // latencyTable.AddRow("P95延迟", $"{progress.P95LatencyMs:F1} ms");
        // latencyTable.AddRow("P99延迟", $"{progress.P99LatencyMs:F1} ms");

        return new Rows(statisticsTable, Text.Empty, latencyTable);
    }
} 