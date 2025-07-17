using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Monitoring;
using PulseRPC.Monitoring.Extensions;
using PulseRPC.Monitoring.Metrics;
using PulseRPC.Monitoring.Performance;

namespace MonitoringExample
{
    /// <summary>
    /// 监控系统示例程序
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== PulseRPC 监控系统示例 ===\n");

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
                Console.WriteLine("1. 基础指标收集");
                Console.WriteLine("2. 性能监控");
                Console.WriteLine("3. RPC调用监控");
                Console.WriteLine("4. 负载均衡监控");
                Console.WriteLine("5. 服务发现监控");
                Console.WriteLine("6. 系统性能监控");
                Console.WriteLine("7. 实时监控仪表板");
                Console.WriteLine("8. 完整监控场景");
                Console.WriteLine("0. 退出");
                Console.Write("输入选择: ");

                var choice = Console.ReadLine();
                Console.WriteLine();

                try
                {
                    switch (choice)
                    {
                        case "1":
                            await BasicMetricsDemo();
                            break;
                        case "2":
                            await PerformanceMonitoringDemo();
                            break;
                        case "3":
                            await RpcMonitoringDemo();
                            break;
                        case "4":
                            await LoadBalancingMonitoringDemo();
                            break;
                        case "5":
                            await ServiceDiscoveryMonitoringDemo();
                            break;
                        case "6":
                            await SystemPerformanceDemo();
                            break;
                        case "7":
                            await RealtimeDashboardDemo();
                            break;
                        case "8":
                            await CompleteMonitoringDemo();
                            break;
                        case "0":
                            Console.WriteLine("感谢使用 PulseRPC 监控系统示例！");
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
        /// 1. 基础指标收集演示
        /// </summary>
        static async Task BasicMetricsDemo()
        {
            Console.WriteLine("=== 基础指标收集演示 ===");

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

            // 添加指标收集
            services.AddPulseRpcMetrics();

            var serviceProvider = services.BuildServiceProvider();
            var metricsCollector = serviceProvider.GetMetricsCollector();

            Console.WriteLine("演示各种指标类型...\n");

            // 计数器演示
            Console.WriteLine("1. 计数器演示");
            var counter = metricsCollector.GetCounter("demo_requests_total", "Total demo requests");
            for (int i = 0; i < 10; i++)
            {
                counter.Increment();
                Console.WriteLine($"   请求计数: {counter.Value}");
                await Task.Delay(200);
            }

            // 仪表演示
            Console.WriteLine("\n2. 仪表演示");
            var gauge = metricsCollector.GetGauge("demo_active_connections", "Active connections");
            var random = new Random();
            for (int i = 0; i < 10; i++)
            {
                var value = random.Next(1, 100);
                gauge.Set(value);
                Console.WriteLine($"   活跃连接数: {gauge.Value}");
                await Task.Delay(300);
            }

            // 直方图演示
            Console.WriteLine("\n3. 直方图演示");
            var histogram = metricsCollector.GetHistogram("demo_response_time", "Response time distribution");
            for (int i = 0; i < 20; i++)
            {
                var value = random.NextDouble() * 2.0; // 0-2秒
                histogram.Observe(value);
                Console.WriteLine($"   响应时间: {value:F3}s, 总请求数: {histogram.Count}, 平均时间: {(histogram.Sum / histogram.Count):F3}s");
                await Task.Delay(150);
            }

            // 计时器演示
            Console.WriteLine("\n4. 计时器演示");
            var timer = metricsCollector.GetTimer("demo_operation_duration", "Operation duration");
            for (int i = 0; i < 5; i++)
            {
                using (var context = timer.StartTimer())
                {
                    // 模拟操作
                    await Task.Delay(random.Next(100, 500));
                    var elapsed = context.Stop();
                    Console.WriteLine($"   操作 {i + 1} 耗时: {elapsed.TotalMilliseconds:F0}ms");
                }
            }

            // 获取指标快照
            Console.WriteLine("\n5. 指标快照");
            var snapshot = metricsCollector.GetSnapshot();
            Console.WriteLine($"   计数器数量: {snapshot.Counters.Count}");
            Console.WriteLine($"   仪表数量: {snapshot.Gauges.Count}");
            Console.WriteLine($"   直方图数量: {snapshot.Histograms.Count}");
            Console.WriteLine($"   计时器数量: {snapshot.Timers.Count}");

            await serviceProvider.DisposeAsync();
        }

        /// <summary>
        /// 2. 性能监控演示
        /// </summary>
        static async Task PerformanceMonitoringDemo()
        {
            Console.WriteLine("=== 性能监控演示 ===");

            var hostBuilder = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // 添加性能监控，采样间隔设置为2秒
                    services.AddPulseRpcPerformanceMonitoring(options =>
                    {
                        options.SamplingInterval = TimeSpan.FromSeconds(2);
                        options.EnableDetailedMetrics = true;
                        options.CollectSystemMetrics = true;
                        options.CollectProcessMetrics = true;
                        options.CollectGcMetrics = true;
                        options.CollectThreadPoolMetrics = true;
                    });
                });

            using var host = hostBuilder.Build();
            
            Console.WriteLine("启动性能监控器...");
            await host.StartAsync();

            var performanceMonitor = host.Services.GetPerformanceMonitor();
            var metricsCollector = host.Services.GetMetricsCollector();

            Console.WriteLine("性能监控器已启动，开始收集指标...\n");

            // 模拟工作负载
            var tasks = new List<Task>();
            for (int i = 0; i < 3; i++)
            {
                int taskId = i;
                tasks.Add(Task.Run(async () =>
                {
                    for (int j = 0; j < 10; j++)
                    {
                        // 模拟CPU密集型操作
                        var sum = 0;
                        for (int k = 0; k < 100000; k++)
                        {
                            sum += k;
                        }

                        // 模拟内存分配
                        var data = new byte[1024 * 1024]; // 1MB
                        Array.Fill(data, (byte)(taskId + j));

                        await Task.Delay(500);
                    }
                }));
            }

            // 监控15秒
            for (int i = 0; i < 8; i++)
            {
                await Task.Delay(2000);
                
                if (performanceMonitor != null)
                {
                    var summary = await performanceMonitor.GetPerformanceSummaryAsync();
                    Console.WriteLine($"时间: {DateTime.Now:HH:mm:ss}");
                    Console.WriteLine($"  CPU使用率: {summary.CpuUsage:F1}%");
                    Console.WriteLine($"  内存使用: {summary.MemoryUsage / (1024 * 1024):F1} MB");
                    Console.WriteLine($"  线程数: {summary.ThreadCount}");
                    Console.WriteLine($"  句柄数: {summary.HandleCount}");
                    Console.WriteLine($"  GC内存: {summary.TotalMemoryBytes / (1024 * 1024):F1} MB");
                    Console.WriteLine($"  运行时间: {summary.Uptime}");
                    Console.WriteLine();
                }
            }

            await Task.WhenAll(tasks);
            await host.StopAsync();
            Console.WriteLine("性能监控演示完成");
        }

