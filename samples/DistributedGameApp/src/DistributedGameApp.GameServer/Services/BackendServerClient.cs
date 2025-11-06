using DistributedGameApp.Shared.Domain.Matchmaking;
using DistributedGameApp.Shared.Hubs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PulseRPC;
using PulseRPC.Client;
using PulseRPC.Transport;
using PulseRPC.Messaging;

namespace DistributedGameApp.GameServer.Services;

/// <summary>
/// BackendServer 客户端 - 用于从 GameServer 连接到 BackendServer
/// </summary>
public class BackendServerClient : IDisposable, IAsyncDisposable
{
    private readonly ILogger<BackendServerClient> _logger;
    private readonly IConfiguration _configuration;
    private IPulseClient? _client;
    private IClientChannel? _channel;
    private bool _isInitialized;
    private bool _isDisposed;
    private CancellationTokenSource? _cts;

    public BackendServerClient(
        IConfiguration configuration,
        ILogger<BackendServerClient> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// 初始化客户端连接
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            _logger.LogWarning("BackendServerClient 已经初始化");
            return;
        }

        try
        {
            _logger.LogInformation("正在初始化 BackendServerClient...");

            // 从配置读取 BackendServer 连接信息
            var backendConfig = _configuration.GetSection("BackendServer");
            var host = backendConfig.GetValue<string>("Host") ?? "localhost";
            var port = backendConfig.GetValue<int>("Port", 10080);

            _logger.LogInformation("连接到 BackendServer: {Host}:{Port}", host, port);

            _cts = new CancellationTokenSource();

            // 创建客户端（不预先添加连接）
            _client = new PulseClientBuilder()
                .WithTransportOptions(TransportType.TCP, new TcpTransportOptions
                {
                    ConnectionTimeout = 30000,
                    NoDelay = true,
                    SendBufferSize = 8192,
                    RecvBufferSize = 8192,
                })
                .Build();

            // 初始化客户端
            await _client.InitializeAsync(cancellationToken);

            // 手动连接到BackendServer
            var descriptor = ConnectionDescriptor.CreateTcp(
                "BackendServer",
                "DistributedGameApp",
                host,
                port,
                ConnectionStrategy.Persistent);

            _channel = await _client.ConnectAsync(descriptor, cancellationToken);

            _isInitialized = true;
            _logger.LogInformation("BackendServerClient 初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BackendServerClient 初始化失败");
            throw;
        }
    }

    /// <summary>
    /// 确保客户端已初始化
    /// </summary>
    private void EnsureInitialized()
    {
        if (!_isInitialized || _channel == null)
        {
            throw new InvalidOperationException("BackendServerClient 未初始化，请先调用 InitializeAsync");
        }

        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(BackendServerClient));
        }
    }

    /// <summary>
    /// 开始匹配
    /// </summary>
    public async Task<MatchmakingResponse> StartMatchmakingAsync(MatchmakingRequest request)
    {
        EnsureInitialized();

        try
        {
            _logger.LogInformation("调用 BackendServer 开始匹配: PlayerId={PlayerId}, Type={MatchType}",
                request.PlayerId, request.MatchType);

            // 使用 InvokeAsync 调用远程方法
            var response = await _channel!.InvokeAsync<MatchmakingRequest, MatchmakingResponse>(
                nameof(IBackendHub),
                nameof(IBackendHub.StartMatchmakingAsync),
                request);

            if (response.Success)
            {
                _logger.LogInformation("匹配请求已提交: PlayerId={PlayerId}", request.PlayerId);
            }
            else
            {
                _logger.LogWarning("匹配请求失败: PlayerId={PlayerId}, Message={Message}",
                    request.PlayerId, response.Message);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "调用 BackendServer 匹配服务失败: PlayerId={PlayerId}", request.PlayerId);
            return new MatchmakingResponse
            {
                Success = false,
                Message = $"匹配服务异常: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// 取消匹配
    /// </summary>
    public async Task<bool> CancelMatchmakingAsync()
    {
        EnsureInitialized();

        try
        {
            _logger.LogInformation("调用 BackendServer 取消匹配");

            // 使用 InvokeAsync 调用远程方法（无参数方法）
            var result = await _channel!.InvokeAsync<object?, bool>(
                nameof(IBackendHub),
                nameof(IBackendHub.CancelMatchmakingAsync),
                null);

            if (result)
            {
                _logger.LogInformation("取消匹配成功");
            }
            else
            {
                _logger.LogWarning("取消匹配失败");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "调用 BackendServer 取消匹配失败");
            return false;
        }
    }

    /// <summary>
    /// 检查连接状态
    /// </summary>
    public bool IsConnected => _isInitialized && !_isDisposed && _client != null;

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _logger.LogInformation("正在释放 BackendServerClient...");

        _cts?.Cancel();
        _cts?.Dispose();
        _client?.Dispose();

        _isDisposed = true;
        _logger.LogInformation("BackendServerClient 已释放");
    }

    /// <summary>
    /// 异步释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _logger.LogInformation("正在异步释放 BackendServerClient...");

        if (_cts != null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }

        _client?.Dispose();

        _isDisposed = true;
        _logger.LogInformation("BackendServerClient 已释放");
    }
}
