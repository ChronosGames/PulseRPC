using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Server;
using PulseRPC.Server.Extensions;
using PulseRPC.Server.ServiceRegistration;

namespace ServiceRegistrationExample
{
    /// <summary>
    /// 服务注册示例程序
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== PulseRPC 服务注册示例 ===\n");

            // 演示场景菜单
            await ShowMenu();
        }

        /// <summary>
        /// 显示演示菜单
        /// </summary>
        static async Task ShowMenu()
        {
            while (true)
            {
                Console.WriteLine("请选择演示场景:");
                Console.WriteLine("1. 基础服务注册");
                Console.WriteLine("2. 自动服务注册");
                Console.WriteLine("3. 多服务注册");
                Console.WriteLine("4. Consul 服务注册");
                Console.WriteLine("5. 健康检查演示");
                Console.WriteLine("6. 心跳演示");
                Console.WriteLine("7. 服务统计信息");
                Console.WriteLine("8. 完整的服务生命周期");
                Console.WriteLine("0. 退出");
                Console.Write("输入选择: ");

                var choice = Console.ReadLine();
                Console.WriteLine();

                try
                {
                    switch (choice)
                    {
                        case "1":
                            await BasicServiceRegistrationDemo();
                            break;
                        case "2":
                            await AutoServiceRegistrationDemo();
                            break;
                        case "3":
                            await MultiServiceRegistrationDemo();
                            break;
                        case "4":
                            await ConsulServiceRegistrationDemo();
                            break;
                        case "5":
                            await HealthCheckDemo();
                            break;
                        case "6":
                            await HeartbeatDemo();
                            break;
                        case "7":
                            await ServiceStatisticsDemo();
                            break;
                        case "8":
                            await CompleteLifecycleDemo();
                            break;
                        case "0":
                            Console.WriteLine("感谢使用 PulseRPC 服务注册示例！");
                            return;
                        default:
                            Console.WriteLine("无效选择，请重试。\n");
                            continue;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"演示过程中发生错误: {ex.Message}\n");
                }

                Console.WriteLine("按任意键继续...");
                Console.ReadKey();
                Console.WriteLine("\n");
            }
        }

        /// <summary>
        /// 1. 基础服务注册演示
        /// </summary>
        static async Task BasicServiceRegistrationDemo()
        {
            Console.WriteLine("=== 基础服务注册演示 ===");

            var services = new ServiceCollection();
            
            // 配置日志
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

            // 添加服务注册（使用默认配置）
            services.AddPulseRpcServiceRegistration(options =>
            {
                options.Enabled = true;
                options.RegistryType = ServiceRegistryType.Custom; // 使用内存注册演示
                options.EnableHealthCheck = false; // 简化演示
                options.EnableHeartbeat = false;
            });

            var serviceProvider = services.BuildServiceProvider();
            var registrar = serviceProvider.GetServiceRegistrar();
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

            try
            {
                // 手动注册服务
                var serviceInfo = new ServiceInfo
                {
                    ServiceId = "api-service-001",
                    ServiceName = "api-service",
                    Host = "127.0.0.1",
                    Port = 8001,
                    Tags = new Dictionary<string, string>
                    {
                        ["version"] = "1.0.0",
                        ["environment"] = "development"
                    },
                    Metadata = new Dictionary<string, object>
                    {
                        ["description"] = "API 服务实例",
                        ["startTime"] = DateTime.UtcNow
                    }
                };

                Console.WriteLine($"注册服务: {serviceInfo.ServiceName}({serviceInfo.ServiceId})");
                var registered = await registrar.RegisterServiceAsync(serviceInfo);
                Console.WriteLine($"注册结果: {(registered ? "成功" : "失败")}");

                // 查看注册的服务
                var registeredServices = registrar.GetRegisteredServices();
                Console.WriteLine($"\n当前注册的服务数量: {registeredServices.Count}");
                foreach (var service in registeredServices)
                {
                    Console.WriteLine($"  - {service.ServiceInfo.ServiceName}({service.ServiceInfo.ServiceId}) @ {service.ServiceInfo.Host}:{service.ServiceInfo.Port}");
                }

                await Task.Delay(2000);

                // 注销服务
                Console.WriteLine($"\n注销服务: {serviceInfo.ServiceId}");
                var unregistered = await registrar.UnregisterServiceAsync(serviceInfo.ServiceId);
                Console.WriteLine($"注销结果: {(unregistered ? "成功" : "失败")}");

                // 再次查看注册的服务
                registeredServices = registrar.GetRegisteredServices();
                Console.WriteLine($"\n当前注册的服务数量: {registeredServices.Count}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "基础服务注册演示失败");
                throw;
            }
            finally
            {
                await serviceProvider.DisposeAsync();
            }
        }

        /// <summary>
        /// 2. 自动服务注册演示
        /// </summary>
        static async Task AutoServiceRegistrationDemo()
        {
            Console.WriteLine("=== 自动服务注册演示 ===");

            var hostBuilder = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // 使用自动配置的服务注册
                    services.AddPulseRpcServiceRegistration("user-service", 8002, options =>
                    {
                        options.RegistryType = ServiceRegistryType.Custom;
                        options.DefaultTags.Add("service-type", "business");
                        options.DefaultTags.Add("protocol", "tcp");
                        options.EnableHealthCheck = false;
                        options.EnableHeartbeat = false;
                    });
                });

            using var host = hostBuilder.Build();

            Console.WriteLine("启动主机，将自动注册服务...");
            await host.StartAsync();

            // 获取服务注册器并查看状态
            var registrar = host.Services.GetServiceRegistrar();
            await Task.Delay(1000); // 等待注册完成

            var registeredServices = registrar.GetRegisteredServices();
            Console.WriteLine($"\n自动注册的服务数量: {registeredServices.Count}");
            foreach (var service in registeredServices)
            {
                Console.WriteLine($"  - 服务: {service.ServiceInfo.ServiceName}");
                Console.WriteLine($"    ID: {service.ServiceInfo.ServiceId}");
                Console.WriteLine($"    地址: {service.ServiceInfo.Host}:{service.ServiceInfo.Port}");
                Console.WriteLine($"    标签: {string.Join(", ", service.ServiceInfo.Tags?.Select(t => $"{t.Key}={t.Value}") ?? Array.Empty<string>())}");
                Console.WriteLine($"    注册时间: {service.RegisteredAt:yyyy-MM-dd HH:mm:ss}");
            }

            Console.WriteLine("\n停止主机，将自动注销服务...");
            await host.StopAsync();
        }

        /// <summary>
        /// 3. 多服务注册演示
        /// </summary>
        static async Task MultiServiceRegistrationDemo()
        {
            Console.WriteLine("=== 多服务注册演示 ===");

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

            // 准备多个服务信息
            var servicesInfo = new[]
            {
                new ServiceInfo
                {
                    ServiceId = "gateway-001",
                    ServiceName = "gateway",
                    Host = "127.0.0.1",
                    Port = 8080,
                    Tags = new Dictionary<string, string> { ["role"] = "gateway", ["version"] = "2.0.0" }
                },
                new ServiceInfo
                {
                    ServiceId = "auth-001",
                    ServiceName = "auth-service",
                    Host = "127.0.0.1",
                    Port = 8081,
                    Tags = new Dictionary<string, string> { ["role"] = "auth", ["version"] = "1.5.0" }
                },
                new ServiceInfo
                {
                    ServiceId = "data-001",
                    ServiceName = "data-service",
                    Host = "127.0.0.1",
                    Port = 8082,
                    Tags = new Dictionary<string, string> { ["role"] = "data", ["version"] = "1.0.0" }
                }
            };

            // 添加多服务注册
            services.AddPulseRpcServiceRegistrations(servicesInfo, options =>
            {
                options.RegistryType = ServiceRegistryType.Custom;
                options.EnableHealthCheck = false;
                options.EnableHeartbeat = false;
            });

            var serviceProvider = services.BuildServiceProvider();
            var registrar = serviceProvider.GetServiceRegistrar();

            Console.WriteLine("注册多个服务...");
            
            // 手动注册额外的服务
            foreach (var serviceInfo in servicesInfo.Take(2)) // 只手动注册前两个，第三个通过自动注册
            {
                await registrar.RegisterServiceAsync(serviceInfo);
                Console.WriteLine($"已注册: {serviceInfo.ServiceName}");
            }

            await Task.Delay(1000);

            // 查看所有注册的服务
            var registeredServices = registrar.GetRegisteredServices();
            Console.WriteLine($"\n总计注册服务: {registeredServices.Count}");
            
            var serviceGroups = registeredServices.GroupBy(s => s.ServiceInfo.ServiceName);
            foreach (var group in serviceGroups)
            {
                Console.WriteLine($"\n服务组: {group.Key}");
                foreach (var service in group)
                {
                    Console.WriteLine($"  实例: {service.ServiceInfo.ServiceId} @ {service.ServiceInfo.Host}:{service.ServiceInfo.Port}");
                }
            }

            // 获取统计信息
            var stats = registrar.GetStatistics();
            Console.WriteLine($"\n=== 服务统计 ===");
            Console.WriteLine($"总注册服务: {stats.TotalRegistered}");
            Console.WriteLine($"健康服务: {stats.HealthyServices}");
            Console.WriteLine($"不健康服务: {stats.UnhealthyServices}");
            Console.WriteLine($"状态未知服务: {stats.UnknownStatusServices}");

            await serviceProvider.DisposeAsync();
        }

        /// <summary>
        /// 4. Consul 服务注册演示
        /// </summary>
        static async Task ConsulServiceRegistrationDemo()
        {
            Console.WriteLine("=== Consul 服务注册演示 ===");
            Console.WriteLine("注意: 此演示需要本地运行 Consul 服务器");
            Console.WriteLine("如果没有 Consul，演示将使用模拟注册中心\n");

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

            try
            {
                // 添加 Consul 服务注册
                services.AddPulseRpcConsulRegistration("http://localhost:8500", options =>
                {
                    options.DefaultTags.Add("datacenter", "dc1");
                    options.DefaultTags.Add("environment", "demo");
                    options.EnableHealthCheck = true;
                    options.HealthCheckInterval = TimeSpan.FromSeconds(10);
                    options.EnableHeartbeat = true;
                    options.HeartbeatInterval = TimeSpan.FromSeconds(5);
                });

                var serviceProvider = services.BuildServiceProvider();
                var registrar = serviceProvider.GetServiceRegistrar();

                // 注册一个演示服务
                var serviceInfo = new ServiceInfo
                {
                    ServiceId = "demo-consul-001",
                    ServiceName = "demo-service",
                    Host = "127.0.0.1",
                    Port = 8003,
                    Tags = new Dictionary<string, string>
                    {
                        ["protocol"] = "http",
                        ["version"] = "1.0.0"
                    }
                };

                Console.WriteLine("向 Consul 注册服务...");
                var registered = await registrar.RegisterServiceAsync(serviceInfo);
                Console.WriteLine($"注册结果: {(registered ? "成功" : "失败")}");

                if (registered)
                {
                    Console.WriteLine("服务已注册到 Consul，您可以通过 Consul UI 查看");
                    Console.WriteLine("访问 http://localhost:8500 查看 Consul 管理界面");
                    
                    Console.WriteLine("\n等待 10 秒后注销服务...");
                    await Task.Delay(10000);

                    await registrar.UnregisterServiceAsync(serviceInfo.ServiceId);
                    Console.WriteLine("服务已从 Consul 注销");
                }

                await serviceProvider.DisposeAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Consul 演示失败 (可能是 Consul 未启动): {ex.Message}");
                Console.WriteLine("请确保 Consul 服务器正在运行: consul agent -dev");
            }
        }

        /// <summary>
        /// 5. 健康检查演示
        /// </summary>
        static async Task HealthCheckDemo()
        {
            Console.WriteLine("=== 健康检查演示 ===");

            var hostBuilder = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddPulseRpcServiceRegistration("health-demo", 8004, options =>
                    {
                        options.RegistryType = ServiceRegistryType.Custom;
                        options.EnableHealthCheck = true;
                        options.HealthCheckInterval = TimeSpan.FromSeconds(3);
                        options.HealthCheckTimeout = TimeSpan.FromSeconds(2);
                        options.EnableHeartbeat = false;
                    });
                });

            using var host = hostBuilder.Build();
            await host.StartAsync();

            var registrar = host.Services.GetServiceRegistrar();
            await Task.Delay(1000);

            var registeredServices = registrar.GetRegisteredServices();
            if (registeredServices.Count > 0)
            {
                var serviceId = registeredServices[0].ServiceInfo.ServiceId;
                
                Console.WriteLine("开始健康检查演示...");
                Console.WriteLine($"服务ID: {serviceId}");
                Console.WriteLine("将模拟健康状态变化...\n");

                // 模拟健康状态变化
                var statuses = new[] { HealthStatus.Healthy, HealthStatus.Unhealthy, HealthStatus.Healthy };
                
                foreach (var status in statuses)
                {
                    Console.WriteLine($"设置健康状态为: {status}");
                    await registrar.UpdateHealthStatusAsync(serviceId, status, $"模拟状态: {status}");
                    
                    await Task.Delay(2000);
                    
                    // 显示当前状态
                    var currentServices = registrar.GetRegisteredServices(serviceId);
                    if (currentServices.Count > 0)
                    {
                        var service = currentServices[0];
                        Console.WriteLine($"当前健康状态: {service.HealthStatus}");
                        Console.WriteLine($"最后检查时间: {service.LastHealthCheck:HH:mm:ss}");
                    }
                    Console.WriteLine();
                }

                // 显示统计信息
                var stats = registrar.GetStatistics();
                Console.WriteLine("=== 健康统计 ===");
                Console.WriteLine($"健康服务: {stats.HealthyServices}");
                Console.WriteLine($"不健康服务: {stats.UnhealthyServices}");
                Console.WriteLine($"状态未知: {stats.UnknownStatusServices}");
            }

            await host.StopAsync();
        }

        /// <summary>
        /// 6. 心跳演示
        /// </summary>
        static async Task HeartbeatDemo()
        {
            Console.WriteLine("=== 心跳演示 ===");

            var hostBuilder = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddPulseRpcServiceRegistration("heartbeat-demo", 8005, options =>
                    {
                        options.RegistryType = ServiceRegistryType.Custom;
                        options.EnableHealthCheck = false;
                        options.EnableHeartbeat = true;
                        options.HeartbeatInterval = TimeSpan.FromSeconds(2);
                        options.HeartbeatTimeout = TimeSpan.FromSeconds(1);
                    });
                });

            using var host = hostBuilder.Build();
            await host.StartAsync();

            Console.WriteLine("心跳服务已启动，每 2 秒发送一次心跳");
            Console.WriteLine("观察日志输出中的心跳信息...\n");

            // 运行 10 秒观察心跳
            for (int i = 0; i < 5; i++)
            {
                await Task.Delay(2000);
                Console.WriteLine($"心跳运行中... ({i + 1}/5)");
            }

            await host.StopAsync();
            Console.WriteLine("心跳演示完成");
        }

        /// <summary>
        /// 7. 服务统计信息演示
        /// </summary>
        static async Task ServiceStatisticsDemo()
        {
            Console.WriteLine("=== 服务统计信息演示 ===");

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

            // 注册多个不同健康状态的服务
            services.AddPulseRpcServiceRegistration(options =>
            {
                options.RegistryType = ServiceRegistryType.Custom;
                options.EnableHealthCheck = false;
                options.EnableHeartbeat = false;
            });

            var serviceProvider = services.BuildServiceProvider();
            var registrar = serviceProvider.GetServiceRegistrar();

            // 注册多个服务
            var servicesInfo = new[]
            {
                new ServiceInfo { ServiceId = "web-01", ServiceName = "web-service", Host = "127.0.0.1", Port = 8010 },
                new ServiceInfo { ServiceId = "web-02", ServiceName = "web-service", Host = "127.0.0.1", Port = 8011 },
                new ServiceInfo { ServiceId = "api-01", ServiceName = "api-service", Host = "127.0.0.1", Port = 8020 },
                new ServiceInfo { ServiceId = "api-02", ServiceName = "api-service", Host = "127.0.0.1", Port = 8021 },
                new ServiceInfo { ServiceId = "db-01", ServiceName = "database", Host = "127.0.0.1", Port = 5432 }
            };

            foreach (var serviceInfo in servicesInfo)
            {
                await registrar.RegisterServiceAsync(serviceInfo);
            }

            // 设置不同的健康状态
            await registrar.UpdateHealthStatusAsync("web-01", HealthStatus.Healthy);
            await registrar.UpdateHealthStatusAsync("web-02", HealthStatus.Healthy);
            await registrar.UpdateHealthStatusAsync("api-01", HealthStatus.Healthy);
            await registrar.UpdateHealthStatusAsync("api-02", HealthStatus.Unhealthy);
            await registrar.UpdateHealthStatusAsync("db-01", HealthStatus.Unknown);

            // 显示统计信息
            var stats = registrar.GetStatistics();
            
            Console.WriteLine("=== 服务注册统计 ===");
            Console.WriteLine($"总注册服务: {stats.TotalRegistered}");
            Console.WriteLine($"健康服务: {stats.HealthyServices}");
            Console.WriteLine($"不健康服务: {stats.UnhealthyServices}");
            Console.WriteLine($"状态未知服务: {stats.UnknownStatusServices}");
            
            Console.WriteLine("\n=== 按服务名分组 ===");
            foreach (var service in stats.Services)
            {
                Console.WriteLine($"{service.Key}: {service.Value} 个实例");
            }

            Console.WriteLine("\n=== 详细服务信息 ===");
            var allServices = registrar.GetRegisteredServices();
            foreach (var service in allServices)
            {
                Console.WriteLine($"服务: {service.ServiceInfo.ServiceName}");
                Console.WriteLine($"  ID: {service.ServiceInfo.ServiceId}");
                Console.WriteLine($"  地址: {service.ServiceInfo.Host}:{service.ServiceInfo.Port}");
                Console.WriteLine($"  健康状态: {service.HealthStatus}");
                Console.WriteLine($"  注册时间: {service.RegisteredAt:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"  最后检查: {service.LastHealthCheck:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine();
            }

            await serviceProvider.DisposeAsync();
        }

        /// <summary>
        /// 8. 完整的服务生命周期演示
        /// </summary>
        static async Task CompleteLifecycleDemo()
        {
            Console.WriteLine("=== 完整服务生命周期演示 ===");

            var hostBuilder = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddPulseRpcServiceRegistration("lifecycle-demo", 8006, options =>
                    {
                        options.RegistryType = ServiceRegistryType.Custom;
                        options.EnableHealthCheck = true;
                        options.HealthCheckInterval = TimeSpan.FromSeconds(5);
                        options.EnableHeartbeat = true;
                        options.HeartbeatInterval = TimeSpan.FromSeconds(3);
                        options.AutoUnregisterOnShutdown = true;
                        options.DefaultTags.Add("demo", "lifecycle");
                        options.DefaultMetadata.Add("startupTime", DateTime.UtcNow);
                    });
                });

            using var host = hostBuilder.Build();
            
            Console.WriteLine("1. 启动服务主机 (自动注册服务)...");
            await host.StartAsync();
            
            var registrar = host.Services.GetServiceRegistrar();
            await Task.Delay(1000);

            Console.WriteLine("2. 查看注册的服务...");
            var services = registrar.GetRegisteredServices();
            foreach (var service in services)
            {
                Console.WriteLine($"   服务: {service.ServiceInfo.ServiceName}({service.ServiceInfo.ServiceId})");
                Console.WriteLine($"   地址: {service.ServiceInfo.Host}:{service.ServiceInfo.Port}");
                Console.WriteLine($"   状态: {service.HealthStatus}");
            }

            Console.WriteLine("\n3. 模拟服务运行期间的状态变化...");
            if (services.Count > 0)
            {
                var serviceId = services[0].ServiceInfo.ServiceId;
                
                // 模拟服务健康状态变化
                await Task.Delay(2000);
                Console.WriteLine("   设置为健康状态");
                await registrar.UpdateHealthStatusAsync(serviceId, HealthStatus.Healthy);

                await Task.Delay(3000);
                Console.WriteLine("   模拟服务异常");
                await registrar.UpdateHealthStatusAsync(serviceId, HealthStatus.Unhealthy);

                await Task.Delay(2000);
                Console.WriteLine("   服务恢复正常");
                await registrar.UpdateHealthStatusAsync(serviceId, HealthStatus.Healthy);
            }

            Console.WriteLine("\n4. 显示最终统计信息...");
            var finalStats = registrar.GetStatistics();
            Console.WriteLine($"   总服务数: {finalStats.TotalRegistered}");
            Console.WriteLine($"   健康服务: {finalStats.HealthyServices}");
            Console.WriteLine($"   运行时长: {DateTime.UtcNow - DateTime.UtcNow.AddSeconds(-15):mm\\:ss}");

            Console.WriteLine("\n5. 停止服务主机 (自动注销服务)...");
            await host.StopAsync();
            
            Console.WriteLine("服务生命周期演示完成！");
        }
    }
} 