        /// <summary>
        /// 3. RPC调用监控演示
        /// </summary>
        static async Task RpcMonitoringDemo()
        {
            Console.WriteLine("=== RPC调用监控演示 ===");

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
            services.AddPulseRpcMetrics();

            var serviceProvider = services.BuildServiceProvider();
            var metricsCollector = serviceProvider.GetMetricsCollector();

            Console.WriteLine("模拟RPC调用...\n");

            var services_names = new[] { "UserService", "OrderService", "PaymentService", "InventoryService" };
            var methods = new[] { "GetUser", "CreateOrder", "ProcessPayment", "UpdateInventory", "GetData", "SaveData" };
            var random = new Random();

            // 模拟100次RPC调用
            for (int i = 0; i < 100; i++)
            {
                var serviceName = services_names[random.Next(services_names.Length)];
                var methodName = methods[random.Next(methods.Length)];
                var success = random.NextDouble() > 0.1; // 90% 成功率
                var duration = TimeSpan.FromMilliseconds(random.Next(10, 1000));
                var requestSize = random.Next(100, 10000);
                var responseSize = random.Next(200, 50000);

                // 记录RPC调用指标
                metricsCollector.RecordRpcCall(serviceName, methodName, success, duration, requestSize, responseSize);

                if (i % 10 == 0)
                {
                    Console.WriteLine($"完成 {i + 1} 次调用...");
                }

                await Task.Delay(50);
            }

            // 显示统计信息
            Console.WriteLine("\n=== RPC调用统计 ===");
            var snapshot = metricsCollector.GetSnapshot();

            // RPC调用总数
            var rpcCalls = snapshot.Counters.Where(c => c.Name == "rpc_calls_total").ToList();
            var totalCalls = rpcCalls.Sum(c => c.Value);
            Console.WriteLine($"总RPC调用次数: {totalCalls}");

            // 按服务分组统计
            var callsByService = rpcCalls.GroupBy(c => c.Tags.GetValueOrDefault("service", "unknown"))
                .ToDictionary(g => g.Key, g => g.Sum(c => c.Value));
            
            Console.WriteLine("\n按服务统计:");
            foreach (var service in callsByService)
            {
                Console.WriteLine($"  {service.Key}: {service.Value} 次调用");
            }

            // 错误统计
            var errorCalls = snapshot.Counters.Where(c => c.Name == "rpc_errors_total").ToList();
            var totalErrors = errorCalls.Sum(c => c.Value);
            var errorRate = totalCalls > 0 ? (totalErrors / totalCalls) * 100 : 0;
            Console.WriteLine($"\n总错误次数: {totalErrors}");
            Console.WriteLine($"错误率: {errorRate:F2}%");

            // 响应时间统计
            var responseTimeHistogram = snapshot.Histograms.FirstOrDefault(h => h.Name == "rpc_call_duration_seconds");
            if (responseTimeHistogram != null)
            {
                Console.WriteLine($"\n响应时间统计:");
                Console.WriteLine($"  平均响应时间: {responseTimeHistogram.Average * 1000:F1}ms");
                Console.WriteLine($"  95% 响应时间: {responseTimeHistogram.GetQuantile(0.95) * 1000:F1}ms");
                Console.WriteLine($"  99% 响应时间: {responseTimeHistogram.GetQuantile(0.99) * 1000:F1}ms");
            }

            await serviceProvider.DisposeAsync();
        }

