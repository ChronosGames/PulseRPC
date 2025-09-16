using PulseRPC.Transport;
// using System.ComponentModel.DataAnnotations; // Not available in netstandard2.1

namespace PulseRPC.Client.Core;

/// <summary>
/// 连接描述符 - 完整描述一个连接的配置信息
/// </summary>
public sealed class ConnectionDescriptor
{
    /// <summary>
    /// 连接唯一标识符
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 连接名称（可读的名称）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 服务名称（用于服务发现）
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// 连接端点（直连时使用）
    /// </summary>
    public EndpointAddress? Endpoint { get; set; }

    /// <summary>
    /// 传输类型
    /// </summary>
    public TransportType Transport { get; set; } = TransportType.Tcp;

    /// <summary>
    /// 连接策略
    /// </summary>
    public ConnectionStrategy Strategy { get; set; } = ConnectionStrategy.Session;

    /// <summary>
    /// 是否启用自动重连
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// 连接标签（用于路由和分类）
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>
    /// 传输选项
    /// </summary>
    public TransportOptions? TransportOptions { get; set; }

    /// <summary>
    /// 连接超时时间
    /// </summary>
    public TimeSpan? ConnectTimeout { get; set; }

    /// <summary>
    /// 空闲超时时间（适用于 Session 和 Transient 策略）
    /// </summary>
    public TimeSpan? IdleTimeout { get; set; }

    /// <summary>
    /// 连接优先级
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// 最大重连次数
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 3;

    /// <summary>
    /// 重连间隔
    /// </summary>
    public TimeSpan ReconnectInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 负载均衡权重
    /// </summary>
    public int Weight { get; set; } = 1;

    /// <summary>
    /// 是否启用健康检查
    /// </summary>
    public bool EnableHealthCheck { get; set; } = true;

    /// <summary>
    /// 健康检查间隔
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 扩展属性
    /// </summary>
    public Dictionary<string, object> Properties { get; set; } = new();

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    /// <summary>
    /// 验证连接描述符的有效性
    /// </summary>
    public ValidationResult Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Id))
            errors.Add("连接 ID 不能为空");

        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("连接名称不能为空");

        // 如果没有服务名称，必须有端点信息
        if (string.IsNullOrWhiteSpace(ServiceName) && Endpoint == null)
            errors.Add("必须指定服务名称或连接端点");

        // 如果有端点信息，验证端点有效性
        if (Endpoint != null)
        {
            var endpointValidation = Endpoint.Validate();
            if (!endpointValidation.IsValid)
                errors.AddRange(endpointValidation.Errors);
        }

        if (Weight <= 0)
            errors.Add("负载均衡权重必须大于 0");

        if (MaxReconnectAttempts < 0)
            errors.Add("最大重连次数不能为负数");

        if (ReconnectInterval <= TimeSpan.Zero)
            errors.Add("重连间隔必须大于 0");

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    /// <summary>
    /// 创建 TCP 连接描述符
    /// </summary>
    public static ConnectionDescriptor CreateTcp(
        string id,
        string name,
        string host,
        int port,
        ConnectionStrategy strategy = ConnectionStrategy.Session,
        Dictionary<string, string>? tags = null)
    {
        return new ConnectionDescriptor
        {
            Id = id,
            Name = name,
            Endpoint = new EndpointAddress(host, port),
            Transport = TransportType.Tcp,
            Strategy = strategy,
            Tags = tags ?? new Dictionary<string, string>()
        };
    }

    /// <summary>
    /// 创建 KCP 连接描述符
    /// </summary>
    public static ConnectionDescriptor CreateKcp(
        string id,
        string name,
        string host,
        int port,
        ConnectionStrategy strategy = ConnectionStrategy.Session,
        Dictionary<string, string>? tags = null)
    {
        return new ConnectionDescriptor
        {
            Id = id,
            Name = name,
            Endpoint = new EndpointAddress(host, port),
            Transport = TransportType.Kcp,
            Strategy = strategy,
            Tags = tags ?? new Dictionary<string, string>()
        };
    }

    /// <summary>
    /// 创建服务发现连接描述符
    /// </summary>
    public static ConnectionDescriptor CreateService(
        string id,
        string name,
        string serviceName,
        TransportType transport = TransportType.Tcp,
        ConnectionStrategy strategy = ConnectionStrategy.Session,
        Dictionary<string, string>? tags = null)
    {
        return new ConnectionDescriptor
        {
            Id = id,
            Name = name,
            ServiceName = serviceName,
            Transport = transport,
            Strategy = strategy,
            Tags = tags ?? new Dictionary<string, string>()
        };
    }

    /// <summary>
    /// 克隆连接描述符
    /// </summary>
    public ConnectionDescriptor Clone()
    {
        return new ConnectionDescriptor
        {
            Id = Id,
            Name = Name,
            ServiceName = ServiceName,
            Endpoint = Endpoint?.Clone(),
            Transport = Transport,
            Strategy = Strategy,
            AutoReconnect = AutoReconnect,
            Tags = new Dictionary<string, string>(Tags),
            TransportOptions = TransportOptions,
            ConnectTimeout = ConnectTimeout,
            IdleTimeout = IdleTimeout,
            Priority = Priority,
            MaxReconnectAttempts = MaxReconnectAttempts,
            ReconnectInterval = ReconnectInterval,
            Weight = Weight,
            EnableHealthCheck = EnableHealthCheck,
            HealthCheckInterval = HealthCheckInterval,
            Properties = new Dictionary<string, object>(Properties)
        };
    }

    public override string ToString()
    {
        var endpoint = Endpoint?.ToString() ?? ServiceName ?? "Unknown";
        return $"{Name} ({Id}) -> {endpoint} [{Transport}:{Strategy}]";
    }
}

/// <summary>
/// 验证结果
/// </summary>
public sealed class ValidationResult
{
    /// <summary>
    /// 是否验证通过
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// 错误信息列表
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// 获取错误信息字符串
    /// </summary>
    public string GetErrorString()
    {
        return string.Join("; ", Errors);
    }
}
