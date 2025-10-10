using System.Text.Json;
using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Storage;

/// <summary>
/// Repository for baseline persistence using JSON files
/// </summary>
public class BaselineRepository
{
    private readonly string _baselineDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public BaselineRepository(string baselineDirectory = "baselines")
    {
        _baselineDirectory = baselineDirectory;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Ensure directory exists
        if (!Directory.Exists(_baselineDirectory))
        {
            Directory.CreateDirectory(_baselineDirectory);
        }
    }

    /// <summary>
    /// Saves a baseline to JSON file
    /// </summary>
    public async Task SaveBaselineAsync(BaselineData baseline, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        
        if (string.IsNullOrWhiteSpace(baseline.Name))
        {
            throw new ArgumentException("Baseline name cannot be empty", nameof(baseline));
        }

        var fileName = GetBaselineFileName(baseline.Name);
        var filePath = Path.Combine(_baselineDirectory, fileName);

        var json = JsonSerializer.Serialize(baseline, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    /// <summary>
    /// Loads a baseline by name
    /// </summary>
    public async Task<BaselineData?> LoadBaselineAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Baseline name cannot be empty", nameof(name));
        }

        var fileName = GetBaselineFileName(name);
        var filePath = Path.Combine(_baselineDirectory, fileName);

        if (!File.Exists(filePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        return JsonSerializer.Deserialize<BaselineData>(json, _jsonOptions);
    }

    /// <summary>
    /// Lists all available baselines
    /// </summary>
    public async Task<List<BaselineData>> ListBaselinesAsync(CancellationToken cancellationToken = default)
    {
        var baselines = new List<BaselineData>();

        if (!Directory.Exists(_baselineDirectory))
        {
            return baselines;
        }

        var files = Directory.GetFiles(_baselineDirectory, "*.json");

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var baseline = JsonSerializer.Deserialize<BaselineData>(json, _jsonOptions);
                
                if (baseline != null)
                {
                    baselines.Add(baseline);
                }
            }
            catch
            {
                // Skip invalid files
            }
        }

        return baselines;
    }

    /// <summary>
    /// Deletes a baseline by name
    /// </summary>
    public Task DeleteBaselineAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Baseline name cannot be empty", nameof(name));
        }

        var fileName = GetBaselineFileName(name);
        var filePath = Path.Combine(_baselineDirectory, fileName);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Checks if a baseline exists
    /// </summary>
    public bool BaselineExists(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var fileName = GetBaselineFileName(name);
        var filePath = Path.Combine(_baselineDirectory, fileName);
        return File.Exists(filePath);
    }

    private static string GetBaselineFileName(string name)
    {
        // Sanitize the name to create a valid filename
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return $"baseline_{sanitized}.json";
    }
}

