using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PulseRPC.Server.Monitoring;

public static class CpuUsageHelper
{
    public static double GetCpuUsage()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return GetCpuUsageWindows();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return GetCpuUsageLinux();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return GetCpuUsageOSX();
        }

        return -1;
    }

    // 使用PerformanceCounter (Windows平台)
    private static float GetCpuUsageWindows()
    {
        try
        {
            using var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            cpuCounter.NextValue(); // 第一次调用总是返回0，需要等待一段时间后再次调用
            Thread.Sleep(1000);     // 等待1秒
            return cpuCounter.NextValue();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"获取CPU使用率失败: {ex.Message}");
            return -1;
        }
    }

    // Linux平台下获取CPU使用率
    private static double GetCpuUsageLinux()
    {
        try
        {
            // 读取/proc/stat文件中的CPU数据
            string[] cpuStats = File.ReadAllLines("/proc/stat")[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // 获取用户态、系统态、空闲等CPU时间
            long user = long.Parse(cpuStats[1]);
            long nice = long.Parse(cpuStats[2]);
            long system = long.Parse(cpuStats[3]);
            long idle = long.Parse(cpuStats[4]);

            // 计算总CPU时间
            long totalCpuTime1 = user + nice + system + idle;
            long idleTime1 = idle;

            // 等待一段时间
            Thread.Sleep(1000);

            // 再次读取数据
            cpuStats = File.ReadAllLines("/proc/stat")[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            user = long.Parse(cpuStats[1]);
            nice = long.Parse(cpuStats[2]);
            system = long.Parse(cpuStats[3]);
            idle = long.Parse(cpuStats[4]);

            long totalCpuTime2 = user + nice + system + idle;
            long idleTime2 = idle;

            // 计算这段时间内的CPU使用率
            long totalCpuTime = totalCpuTime2 - totalCpuTime1;
            long idleTime = idleTime2 - idleTime1;

            return 100.0 * (1.0 - (double)idleTime / totalCpuTime);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"获取Linux CPU使用率失败: {ex.Message}");
            return -1;
        }
    }

    // macOS平台下获取CPU使用率
    private static double GetCpuUsageOSX()
    {
        try
        {
            // 使用Process调用top命令
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"top -l 1 | grep 'CPU usage'\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // 解析输出 - 示例格式: "CPU usage: 10.51% user, 15.31% sys, 74.18% idle"
            string[] parts = output.Split(':')[1].Split(',');
            string userPart = parts[0].Trim();
            string sysPart = parts[1].Trim();

            double userPercentage = double.Parse(userPart.Split('%')[0]);
            double sysPercentage = double.Parse(sysPart.Split('%')[0]);

            // 返回用户态和系统态CPU使用率之和
            return userPercentage + sysPercentage;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"获取macOS CPU使用率失败: {ex.Message}");
            return -1;
        }
    }

    // 获取进程CPU使用率
    public static double GetProcessCpuUsage()
    {
        try
        {
            var startTime = DateTime.UtcNow;
            var startCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;

            Thread.Sleep(500);

            var endTime = DateTime.UtcNow;
            var endCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;

            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMsPassed = (endTime - startTime).TotalMilliseconds;

            var cpuUsagePercentage = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed) * 100;

            return Math.Round(cpuUsagePercentage, 2);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"获取进程CPU使用率失败: {ex.Message}");
            return -1;
        }
    }
}