        /// <summary>
        /// 4. 负载均衡监控演示
        /// </summary>
        static async Task LoadBalancingMonitoringDemo()
        {
            Console.WriteLine("=== 负载均衡监控演示 ===");

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
            services.AddPulseRpcMetrics();

            var serviceProvider = services.BuildServiceProvider();
            var metricsCollector = serviceProvider.GetMetricsCollector();

            Console.WriteLine("模拟负载均衡选择...\n");

            var strategies = new[] { "RoundRobin", "Random", "WeightedRoundRobin", "LeastConnections", "ConsistentHash" };
            var services_names = new[] { "ApiService", "DataService", "CacheService" };
            var random = new Random();

            // 模拟负载均衡选择
            for (int i = 0; i < 200; i++)
            {
                var strategy = strategies[random.Next(strategies.Length)];
                var serviceName = services_names[random.Next(services_names.Length)];
                var endpointCount = random.Next(2, 10);
                var selectionTime = TimeSpan.FromMicroseconds(random.Next(100, 5000));

                metricsCollector.RecordLoadBalancing(strategy, serviceName, endpointCount, selectionTime);

                if (i % 20 == 0)
                {
                    Console.WriteLine($"完成 {i + 1} 次负载均衡选择...");
                }

                await Task.Delay(25);
            }

            // 显示统计信息
            Console.WriteLine("\n=== 负载均衡统计 ===");
            var snapshot = metricsCollector.GetSnapshot();

            // 选择次数统计
            var selections = snapshot.Counters.Where(c => c.Name == "lb_selections_total").ToList();
            var totalSelections = selections.Sum(c => c.Value);
            Console.WriteLine($"总选择次数: {totalSelections}");

            // 按策略统计
            var selectionsByStrategy = selections.GroupBy(c => c.Tags.GetValueOrDefault("strategy", "unknown"))
                .ToDictionary(g => g.Key, g => g.Sum(c => c.Value));
            
            Console.WriteLine("\n按策略统计:");
            foreach (var strategy in selectionsByStrategy)
            {
                Console.WriteLine($"  {strategy.Key}: {strategy.Value} 次选择");
            }

            // 选择时间统计
            var selectionTimeHistogram = snapshot.Histograms.FirstOrDefault(h => h.Name == "lb_selection_duration_seconds");
            if (selectionTimeHistogram != null)
            {
                Console.WriteLine($"\n选择时间统计:");
                Console.WriteLine($"  平均选择时间: {selectionTimeHistogram.Average * 1000000:F0}μs");
                Console.WriteLine($"  95% 选择时间: {selectionTimeHistogram.GetQuantile(0.95) * 1000000:F0}μs");
                Console.WriteLine($"  最大选择时间: {selectionTimeHistogram.GetQuantile(1.0) * 1000000:F0}μs");
            }

            // 端点数量统计
            var endpointGauges = snapshot.Gauges.Where(g => g.Name == "lb_available_endpoints").ToList();
            if (endpointGauges.Count > 0)
            {
                var avgEndpoints = endpointGauges.Average(g => g.Value);
                Console.WriteLine($"\n平均可用端点数: {avgEndpoints:F1}");
            }

            await serviceProvider.DisposeAsync();
        }

