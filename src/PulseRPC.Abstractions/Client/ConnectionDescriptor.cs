using PulseRPC.Transport;
using System;
using System.Collections.Generic;

namespace PulseRPC.Client;

/// <summary>
/// 端点地址
/// </summary>
public sealed class EndpointAddress
{
    /// <summary>
    /// 主机名或IP地址
    /// </summary>
    public string Host { get; }

    /// <summary>
    /// 端口号
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public EndpointAddress(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("主机名不能为空", nameof(host));
        }

        if (port <= 0 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "端口号必须在1-65535范围内");
        }

        Host = host;
        Port = port;
    }

    /// <summary>
    /// 验证端点地址的有效性
    /// </summary>
    public ValidationResult Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Host))
            errors.Add("主机地址不能为空");

        if (Port <= 0 || Port > 65535)
            errors.Add("端口号必须在 1-65535 范围内");

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    /// <summary>
    /// 克隆端点地址
    /// </summary>
    public EndpointAddress Clone()
    {
        return new EndpointAddress(Host, Port);
    }

    /// <summary>
    /// 从字符串解析端点地址
    /// </summary>
    public static EndpointAddress Parse(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("端点地址不能为空", nameof(endpoint));
        }

        var lastColonIndex = endpoint.LastIndexOf(':');
        if (lastColonIndex == -1)
        {
            throw new ArgumentException("端点地址格式无效，应为 host:port", nameof(endpoint));
        }

        var host = endpoint.Substring(0, lastColonIndex);
        var portStr = endpoint.Substring(lastColonIndex + 1);

        if (!int.TryParse(portStr, out var port))
        {
            throw new ArgumentException("端口号格式无效", nameof(endpoint));
        }

        return new EndpointAddress(host, port);
    }

    /// <summary>
    /// 尝试从字符串解析端点地址
    /// </summary>
    public static bool TryParse(string endpoint, out EndpointAddress? result)
    {
        result = null;

        try
        {
            result = Parse(endpoint);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public override string ToString()
    {
        return $"{Host}:{Port}";
    }

    public override bool Equals(object? obj)
    {
        if (obj is EndpointAddress other)
        {
            return Host.Equals(other.Host, StringComparison.OrdinalIgnoreCase) && Port == other.Port;
        }
        return false;
    }

    public override int GetHashCode()
    {
#if NETSTANDARD2_1
        return Host.ToLowerInvariant().GetHashCode() ^ Port.GetHashCode();
#else
        return HashCode.Combine(Host.ToLowerInvariant(), Port);
#endif
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
    public TransportType Transport { get; set; } = TransportType.TCP;

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
            Transport = TransportType.TCP,
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
            Transport = TransportType.KCP,
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
        TransportType transport = TransportType.TCP,
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
/// 连接策略枚举
/// </summary>
public enum ConnectionStrategy
{
    /// <summary>
    /// 会话级连接 - 在客户端运行期间保持连接
    /// </summary>
    Session,

    /// <summary>
    /// 瞬态连接 - 用完即断开
    /// </summary>
    Transient,

    /// <summary>
    /// 持久连接 - 始终保持连接，自动重连
    /// </summary>
    Persistent,

    /// <summary>
    /// 池化连接 - 使用连接池管理
    /// </summary>
    Pooled
}

/// <summary>
/// 连接统计信息
/// </summary>
public sealed class ConnectionStatistics
{
    /// <summary>
    /// 连接ID
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 连接时间
    /// </summary>
    public DateTime? ConnectedAt { get; set; }

    /// <summary>
    /// 最后活动时间
    /// </summary>
    public DateTime LastActiveAt { get; set; }

    /// <summary>
    /// 发送的消息数
    /// </summary>
    public long MessagesSent { get; set; }

    /// <summary>
    /// 接收的消息数
    /// </summary>
    public long MessagesRecv { get; set; }

    /// <summary>
    /// 发送的字节数
    /// </summary>
    public long BytesSent { get; set; }

    /// <summary>
    /// 接收的字节数
    /// </summary>
    public long BytesRecv { get; set; }

    /// <summary>
    /// 错误计数
    /// </summary>
    public long ErrorCount { get; set; }

    /// <summary>
    /// 重连次数
    /// </summary>
    public int ReconnectCount { get; set; }

    /// <summary>
    /// 平均响应时间
    /// </summary>
    public TimeSpan AverageResponseTime { get; set; }

    /// <summary>
    /// 活跃请求数
    /// </summary>
    public int ActiveRequests { get; set; }

    /// <summary>
    /// 最后错误时间
    /// </summary>
    public DateTime? LastErrorAt { get; set; }

    /// <summary>
    /// 最后错误信息
    /// </summary>
    public string? LastError { get; set; }
}
