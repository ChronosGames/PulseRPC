using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PulseRPC.Client.Transport;

/// <summary>
/// 网络诊断工具
/// </summary>
public class NetworkDiagnostics
{
    private readonly ILogger _logger;

    public NetworkDiagnostics(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// 运行完整的网络诊断
    /// </summary>
    public async Task<NetworkDiagnosticResult> RunDiagnosticsAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        var result = new NetworkDiagnosticResult
        {
            Host = host,
            Port = port,
            DiagnosticTime = DateTime.UtcNow
        };

        _logger.LogInformation("开始网络诊断: {Host}:{Port}", host, port);

        try
        {
            // 1. 基础连通性测试
            result.PingResult = await TestPingAsync(host, cancellationToken);

            // 2. UDP端口连通性测试
            result.UdpConnectivityResult = await TestUdpConnectivityAsync(host, port, cancellationToken);

            // 3. 本地网络接口检查
            result.NetworkInterfaceInfo = GetNetworkInterfaceInfo();

            // 4. 防火墙检测
            result.FirewallInfo = DetectFirewallIssues();

            // 5. 系统资源检查
            result.SystemResourceInfo = GetSystemResourceInfo();

            result.IsSuccessful = result.PingResult.IsSuccessful && result.UdpConnectivityResult.IsSuccessful;

            _logger.LogInformation("网络诊断完成: 成功={IsSuccessful}", result.IsSuccessful);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "网络诊断过程中发生异常");
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// 测试Ping连通性
    /// </summary>
    private async Task<PingResult> TestPingAsync(string host, CancellationToken cancellationToken)
    {
        var result = new PingResult { Host = host };

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 5000);

            result.IsSuccessful = reply.Status == IPStatus.Success;
            result.RoundTripTime = reply.RoundtripTime;
            result.Status = reply.Status.ToString();

            if (result.IsSuccessful)
            {
                _logger.LogDebug("Ping成功: {Host}, RTT={RTT}ms", host, reply.RoundtripTime);
            }
            else
            {
                _logger.LogWarning("Ping失败: {Host}, Status={Status}", host, reply.Status);
            }
        }
        catch (Exception ex)
        {
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Ping测试异常: {Host}", host);
        }

        return result;
    }

    /// <summary>
    /// 测试UDP连通性
    /// </summary>
    private async Task<UdpConnectivityResult> TestUdpConnectivityAsync(string host, int port, CancellationToken cancellationToken)
    {
        var result = new UdpConnectivityResult { Host = host, Port = port };

        try
        {
            using var udpClient = new UdpClient();
            var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 0);
            udpClient.Client.Bind(endpoint);

            // 解析目标地址
            var addresses = await Dns.GetHostAddressesAsync(host);
            var targetAddress = Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetwork);

            if (targetAddress == null)
            {
                result.IsSuccessful = false;
                result.ErrorMessage = $"无法解析主机地址: {host}";
                return result;
            }

            var targetEndpoint = new IPEndPoint(targetAddress, port);

            // 发送测试数据
            var testData = System.Text.Encoding.UTF8.GetBytes("DIAGNOSTIC_TEST");
            var stopwatch = Stopwatch.StartNew();

            await udpClient.SendAsync(testData, testData.Length, targetEndpoint);

            stopwatch.Stop();
            result.IsSuccessful = true;
            result.SendTime = stopwatch.ElapsedMilliseconds;

            _logger.LogDebug("UDP连通性测试成功: {Host}:{Port}, 发送时间={SendTime}ms", host, port, result.SendTime);
        }
        catch (Exception ex)
        {
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "UDP连通性测试失败: {Host}:{Port}", host, port);
        }

        return result;
    }

    /// <summary>
    /// 获取网络接口信息
    /// </summary>
    private List<NetworkInterfaceInfo> GetNetworkInterfaceInfo()
    {
        var interfaces = new List<NetworkInterfaceInfo>();

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var info = new NetworkInterfaceInfo
                    {
                        Name = ni.Name,
                        Description = ni.Description,
                        Type = ni.NetworkInterfaceType.ToString(),
                        Status = ni.OperationalStatus.ToString(),
                        Speed = ni.Speed,
                        SupportsMulticast = ni.SupportsMulticast
                    };

                    var ipProps = ni.GetIPProperties();
                    foreach (var ip in ipProps.UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            info.IPAddresses.Add(ip.Address.ToString());
                        }
                    }

                    interfaces.Add(info);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取网络接口信息失败");
        }

        return interfaces;
    }

    /// <summary>
    /// 检测防火墙问题
    /// </summary>
    private FirewallInfo DetectFirewallIssues()
    {
        var info = new FirewallInfo();

        try
        {
            // Windows防火墙检测
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                info.WindowsFirewallEnabled = IsWindowsFirewallEnabled();
            }

            // 检查端口占用情况
            info.Suggestions.Add("检查目标端口是否被其他程序占用");
            info.Suggestions.Add("确认防火墙允许UDP通信");
            info.Suggestions.Add("尝试临时禁用防火墙进行测试");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "防火墙检测失败");
            info.ErrorMessage = ex.Message;
        }

        return info;
    }

    /// <summary>
    /// 检查Windows防火墙状态
    /// </summary>
    private bool IsWindowsFirewallEnabled()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "advfirewall show allprofiles state",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return output.Contains("ON") || output.Contains("启用");
        }
        catch
        {
            return false; // 无法确定，假设未启用
        }
    }

    /// <summary>
    /// 获取系统资源信息
    /// </summary>
    private SystemResourceInfo GetSystemResourceInfo()
    {
        var info = new SystemResourceInfo();

        try
        {
            // 获取进程信息
            using var currentProcess = Process.GetCurrentProcess();
            info.ProcessId = currentProcess.Id;
            info.WorkingSet = currentProcess.WorkingSet64;
            info.ThreadCount = currentProcess.Threads.Count;

            // 系统信息
            info.OSVersion = Environment.OSVersion.ToString();
            info.ProcessorCount = Environment.ProcessorCount;
            info.Is64BitProcess = Environment.Is64BitProcess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取系统资源信息失败");
            info.ErrorMessage = ex.Message;
        }

        return info;
    }
}

