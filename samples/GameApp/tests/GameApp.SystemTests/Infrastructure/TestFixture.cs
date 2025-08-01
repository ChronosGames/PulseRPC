using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace GameApp.SystemTests.Infrastructure
{
    /// <summary>
    /// 系统测试基础设施 - 管理测试环境的生命周期
    /// </summary>
    public class TestFixture : IAsyncLifetime
    {
        public WebApplicationFactory<Program> AuthServerFactory { get; private set; } = null!;
        public IConfiguration TestConfiguration { get; private set; } = null!;
        public ILogger<TestFixture> Logger { get; private set; } = null!;

        // 测试环境配置
        public string AuthServerUrl => "http://localhost";
        public string GameServerAddress => "localhost";
        public int GameServerTcpPort => TestConfiguration.GetValue<int>("GameServerOptions:TcpPort");
        public int GameServerKcpPort => TestConfiguration.GetValue<int>("GameServerOptions:KcpPort");
        public int BattleServerTcpPort => TestConfiguration.GetValue<int>("BattleServerOptions:TcpPort");
        public int BattleServerKcpPort => TestConfiguration.GetValue<int>("BattleServerOptions:KcpPort");

        public async Task InitializeAsync()
        {
            // 加载测试配置
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.Test.json", optional: false)
                .AddEnvironmentVariables()
                .Build();

            TestConfiguration = configuration;

            // 创建日志记录器
            using var loggerFactory = LoggerFactory.Create(builder =>
                builder.AddConsole().SetMinimumLevel(LogLevel.Information));
            Logger = loggerFactory.CreateLogger<TestFixture>();

            Logger.LogInformation("正在初始化系统测试环境...");

            // 创建 AuthServer 测试工厂
            AuthServerFactory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseConfiguration(configuration);

                    builder.ConfigureAppConfiguration((context, config) =>
                    {
                        config.AddInMemoryCollection(new[]
                        {
                            new KeyValuePair<string, string?>("UseInMemoryDatabase", "true"),
                            new KeyValuePair<string, string?>("ConnectionStrings:MongoDB", ""),
                            new KeyValuePair<string, string?>("ConnectionStrings:Redis", "")
                        });
                    });

                    builder.ConfigureServices(services =>
                    {
                        // 移除实际的数据库服务，使用内存模拟
                        var descriptors = services.Where(d =>
                            d.ServiceType.Name.Contains("Mongo") ||
                            d.ServiceType.Name.Contains("Redis") ||
                            d.ServiceType == typeof(MongoDB.Driver.IMongoDatabase) ||
                            d.ServiceType == typeof(StackExchange.Redis.IDatabase)).ToList();

                        foreach (var descriptor in descriptors)
                        {
                            services.Remove(descriptor);
                        }

                        // 注册模拟服务
                        services.AddSingleton<MongoDB.Driver.IMongoDatabase>(provider =>
                        {
                            var client = new MongoDB.Driver.MongoClient("mongodb://localhost:27017");
                            return client.GetDatabase("test_db");
                        });

                        services.AddSingleton<StackExchange.Redis.IDatabase>(provider =>
                        {
                            var connection = StackExchange.Redis.ConnectionMultiplexer.Connect("localhost:6379");
                            return connection.GetDatabase();
                        });
                    });
                });

            Logger.LogInformation("系统测试环境初始化完成");
        }

        public async Task DisposeAsync()
        {
            Logger?.LogInformation("正在清理系统测试环境...");

            AuthServerFactory?.Dispose();

            Logger?.LogInformation("系统测试环境清理完成");
        }

        /// <summary>
        /// 创建测试用的HTTP客户端
        /// </summary>
        public HttpClient CreateAuthServerClient()
        {
            return AuthServerFactory.CreateClient();
        }

        /// <summary>
        /// 等待服务准备就绪
        /// </summary>
        public async Task WaitForServicesReadyAsync(TimeSpan? timeout = null)
        {
            timeout ??= TimeSpan.FromSeconds(30);

            using var client = CreateAuthServerClient();

            var endTime = DateTime.UtcNow.Add(timeout.Value);

            while (DateTime.UtcNow < endTime)
            {
                try
                {
                    var response = await client.GetAsync("/ready");
                    if (response.IsSuccessStatusCode)
                    {
                        Logger.LogInformation("服务已准备就绪");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogDebug("等待服务就绪: {Error}", ex.Message);
                }

                await Task.Delay(1000);
            }

            throw new TimeoutException("等待服务就绪超时");
        }
    }
}
