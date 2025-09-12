using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Metrics.Abstractions;
using PulseRPC.Benchmark.Server.Configuration;
using PulseRPC.Benchmark.Server.Services;
using PulseRPC.Benchmark.Server.Extensions;
using PulseRPC.Benchmark.Shared;
using PulseRPC.Server;
using PulseRPC.Server.Services;
using PulseRPC.Server.Transport;

namespace PulseRPC.Benchmark.Server;

/// <summary>
/// 基准测试服务端宿主服务
/// 负责管理PulseRPC服务器的完整生命周期
/// </summary>
public class BenchmarkServerHost(
    ILogger<BenchmarkServerHost> logger,
    IServiceProvider serviceProvider,
    ServerConfiguration config,
    ServiceRegistry serviceRegistry,
    IServerChannelManager channelManager,
    IPulseRPCServer pulseServer,
    IMetricsCollector metricsCollector)
    : BackgroundService
{
    private readonly ILogger<BenchmarkServerHost> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly ServerConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
    private readonly IMetricsCollector _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
    private readonly IPulseRPCServer _pulseServer = pulseServer ?? throw new ArgumentNullException(nameof(pulseServer));
    private readonly ServiceRegistry _serviceRegistry = serviceRegistry ?? throw new ArgumentNullException(nameof(serviceRegistry));

    private readonly Lock _stateLock = new();
    private ServerState _currentState = ServerState.Stopped;

    /// <summary>
    /// 服务器当前状态
    /// </summary>
    public ServerState CurrentState
    {
        get
        {
            lock (_stateLock)
            {
                return _currentState;
            }
        }
        private set
        {
            lock (_stateLock)
            {
                if (_currentState == value)
                {
                    return;
                }

                var oldState = _currentState;
                _currentState = value;
                _logger.LogInformation("服务器状态变更: {OldState} -> {NewState}", oldState, value);
                StateChanged?.Invoke(oldState, value);
            }
        }
    }

    /// <summary>
    /// 服务器状态变更事件
    /// </summary>
    public event Action<ServerState, ServerState>? StateChanged;


    /// <summary>
    /// 启动服务端宿主
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("🚀 开始启动基准测试服务端宿主...");

            CurrentState = ServerState.Starting;

            // 启动各个子系统
            await StartSubsystemsAsync(stoppingToken);

            // 启动PulseRPC服务器
            await StartRpcServerAsync(stoppingToken);

            CurrentState = ServerState.Running;
            _logger.LogInformation("✅ 基准测试服务端宿主启动完成");

            // 等待停止信号
            await WaitForShutdownAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("🛑 服务端宿主接收到停止信号");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 服务端宿主启动失败");
            CurrentState = ServerState.Faulted;
            throw;
        }
        finally
        {
            await ShutdownAsync();
        }
    }

    /// <summary>
    /// 启动子系统
    /// </summary>
    private async Task StartSubsystemsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔧 启动子系统...");

        // 启动指标系统
        _logger.LogInformation("📊 指标系统已启动");

        // 开始收集服务器启动指标
        await _metricsCollector.CollectAsync("server_startup", new
        {
            Timestamp = DateTime.UtcNow,
            Port = _config.Port,
            MetricsPort = _config.MetricsPort,
            MaxConnections = _config.MaxConnections
        }, cancellationToken);
    }

    /// <summary>
    /// 启动PulseRPC服务器
    /// </summary>
    private async Task StartRpcServerAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🌐 启动PulseRPC服务器...");

        try
        {
            // 注册基准测试服务
            var benchmarkService = _serviceProvider.GetRequiredService<IBenchmarkService>();

            // 启动服务器监听已配置的传输端口
            await _pulseServer.StartAsync(cancellationToken);

            _logger.LogInformation("✅ PulseRPC服务器已启动在端口 {Port}", _config.Port);

            _serviceRegistry.RegisterService<IBenchmarkService, BenchmarkServiceImpl>((BenchmarkServiceImpl)benchmarkService);

            // 记录服务器启动指标
            await _metricsCollector.CollectAsync("server_started", new
            {
                Timestamp = DateTime.UtcNow,
                Port = _config.Port,
                CompressionEnabled = _config.EnableCompression,
                MaxConnections = _config.MaxConnections
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ PulseRPC服务器启动失败");
            throw;
        }
    }

    /// <summary>
    /// 等待关闭信号
    /// </summary>
    private static async Task WaitForShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 正常的停止信号，不需要记录错误
        }
    }

    /// <summary>
    /// 优雅关闭
    /// </summary>
    private async Task ShutdownAsync()
    {
        if (CurrentState is ServerState.Stopped or ServerState.Stopping)
        {
            return;
        }

        _logger.LogInformation("🛑 开始优雅关闭服务端宿主...");
        CurrentState = ServerState.Stopping;

        try
        {
            // 停止服务器
            try
            {
                await _pulseServer.StopAsync(CancellationToken.None);
            }
            catch (Exception stopEx)
            {
                _logger.LogWarning(stopEx, "停止服务器时发生非致命错误");
            }

            // 记录服务器停止指标
            await _metricsCollector.CollectAsync("server_stopped", new
            {
                Timestamp = DateTime.UtcNow,
                GracefulShutdown = true
            }, CancellationToken.None);

            CurrentState = ServerState.Stopped;
            _logger.LogInformation("✅ 服务端宿主已优雅关闭");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 服务端关闭过程中发生错误");
            CurrentState = ServerState.Faulted;
        }
    }
}

/// <summary>
/// 服务器状态枚举
/// </summary>
public enum ServerState
{
    /// <summary>已停止</summary>
    Stopped,
    /// <summary>启动中</summary>
    Starting,
    /// <summary>运行中</summary>
    Running,
    /// <summary>停止中</summary>
    Stopping,
    /// <summary>故障状态</summary>
    Faulted
}
