using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC;
using PulseRPC.Server;
using ChatApp.Server.Services;
using ChatApp.Server.Hubs;

namespace ChatApp.Server
{
    /// <summary>
    /// 服务器启动类
    /// </summary>
    public class Startup
    {
        /// <summary>
        /// 配置服务
        /// </summary>
        public void ConfigureServices(IServiceCollection services)
        {
            // 添加日志服务
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            });
            
            // 注册PulseRPC基础服务
            services.AddPulseServerServices();
            
            // 注册服务实现
            services.AddSingleton<ChatService>();
            services.AddSingleton<ChatHub>();
            
            // 注册服务注册表
            services.AddSingleton<ServiceRegistry>();
            
            // 替换默认的消息分发器为服务分发器
            services.AddSingleton<IMessageDispatcher, ServiceDispatcher>();
        }
        
        /// <summary>
        /// 配置应用
        /// </summary>
        public void Configure(IServiceProvider provider)
        {
            // 获取服务注册表
            var serviceRegistry = provider.GetRequiredService<ServiceRegistry>();
            
            // 扫描并注册所有服务
            serviceRegistry.ScanAndRegisterServices(new[] { typeof(Startup).Assembly });
            
            // 启动网络服务器
            var networkServer = provider.GetRequiredService<NetworkServer>();
            networkServer.Start(7000);
            
            // 获取日志记录器
            var logger = provider.GetRequiredService<ILogger<Startup>>();
            logger.LogInformation("聊天服务器已启动，监听端口: 7000");
        }
    }
} 