using System;

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