/// <summary>
/// 网络诊断结果
/// </summary>
public class NetworkDiagnosticResult
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public DateTime DiagnosticTime { get; set; }
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }

    public PingResult PingResult { get; set; } = new PingResult();
    public UdpConnectivityResult UdpConnectivityResult { get; set; } = new UdpConnectivityResult();
    public List<NetworkInterfaceInfo> NetworkInterfaceInfo { get; set; } = new List<NetworkInterfaceInfo>();
    public FirewallInfo FirewallInfo { get; set; } = new FirewallInfo();
    public SystemResourceInfo SystemResourceInfo { get; set; } = new SystemResourceInfo();

    public string GetSummary()
    {
        var summary = $"网络诊断报告 - {Host}:{Port}\n";
        summary += $"诊断时间: {DiagnosticTime:yyyy-MM-dd HH:mm:ss} UTC\n";
        summary += $"整体状态: {(IsSuccessful ? "成功" : "失败")}\n\n";

        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            summary += $"错误信息: {ErrorMessage}\n\n";
        }

        summary += $"Ping测试: {(PingResult.IsSuccessful ? "成功" : "失败")}";
        if (PingResult.IsSuccessful)
        {
            summary += $" (RTT: {PingResult.RoundTripTime}ms)";
        }
        summary += "\n";

        summary += $"UDP连通性: {(UdpConnectivityResult.IsSuccessful ? "成功" : "失败")}";
        if (UdpConnectivityResult.IsSuccessful)
        {
            summary += $" (发送时间: {UdpConnectivityResult.SendTime}ms)";
        }
        summary += "\n";

        if (FirewallInfo.WindowsFirewallEnabled)
        {
            summary += "Windows防火墙: 已启用\n";
        }

        summary += $"网络接口数量: {NetworkInterfaceInfo.Count}\n";

        return summary;
    }
}

public class PingResult
{
    public string Host { get; set; } = string.Empty;
    public bool IsSuccessful { get; set; }
    public long RoundTripTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public class UdpConnectivityResult
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool IsSuccessful { get; set; }
    public long SendTime { get; set; }
    public string? ErrorMessage { get; set; }
}

public class NetworkInterfaceInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long Speed { get; set; }
    public bool SupportsMulticast { get; set; }
    public List<string> IPAddresses { get; set; } = new List<string>();
}

public class FirewallInfo
{
    public bool WindowsFirewallEnabled { get; set; }
    public List<string> Suggestions { get; set; } = new List<string>();
    public string? ErrorMessage { get; set; }
}

public class SystemResourceInfo
{
    public int ProcessId { get; set; }
    public long WorkingSet { get; set; }
    public int ThreadCount { get; set; }
    public string OSVersion { get; set; } = string.Empty;
    public int ProcessorCount { get; set; }
    public bool Is64BitProcess { get; set; }
    public string? ErrorMessage { get; set; }
}