        /// <summary>
        /// 5. 服务发现监控演示
        /// </summary>
        static async Task ServiceDiscoveryMonitoringDemo()
        {
            Console.WriteLine("=== 服务发现监控演示 ===");

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
            services.AddPulseRpcMetrics();

            var serviceProvider = services.BuildServiceProvider();
            var metricsCollector = serviceProvider.GetMetricsCollector();

            Console.WriteLine("模拟服务发现操作...\n");

            var discoveryTypes = new[] { "Consul", "Etcd", "DNS", "Zookeeper" };
            var services_names = new[] { "UserService", "OrderService", "PaymentService", "NotificationService" };
            var random = new Random();

            // 模拟服务发现操作
            for (int i = 0; i < 150; i++)
            {
                var discoveryType = discoveryTypes[random.Next(discoveryTypes.Length)];
                var serviceName = services_names[random.Next(services_names.Length)];
                var endpointCount = random.Next(1, 8);
                var discoveryTime = TimeSpan.FromMilliseconds(random.Next(5, 200));
                var cacheHit = random.NextDouble() > 0.3; // 70% 缓存命中率

                metricsCollector.RecordServiceDiscovery(discoveryType, serviceName, endpointCount, discoveryTime, cacheHit);

                if (i % 15 == 0)
                {
                    Console.WriteLine($"完成 {i + 1} 次服务发现...");
                }

                await Task.Delay(30);
            }

            // 显示统计信息
            Console.WriteLine("\n=== 服务发现统计 ===");
            var snapshot = metricsCollector.GetSnapshot();

            // 发现次数统计
            var discoveries = snapshot.Counters.Where(c => c.Name == "sd_discoveries_total").ToList();
            var totalDiscoveries = discoveries.Sum(c => c.Value);
            Console.WriteLine($"总发现次数: {totalDiscoveries}");

            // 按类型统计
            var discoveriesByType = discoveries.GroupBy(c => c.Tags.GetValueOrDefault("type", "unknown"))
                .ToDictionary(g => g.Key, g => g.Sum(c => c.Value));
            
            Console.WriteLine("\n按发现类型统计:");
            foreach (var type in discoveriesByType)
            {
                Console.WriteLine($"  {type.Key}: {type.Value} 次发现");
            }

            // 缓存命中率
            var cacheHits = snapshot.Counters.Where(c => c.Name == "sd_cache_hits_total").Sum(c => c.Value);
            var cacheMisses = snapshot.Counters.Where(c => c.Name == "sd_cache_misses_total").Sum(c => c.Value);
            var totalCacheOperations = cacheHits + cacheMisses;
            var cacheHitRate = totalCacheOperations > 0 ? (cacheHits / totalCacheOperations) * 100 : 0;
            
            Console.WriteLine($"\n缓存统计:");
            Console.WriteLine($"  缓存命中: {cacheHits} 次");
            Console.WriteLine($"  缓存未命中: {cacheMisses} 次");
            Console.WriteLine($"  缓存命中率: {cacheHitRate:F1}%");

            // 发现时间统计
            var discoveryTimeHistogram = snapshot.Histograms.FirstOrDefault(h => h.Name == "sd_discovery_duration_seconds");
            if (discoveryTimeHistogram != null)
            {
                Console.WriteLine($"\n发现时间统计:");
                Console.WriteLine($"  平均发现时间: {discoveryTimeHistogram.Average * 1000:F1}ms");
                Console.WriteLine($"  95% 发现时间: {discoveryTimeHistogram.GetQuantile(0.95) * 1000:F1}ms");
                Console.WriteLine($"  最大发现时间: {discoveryTimeHistogram.GetQuantile(1.0) * 1000:F1}ms");
            }

            await serviceProvider.DisposeAsync();
        }

