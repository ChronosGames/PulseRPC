using Microsoft.Extensions.Logging;
using PulseRPC;
using PulseRPC.Client;
using PulseRPC.Client.Channels;
using PulseRPC.Serialization;
using PulseRPC.Transport;
using PulseRPC.Benchmark.Core.Models;
using PulseRPC.Benchmark.Shared;
using PulseRPC.Client.Core;

namespace PulseRPC.Benchmark.Scenarios;

/// <summary>
/// 基准测试客户端基类
/// 封装 PulseRPC 客户端初始化逻辑和通用功能
/// </summary>
[PulseClientGeneration(typeof(IBenchmarkHub))]
public abstract class BenchmarkClientBase : IDisposable
{
    protected readonly ILogger Logger;
    protected readonly ILoggerFactory LoggerFactory;
    protected IPulseClient? PulseClient;
    protected IBenchmarkHub? BenchmarkService;
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

        // 创建通道管理器
        var builder = new PulseClientBuilder();
        builder.AddTransport("TcpChannel", TransportType.Tcp, "localhost", 12345, new TcpTransportOptions
        {
            NoDelay = true,
            KeepAlive = true,
            ConnectionTimeout = 5000
        });

        // 如果启用KCP，创建KCP通道
        if (config.EnableKcp)
        {
            var kcpOptions = config.KcpOptions ?? new KcpTransportOptions
            {
                    NoDelay = true,
                    Interval = 10,
                    Resend = 2,
                    DisableFlowControl = false,
                    SendWindow = 32,
                    RecvWindow = 128
            };

            builder.AddTransport("KcpChannel", TransportType.Kcp, "localhost", 12345, kcpOptions);
        }

        PulseClient = builder.Build();

        // 获取服务代理
        BenchmarkService = await this.PulseClient.GetServiceAsync<IBenchmarkHub>();

        Logger.LogInformation("基准测试客户端初始化完成");
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

            PulseClient?.Dispose();
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
