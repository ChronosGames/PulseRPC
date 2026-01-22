using System.Text.Json;
using System.Text.Json.Serialization;
using PulseRPC.Benchmark.Models;

namespace PulseRPC.Benchmark.Reports;

/// <summary>
/// JSON报告器
/// </summary>
public static class JsonReporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task SaveAsync(BenchmarkResult result, string outputPath)
    {
        var json = JsonSerializer.Serialize(result, Options);
        await File.WriteAllTextAsync(outputPath, json);
    }

    public static string Serialize(BenchmarkResult result)
    {
        return JsonSerializer.Serialize(result, Options);
    }
}
