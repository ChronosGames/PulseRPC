using System;
using System.Runtime.InteropServices;

namespace PulseRPC.Benchmark.Shared.Environment;

/// <summary>
/// 环境检测器
/// 检测操作系统、CPU、内存等环境信息
/// </summary>
public static class EnvironmentDetector
{
    /// <summary>
    /// 检测完整的环境信息
    /// </summary>
    /// <returns>环境信息对象</returns>
    public static EnvironmentInfo Detect()
    {
        return new EnvironmentInfo
        {
            OperatingSystem = GetOperatingSystem(),
            OSVersion = GetOSVersion(),
            CpuModel = GetCpuModel(),
            CpuCoreCount = System.Environment.ProcessorCount,
            TotalMemoryMB = GetTotalMemoryMB(),
            DotNetVersion = GetDotNetVersion(),
            Architecture = GetArchitecture(),
            MachineName = System.Environment.MachineName,
            UserName = System.Environment.UserName,
            Is64BitOperatingSystem = System.Environment.Is64BitOperatingSystem,
            Is64BitProcess = System.Environment.Is64BitProcess
        };
    }

    /// <summary>
    /// 获取操作系统名称
    /// </summary>
    private static string GetOperatingSystem()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "Windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "Linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "macOS";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
            return "FreeBSD";
        
        return "Unknown";
    }

    /// <summary>
    /// 获取操作系统版本
    /// </summary>
    private static string GetOSVersion()
    {
        return RuntimeInformation.OSDescription;
    }

    /// <summary>
    /// 获取 CPU 型号
    /// </summary>
    private static string GetCpuModel()
    {
        try
        {
            // 在 Windows 上通过环境变量获取
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return System.Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") 
                    ?? "Unknown CPU";
            }

            // 在 Linux 上可以读取 /proc/cpuinfo
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (System.IO.File.Exists("/proc/cpuinfo"))
                {
                    var lines = System.IO.File.ReadAllLines("/proc/cpuinfo");
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("model name"))
                        {
                            var parts = line.Split(':');
                            if (parts.Length > 1)
                                return parts[1].Trim();
                        }
                    }
                }
            }

            // 在 macOS 上可以使用 sysctl
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "Apple Silicon / Intel CPU"; // 简化处理
            }
        }
        catch
        {
            // 忽略错误，返回默认值
        }

        return $"{System.Environment.ProcessorCount} Core CPU";
    }

    /// <summary>
    /// 获取总内存（MB）
    /// </summary>
    private static long GetTotalMemoryMB()
    {
        try
        {
            // .NET 没有直接的 API 获取总物理内存
            // 这里返回当前进程可用的内存作为估算
            var gcMemory = GC.GetGCMemoryInfo();
            var totalMemory = gcMemory.TotalAvailableMemoryBytes / (1024 * 1024);
            
            if (totalMemory > 0)
                return totalMemory;
        }
        catch
        {
            // 忽略错误
        }

        return 0; // 无法获取
    }

    /// <summary>
    /// 获取 .NET 版本
    /// </summary>
    private static string GetDotNetVersion()
    {
        return RuntimeInformation.FrameworkDescription;
    }

    /// <summary>
    /// 获取架构
    /// </summary>
    private static string GetArchitecture()
    {
        return RuntimeInformation.ProcessArchitecture.ToString();
    }
}

/// <summary>
/// 环境信息
/// </summary>
public class EnvironmentInfo
{
    /// <summary>
    /// 操作系统名称
    /// </summary>
    public string OperatingSystem { get; set; } = string.Empty;

    /// <summary>
    /// 操作系统版本
    /// </summary>
    public string OSVersion { get; set; } = string.Empty;

    /// <summary>
    /// CPU 型号
    /// </summary>
    public string CpuModel { get; set; } = string.Empty;

    /// <summary>
    /// CPU 核心数
    /// </summary>
    public int CpuCoreCount { get; set; }

    /// <summary>
    /// 总内存（MB）
    /// </summary>
    public long TotalMemoryMB { get; set; }

    /// <summary>
    /// .NET 运行时版本
    /// </summary>
    public string DotNetVersion { get; set; } = string.Empty;

    /// <summary>
    /// 处理器架构
    /// </summary>
    public string Architecture { get; set; } = string.Empty;

    /// <summary>
    /// 机器名称
    /// </summary>
    public string MachineName { get; set; } = string.Empty;

    /// <summary>
    /// 用户名
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// 是否为64位操作系统
    /// </summary>
    public bool Is64BitOperatingSystem { get; set; }

    /// <summary>
    /// 是否为64位进程
    /// </summary>
    public bool Is64BitProcess { get; set; }

    /// <summary>
    /// 转换为字符串表示
    /// </summary>
    public override string ToString()
    {
        return $"{OperatingSystem} {OSVersion} | {CpuModel} ({CpuCoreCount} cores) | " +
               $"{TotalMemoryMB} MB RAM | {DotNetVersion} | {Architecture}";
    }
}

