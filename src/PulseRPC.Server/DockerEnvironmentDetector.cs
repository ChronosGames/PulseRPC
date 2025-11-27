using System.Net;
using System.Net.Sockets;
using PulseRPC.Server;

namespace PulseRPC.Server;

public class DockerEnvironmentDetector
{
    // 检测是否在Docker容器中运行
    public static bool IsRunningInContainer()
    {
        // 检查cgroup文件
        if (File.Exists("/proc/1/cgroup"))
        {
            var content = File.ReadAllText("/proc/1/cgroup");
            if (content.Contains("/docker/") || content.Contains("/kubepods/"))
            {
                return true;
            }
        }

        // 检查.dockerenv文件
        if (File.Exists("/.dockerenv"))
        {
            return true;
        }

        return false;
    }

    // 从环境变量获取服务配置
    public static ServerOptions GetServerOptionsFromEnv()
    {
        var options = new ServerOptions();

        options.Port = GetEnvInt("NETCORE_PORT", options.Port);
        options.MaxConnections = GetEnvInt("NETCORE_MAX_CONNECTIONS", options.MaxConnections);
        options.ReceiveBufferSize = GetEnvInt("NETCORE_RECEIVE_BUFFER_SIZE", options.ReceiveBufferSize);
        options.SendBufferSize = GetEnvInt("NETCORE_SEND_BUFFER_SIZE", options.SendBufferSize);
        options.BacklogSize = GetEnvInt("NETCORE_BACKLOG_SIZE", options.BacklogSize);
        options.MaxPacketSize = GetEnvInt("NETCORE_MAX_PACKET_SIZE", options.MaxPacketSize);
        options.NoDelay = GetEnvBool("NETCORE_NO_DELAY", options.NoDelay);
        options.UseEncryption = GetEnvBool("NETCORE_USE_ENCRYPTION", options.UseEncryption);

        var idleTimeoutSeconds = GetEnvInt("NETCORE_IDLE_TIMEOUT_SECONDS", (int)options.IdleTimeout.TotalSeconds);
        options.IdleTimeout = TimeSpan.FromSeconds(idleTimeoutSeconds);

        var heartbeatIntervalSeconds = GetEnvInt("NETCORE_HEARTBEAT_INTERVAL_SECONDS", (int)options.HeartbeatInterval.TotalSeconds);
        options.HeartbeatInterval = TimeSpan.FromSeconds(heartbeatIntervalSeconds);

        return options;
    }

    // 从环境变量获取服务注册信息
    // public static ServiceRegistration GetServiceRegistrationFromEnv()
    // {
    //     var registration = new ServiceRegistration
    //     {
    //         ServiceType = Environment.GetEnvironmentVariable("SERVICE_TYPE") ?? "Unknown",
    //         ZoneId = Environment.GetEnvironmentVariable("ZONE_ID") ?? "default",
    //         ServerId = Environment.GetEnvironmentVariable("SERVER_ID") ?? "default",
    //         InstanceId = Environment.GetEnvironmentVariable("INSTANCE_ID") ?? "default",
    //         Host = GetContainerIP(),
    //         Port = GetEnvInt("SERVICE_PORT", 0)
    //     };
    //
    //     // 添加自定义元数据
    //     var metadataPrefix = "SERVICE_META_";
    //     foreach (var key in Environment.GetEnvironmentVariables().Keys)
    //     {
    //         var envKey = key.ToString() ?? string.Empty;
    //         if (envKey.StartsWith(metadataPrefix, StringComparison.Ordinal))
    //         {
    //             var metaKey = envKey.Substring(metadataPrefix.Length);
    //             var metaValue = Environment.GetEnvironmentVariable(envKey) ?? string.Empty;
    //             registration.Metadata[metaKey] = metaValue;
    //         }
    //     }
    //
    //     return registration;
    // }

    // 获取容器IP地址
    private static string GetContainerIP()
    {
        // 首先检查环境变量
        var hostFromEnv = Environment.GetEnvironmentVariable("SERVICE_HOST");
        if (!string.IsNullOrEmpty(hostFromEnv))
        {
            return hostFromEnv;
        }

        // 否则尝试获取本机IP（非localhost）
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork &&
                    !ip.ToString().StartsWith("127."))
                {
                    return ip.ToString();
                }
            }

            // 如果没有找到合适的IP，返回localhost
            return "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    // 辅助方法：从环境变量获取整数值
    private static int GetEnvInt(string name, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out var result) ? result : defaultValue;
    }

    // 辅助方法：从环境变量获取布尔值
    private static bool GetEnvBool(string name, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value))
            return defaultValue;

        return value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