        /// <summary>
        /// 6. 系统性能监控演示
        /// </summary>
        static async Task SystemPerformanceDemo()
        {
            Console.WriteLine("=== 系统性能监控演示 ===");

            var hostBuilder = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddPulseRpcMonitoring(options =>
                    {
                        options.Performance.SamplingInterval = TimeSpan.FromSeconds(1);
                        options.Performance.EnableDetailedMetrics = true;
                        options.Performance.CollectSystemMetrics = true;
                        options.Performance.CollectProcessMetrics = true;
                        options.Performance.CollectGcMetrics = true;
                        options.Performance.CollectThreadPoolMetrics = true;
                    });
                });

            using var host = hostBuilder.Build();
            await host.StartAsync();

            var performanceMonitor = host.Services.GetPerformanceMonitor();
            Console.WriteLine("系统性能监控启动，监控10秒...\n");

            // 创建一些工作负载
            var workloadTask = Task.Run(async () =>
            {
                var data = new List<byte[]>();
                for (int i = 0; i < 50; i++)
                {
                    // 分配内存
                    data.Add(new byte[1024 * 1024]); // 1MB

                    // CPU工作
                    var sum = 0L;
                    for (int j = 0; j < 1000000; j++)
                    {
                        sum += j;
                    }

                    if (i % 10 == 0)
                    {
                        // 触发GC
                        GC.Collect();
                        await Task.Delay(100);
                    }

                    await Task.Delay(200);
                }
            });

            // 监控10秒
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(1000);
                
                if (performanceMonitor != null)
                {
                    var summary = await performanceMonitor.GetPerformanceSummaryAsync();
                    Console.WriteLine($"[{i + 1:D2}s] CPU: {summary.CpuUsage:F1}% | " +
                                    $"内存: {summary.MemoryUsage / (1024 * 1024):F0}MB | " +
                                    $"线程: {summary.ThreadCount} | " +
                                    $"GC内存: {summary.TotalMemoryBytes / (1024 * 1024):F0}MB");
                }
            }

            await workloadTask;
            await host.StopAsync();
            Console.WriteLine("\n系统性能监控演示完成");
        }

        /// <summary>
        /// 7. 实时监控仪表板演示
        /// </summary>
        static async Task RealtimeDashboardDemo()
        {
            Console.WriteLine("=== 实时监控仪表板演示 ===");

            var hostBuilder = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddPulseRpcMonitoring(options =>
                    {
                        options.Performance.SamplingInterval = TimeSpan.FromSeconds(1);
                        options.Metrics.CollectDetailedRpcMetrics = true;
                    });
                });

            using var host = hostBuilder.Build();
            await host.StartAsync();

            var metricsCollector = host.Services.GetMetricsCollector();
            var performanceMonitor = host.Services.GetPerformanceMonitor();

            Console.WriteLine("启动实时监控仪表板 (按任意键停止)...\n");

            // 后台模拟RPC调用
            var simulationTask = Task.Run(async () =>
            {
                var random = new Random();
                var services_names = new[] { "UserService", "OrderService", "PaymentService" };
                var methods = new[] { "Get", "Create", "Update", "Delete" };

                while (!Console.KeyAvailable)
                {
                    var serviceName = services_names[random.Next(services_names.Length)];
                    var methodName = methods[random.Next(methods.Length)];
                    var success = random.NextDouble() > 0.05; // 95% 成功率
                    var duration = TimeSpan.FromMilliseconds(random.Next(10, 200));

                    metricsCollector.RecordRpcCall(serviceName, methodName, success, duration);
                    await Task.Delay(random.Next(100, 500));
                }
            });

            // 显示仪表板
            var startTime = DateTime.UtcNow;
            while (!Console.KeyAvailable)
            {
                Console.Clear();
                Console.WriteLine("=== PulseRPC 实时监控仪表板 ===");
                Console.WriteLine($"运行时间: {DateTime.UtcNow - startTime:hh\\:mm\\:ss}\n");

                // 系统指标
                if (performanceMonitor != null)
                {
                    var summary = await performanceMonitor.GetPerformanceSummaryAsync();
                    Console.WriteLine("📊 系统指标:");
                    Console.WriteLine($"   CPU使用率: {summary.CpuUsage:F1}%");
                    Console.WriteLine($"   内存使用: {summary.MemoryUsage / (1024 * 1024):F0} MB");
                    Console.WriteLine($"   线程数量: {summary.ThreadCount}");
                    Console.WriteLine($"   GC内存: {summary.TotalMemoryBytes / (1024 * 1024):F0} MB");
                }

                // RPC指标
                var snapshot = metricsCollector.GetSnapshot();
                var rpcCalls = snapshot.Counters.Where(c => c.Name == "rpc_calls_total").Sum(c => c.Value);
                var rpcErrors = snapshot.Counters.Where(c => c.Name == "rpc_errors_total").Sum(c => c.Value);
                var errorRate = rpcCalls > 0 ? (rpcErrors / rpcCalls) * 100 : 0;

                Console.WriteLine("\n🚀 RPC指标:");
                Console.WriteLine($"   总调用次数: {rpcCalls}");
                Console.WriteLine($"   错误次数: {rpcErrors}");
                Console.WriteLine($"   错误率: {errorRate:F2}%");

                // 响应时间
                var responseTimeHistogram = snapshot.Histograms.FirstOrDefault(h => h.Name == "rpc_call_duration_seconds");
                if (responseTimeHistogram != null && responseTimeHistogram.Count > 0)
                {
                    Console.WriteLine($"   平均响应时间: {responseTimeHistogram.Average * 1000:F1}ms");
                    Console.WriteLine($"   95% 响应时间: {responseTimeHistogram.GetQuantile(0.95) * 1000:F1}ms");
                }

                Console.WriteLine("\n按任意键停止监控...");
                await Task.Delay(1000);
            }

            Console.ReadKey(); // 消费按键
            await simulationTask;
            await host.StopAsync();
            Console.WriteLine("\n实时监控仪表板演示完成");
        }

        /// <summary>
        /// 8. 完整监控场景演示
        /// </summary>
        static async Task CompleteMonitoringDemo()
        {
            Console.WriteLine("=== 完整监控场景演示 ===");

            var hostBuilder = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddPulseRpcMonitoring(options =>
                    {
                        options.Performance.SamplingInterval = TimeSpan.FromSeconds(2);
                        options.Performance.EnableDetailedMetrics = true;
                        options.Performance.EnableAlerts = true;
                        options.Performance.CpuUsageAlertThreshold = 50.0;
                        options.Performance.MemoryUsageAlertThreshold = 100.0; // MB
                        
                        options.Metrics.CollectDetailedRpcMetrics = true;
                        options.Metrics.CollectLoadBalancingMetrics = true;
                        options.Metrics.CollectServiceDiscoveryMetrics = true;
                        options.Metrics.CollectHealthCheckMetrics = true;
                    });
                });

            using var host = hostBuilder.Build();
            await host.StartAsync();

            var metricsCollector = host.Services.GetMetricsCollector();
            var performanceMonitor = host.Services.GetPerformanceMonitor();

            Console.WriteLine("启动完整监控场景，运行30秒...\n");

            // 多个工作负载任务
            var tasks = new List<Task>();

            // RPC调用模拟
            tasks.Add(Task.Run(async () =>
            {
                var random = new Random();
                var services_names = new[] { "UserService", "OrderService", "PaymentService", "InventoryService" };
                var methods = new[] { "Get", "Create", "Update", "Delete", "List", "Search" };

                for (int i = 0; i < 200; i++)
                {
                    var serviceName = services_names[random.Next(services_names.Length)];
                    var methodName = methods[random.Next(methods.Length)];
                    var success = random.NextDouble() > 0.08; // 92% 成功率
                    var duration = TimeSpan.FromMilliseconds(random.Next(5, 300));
                    var requestSize = random.Next(100, 5000);
                    var responseSize = random.Next(200, 20000);

                    metricsCollector.RecordRpcCall(serviceName, methodName, success, duration, requestSize, responseSize);
                    await Task.Delay(random.Next(50, 200));
                }
            }));

            // 负载均衡模拟
            tasks.Add(Task.Run(async () =>
            {
                var random = new Random();
                var strategies = new[] { "RoundRobin", "Random", "WeightedRoundRobin", "LeastConnections" };
                var services_names = new[] { "ApiGateway", "UserService", "OrderService" };

                for (int i = 0; i < 150; i++)
                {
                    var strategy = strategies[random.Next(strategies.Length)];
                    var serviceName = services_names[random.Next(services_names.Length)];
                    var endpointCount = random.Next(2, 8);
                    var selectionTime = TimeSpan.FromMicroseconds(random.Next(50, 2000));

                    metricsCollector.RecordLoadBalancing(strategy, serviceName, endpointCount, selectionTime);
                    await Task.Delay(random.Next(100, 300));
                }
            }));

            // 服务发现模拟
            tasks.Add(Task.Run(async () =>
            {
                var random = new Random();
                var types = new[] { "Consul", "Etcd", "DNS" };
                var services_names = new[] { "UserService", "OrderService", "PaymentService" };

                for (int i = 0; i < 100; i++)
                {
                    var type = types[random.Next(types.Length)];
                    var serviceName = services_names[random.Next(services_names.Length)];
                    var endpointCount = random.Next(1, 6);
                    var discoveryTime = TimeSpan.FromMilliseconds(random.Next(10, 150));
                    var cacheHit = random.NextDouble() > 0.25; // 75% 缓存命中率

                    metricsCollector.RecordServiceDiscovery(type, serviceName, endpointCount, discoveryTime, cacheHit);
                    await Task.Delay(random.Next(200, 500));
                }
            }));

            // 健康检查模拟
            tasks.Add(Task.Run(async () =>
            {
                var random = new Random();
                var services_names = new[] { "UserService", "OrderService", "PaymentService", "InventoryService" };
                var statuses = new[] { "healthy", "unhealthy", "unknown" };
                var weights = new[] { 0.85, 0.10, 0.05 }; // 85% 健康, 10% 不健康, 5% 未知

                for (int i = 0; i < 80; i++)
                {
                    var serviceName = services_names[random.Next(services_names.Length)];
                    var endpointId = $"{serviceName}-{random.Next(1, 4)}";
                    
                    // 基于权重选择状态
                    var rand = random.NextDouble();
                    var status = rand < weights[0] ? statuses[0] : rand < weights[0] + weights[1] ? statuses[1] : statuses[2];
                    
                    var checkTime = TimeSpan.FromMilliseconds(random.Next(5, 100));

                    metricsCollector.RecordHealthCheck(serviceName, endpointId, status, checkTime);
                    await Task.Delay(random.Next(300, 800));
                }
            }));

            // CPU密集型工作负载
            tasks.Add(Task.Run(async () =>
            {
                var data = new List<byte[]>();
                for (int i = 0; i < 20; i++)
                {
                    // 分配内存
                    data.Add(new byte[2 * 1024 * 1024]); // 2MB

                    // CPU计算
                    var sum = 0L;
                    for (int j = 0; j < 2000000; j++)
                    {
                        sum += j * j;
                    }

                    if (i % 5 == 0)
                    {
                        GC.Collect();
                    }

                    await Task.Delay(1000);
                }
            }));

            // 每5秒显示一次摘要
            var summaryTask = Task.Run(async () =>
            {
                for (int i = 0; i < 6; i++)
                {
                    await Task.Delay(5000);
                    
                    Console.WriteLine($"\n=== 第 {(i + 1) * 5} 秒监控摘要 ===");
                    
                    if (performanceMonitor != null)
                    {
                        var summary = await performanceMonitor.GetPerformanceSummaryAsync();
                        Console.WriteLine($"系统: CPU {summary.CpuUsage:F1}% | 内存 {summary.MemoryUsage / (1024 * 1024):F0}MB | 线程 {summary.ThreadCount}");
                    }

                    var snapshot = metricsCollector.GetSnapshot();
                    var rpcCalls = snapshot.Counters.Where(c => c.Name == "rpc_calls_total").Sum(c => c.Value);
                    var rpcErrors = snapshot.Counters.Where(c => c.Name == "rpc_errors_total").Sum(c => c.Value);
                    var lbSelections = snapshot.Counters.Where(c => c.Name == "lb_selections_total").Sum(c => c.Value);
                    var sdDiscoveries = snapshot.Counters.Where(c => c.Name == "sd_discoveries_total").Sum(c => c.Value);
                    var hcChecks = snapshot.Counters.Where(c => c.Name == "hc_checks_total").Sum(c => c.Value);

                    Console.WriteLine($"指标: RPC {rpcCalls}({rpcErrors}错误) | 负载均衡 {lbSelections} | 服务发现 {sdDiscoveries} | 健康检查 {hcChecks}");
                }
            });

            // 等待所有任务完成
            await Task.WhenAll(tasks.Concat(new[] { summaryTask }));

            // 最终报告
            Console.WriteLine("\n=== 最终监控报告 ===");
            if (performanceMonitor != null)
            {
                var finalSummary = await performanceMonitor.GetPerformanceSummaryAsync();
                Console.WriteLine($"最终性能指标:");
                Console.WriteLine($"  CPU使用率: {finalSummary.CpuUsage:F1}%");
                Console.WriteLine($"  内存使用: {finalSummary.MemoryUsage / (1024 * 1024):F0} MB");
                Console.WriteLine($"  线程数: {finalSummary.ThreadCount}");
                Console.WriteLine($"  GC 0代: {finalSummary.GcGen0Collections} 次");
                Console.WriteLine($"  GC 1代: {finalSummary.GcGen1Collections} 次");
                Console.WriteLine($"  GC 2代: {finalSummary.GcGen2Collections} 次");
            }

            var finalSnapshot = metricsCollector.GetSnapshot();
            Console.WriteLine($"\n最终指标计数:");
            Console.WriteLine($"  计数器: {finalSnapshot.Counters.Count}");
            Console.WriteLine($"  仪表: {finalSnapshot.Gauges.Count}");
            Console.WriteLine($"  直方图: {finalSnapshot.Histograms.Count}");
            Console.WriteLine($"  计时器: {finalSnapshot.Timers.Count}");

            await host.StopAsync();
            Console.WriteLine("\n完整监控场景演示完成！");
        }
    }
} 