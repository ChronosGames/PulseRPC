namespace PulseServiceDiscovery.Consul;

public record ConsulOptions
{
    public string Endpoint { get; init; } = "http://localhost:8500";
    public string? Datacenter { get; init; }
    public string? Token { get; init; }
    public ConsulHealthCheckOptions HealthCheck { get; init; } = new();
    public ConsulDiscoveryOptions DiscoveryOptions { get; init; } = new();
    public ConsulConnectionOptions Connection { get; init; } = new();
}

public record ConsulHealthCheckOptions
{
    public bool Enabled { get; init; } = true;
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan DeregisterAfter { get; init; } = TimeSpan.FromMinutes(10);
}

public record ConsulDiscoveryOptions
{
    public bool HealthyOnly { get; init; } = true;
    public bool UseCache { get; init; } = true;
    public TimeSpan CacheTtl { get; init; } = TimeSpan.FromMinutes(5);
    public string[]? Tags { get; init; }
}

public record ConsulConnectionOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxRetries { get; init; } = 3;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
}

