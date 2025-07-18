namespace PulseRPC.Infrastructure.Etcd;

public record EtcdOptions
{
    public string[] Endpoints { get; init; } = { "http://localhost:2379" };
    public string KeyPrefix { get; init; } = "/pulse-service-discovery";
    public bool UseLeases { get; init; } = true;
    public TimeSpan LeaseTtl { get; init; } = TimeSpan.FromMinutes(10);
    public EtcdConnectionOptions Connection { get; init; } = new();
    public EtcdAuthOptions Auth { get; init; } = new();
}

public record EtcdConnectionOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxRetries { get; init; } = 3;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public bool UseTls { get; init; } = false;
}

public record EtcdAuthOptions
{
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? CertFile { get; init; }
    public string? KeyFile { get; init; }
    public string? CaCertFile { get; init; }
}
