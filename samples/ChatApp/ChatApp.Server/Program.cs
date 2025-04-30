using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChatApp.Server
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 添加服务
            builder.Services.AddLogging();
            builder.Services.AddSingleton<ChatTCPServer>();

            var app = builder.Build();

            // 启动TCP服务器
            var tcpServer = app.Services.GetRequiredService<ChatTCPServer>();
            var port = 12345; // 可以从配置中获取
            var endpoint = new IPEndPoint(IPAddress.Any, port);
            _ = tcpServer.StartAsync(endpoint);

            app.Lifetime.ApplicationStopping.Register(() =>
            {
                tcpServer.Stop();
            });

            await app.RunAsync();
        }
    }
}
