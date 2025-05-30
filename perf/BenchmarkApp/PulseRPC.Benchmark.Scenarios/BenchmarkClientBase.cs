using Microsoft.Extensions.Logging;
using PulseRPC;
using PulseRPC.Client;
using PulseRPC.Client.Channels;
using PulseRPC.Serialization;
using PulseRPC.Transport;
using PulseRPC.Benchmark.Core.Models;
using PulseRPC.Benchmark.Shared;

namespace PulseRPC.Benchmark.Scenarios;

/// <summary>
/// 基准测试客户端基类
/// 封装 PulseRPC 客户端初始化逻辑和通用功能
/// </summary>
[PulseClientGeneration(typeof(IBenchmarkService))]
public abstract class BenchmarkClientBase : IDisposable
{
    protected readonly ILogger Logger;
    protected readonly ILoggerFactory LoggerFactory;
    protected IChannelManager? ChannelManager;
    protected IBenchmarkService? BenchmarkService;
    private CancellationTokenSource? _cts;
    private bool _disposed = false;

    protected BenchmarkClientBase(ILoggerFactory loggerFactory)
    {
        LoggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        Logger = loggerFactory.CreateLogger(GetType());
    }

    /// <summary>
    /// 场景名称
    /// </summary>
    public abstract string ScenarioName { get; }

    /// <summary>
    /// 场景描述
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// 场景版本
    /// </summary>
    public virtual string Version => "1.0.0";

    /// <summary>
    /// 场景类别
    /// </summary>
    public virtual string Category => "Basic";

    /// <summary>
    /// 执行基准测试场景
    /// </summary>
    /// <param name="config">测试配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>测试结果</returns>
    public abstract Task<BenchmarkResult> ExecuteScenarioAsync(BenchmarkConfiguration config, CancellationToken cancellationToken = default);

    /// <summary>
    /// 初始化客户端
    /// </summary>
    /// <param name="config">配置信息</param>
    public virtual async Task InitializeAsync(BenchmarkConfiguration config)
    {
        Logger.LogInformation("正在初始化基准测试客户端: {ScenarioName}", ScenarioName);

        _cts = new CancellationTokenSource();

        // 创建序列化器
        var serializer = PulseRPCSerializerProvider.Instance;

        // 创建传输工厂
        var transportFactory = new TransportFactory(LoggerFactory);

        // 创建通道管理器
        ChannelManager = new ChannelManager();

        // 创建TCP通道
        var tcpOptions = config.TcpOptions;
        if (tcpOptions == null)
        {
            tcpOptions = new PulseRPC.Transport.TransportOptions
            {
                NoDelay = true,
                KeepAlive = true,
                AutoReconnect = false  // 基准测试中禁用自动重连
            };
        }

        var tcpTransport = await transportFactory.CreateTransportAsync(
            TransportType.Tcp, tcpOptions);

        var tcpChannel = new TransportChannel(
            "TcpChannel",
            tcpTransport,
            serializer,
            LoggerFactory.CreateLogger<TransportChannel>());

        ChannelManager.RegisterChannel("TcpChannel", tcpChannel, true);

        // 如果启用KCP，创建KCP通道
        if (config.EnableKcp)
        {
            var kcpOptions = config.KcpOptions;
            if (kcpOptions == null)
            {
                kcpOptions = new PulseRPC.Transport.TransportOptions
                {
                    Kcp = new KcpOptions
                    {
                        NoDelay = 1,
                        Interval = 10,
                        Resend = 2,
                        DisableFlowControl = false,
                        SendWindow = 32,
                        ReceiveWindow = 128
                    }
                };
            }

            var kcpTransport = await transportFactory.CreateTransportAsync(
                TransportType.Kcp, kcpOptions);

            var kcpChannel = new TransportChannel(
                "KcpChannel",
                kcpTransport,
                serializer,
                LoggerFactory.CreateLogger<TransportChannel>());

            ChannelManager.RegisterChannel("KcpChannel", kcpChannel);
        }

        // 获取服务代理
        BenchmarkService = ChannelManager.GetBenchmarkService();

        Logger.LogInformation("基准测试客户端初始化完成");
    }

    /// <summary>
    /// 连接到服务器
    /// </summary>
    /// <param name="host">服务器地址</param>
    /// <param name="tcpPort">TCP端口</param>
    /// <param name="kcpPort">KCP端口（可选）</param>
    public virtual async Task ConnectAsync(string host, int tcpPort, int kcpPort = 0)
    {
        if (ChannelManager == null)
            throw new InvalidOperationException("客户端尚未初始化");

        Logger.LogInformation("正在连接到服务器 {Host}:{TcpPort}", host, tcpPort);

        try
        {
            // 连接TCP通道
            var tcpChannel = ChannelManager.GetChannel("TcpChannel");
            await tcpChannel.ConnectAsync(host, tcpPort);

            // 如果启用KCP且提供了端口，连接KCP通道
            if (kcpPort > 0)
            {
                var kcpChannel = ChannelManager.GetChannel("KcpChannel");
                if (kcpChannel != null)
                {
                    await kcpChannel.ConnectAsync(host, kcpPort);
                    Logger.LogInformation("已连接到服务器 (TCP + KCP)");
                }
                else
                {
                    Logger.LogInformation("已连接到服务器 (TCP only)");
                }
            }
            else
            {
                Logger.LogInformation("已连接到服务器 (TCP only)");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "连接服务器失败");
            throw;
        }
    }

    /// <summary>
    /// 断开与服务器的连接
    /// </summary>
    public virtual async Task DisconnectAsync()
    {
        if (ChannelManager == null) return;

        Logger.LogInformation("正在断开服务器连接...");

        try
        {
            // 断开所有通道连接
            var channels = new[] { "TcpChannel", "KcpChannel" };

            foreach (var channelName in channels)
            {
                var channel = ChannelManager.GetChannel(channelName);
                if (channel != null)
                {
                    await channel.DisconnectAsync();
                }
            }

            Logger.LogInformation("已断开服务器连接");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "断开连接时发生错误");
        }
    }

    /// <summary>
    /// 生成测试数据
    /// </summary>
    /// <param name="size">数据大小（字节）</param>
    /// <returns>测试数据</returns>
    protected virtual byte[] GenerateTestData(int size)
    {
        var data = new byte[size];
        Random.Shared.NextBytes(data);
        return data;
    }

    /// <summary>
    /// 生成测试字符串
    /// </summary>
    /// <param name="length">字符串长度</param>
    /// <returns>测试字符串</returns>
    protected virtual string GenerateTestString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[Random.Shared.Next(s.Length)]).ToArray());
    }

    /// <summary>
    /// 获取默认配置
    /// </summary>
    /// <returns>默认配置</returns>
    public virtual BenchmarkConfiguration GetDefaultConfiguration()
    {
        return new BenchmarkConfiguration
        {
            Host = "localhost",
            TcpPort = 12345,
            KcpPort = 12346,
            EnableKcp = false,
            Iterations = 1000,
            ConcurrentConnections = 1,
            MessageSizeBytes = 1024,
            WarmupIterations = 100,
            TestIntervalMs = 0,
            EnableVerboseLogging = false,
            CollectResourceMetrics = true
        };
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public virtual void Dispose()
    {
        if (_disposed) return;

        try
        {
            _cts?.Cancel();
            _cts?.Dispose();

            var disconnectTask = DisconnectAsync();
            disconnectTask.Wait(TimeSpan.FromSeconds(5));

            ChannelManager?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "资源释放时发生错误");
        }
        finally
        {
            _disposed = true;
        }
    }
}
