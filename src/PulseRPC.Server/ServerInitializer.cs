using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.MessageRegistration;

namespace PulseRPC.Server;

/// <summary>
/// PulseRPC服务器初始化工具
/// </summary>
public class ServerInitializer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ServerInitializer> _logger;
    private readonly MessageDispatcher _dispatcher;

    /// <summary>
    /// 初始化服务器初始化器
    /// </summary>
    /// <param name="serviceProvider">服务提供程序</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="dispatcher">消息分发器</param>
    public ServerInitializer(
        IServiceProvider serviceProvider,
        ILogger<ServerInitializer> logger,
        MessageDispatcher dispatcher)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <summary>
    /// 运行初始化过程
    /// </summary>
    public void Initialize()
    {
        _logger.LogInformation("开始初始化PulseRPC服务器...");

        // 1. 注册所有消息定义
        ServerMessageRegistration.Initialize();

        // 2. 注册所有处理器
        RegisterMessageHandlers();

        // 3. 初始化TcpServer
        var tcpServer = _serviceProvider.GetRequiredService<TcpServer>();
        tcpServer.Start();

        _logger.LogInformation("PulseRPC服务器初始化完成");
    }

    /// <summary>
    /// 从指定程序集注册消息处理器
    /// </summary>
    /// <param name="assembly">要扫描的程序集</param>
    public void RegisterHandlersFromAssembly(Assembly assembly)
    {
        _dispatcher.RegisterHandlersFromAssembly(assembly);
    }

    /// <summary>
    /// 从当前应用程序域中注册所有消息处理器
    /// </summary>
    public void RegisterMessageHandlers()
    {
        _logger.LogInformation("正在扫描并注册消息处理器...");
        _dispatcher.RegisterHandlersFromCurrentAppDomain();
        _logger.LogInformation("消息处理器注册完成");
    }
}
