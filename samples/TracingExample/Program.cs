using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Tracing;
using PulseRPC.Tracing.Extensions;
using PulseRPC.Tracing.Implementations;

namespace TracingExample
{
    /// <summary>
    /// 链路追踪示例程序
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== PulseRPC 链路追踪系统示例 ===\n");

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
                Console.WriteLine("1. 基础链路追踪");
                Console.WriteLine("2. 分布式上下文传播");
                Console.WriteLine("3. RPC调用追踪");
                Console.WriteLine("4. HTTP请求追踪");
                Console.WriteLine("5. 异常追踪");
                Console.WriteLine("6. 嵌套跨度");
                Console.WriteLine("7. 采样和过滤");
                Console.WriteLine("8. 完整追踪场景");
                Console.WriteLine("0. 退出");
                Console.Write("输入选择: ");

                var choice = Console.ReadLine();
                Console.WriteLine();

                try
                {
                    switch (choice)
                    {
                        case "1":
                            await BasicTracingDemo();
                            break;
                        case "2":
                            await DistributedContextDemo();
                            break;
                        case "3":
                            await RpcTracingDemo();
                            break;
                        case "4":
                            await HttpTracingDemo();
                            break;
                        case "5":
                            await ExceptionTracingDemo();
                            break;
                        case "6":
                            await NestedSpansDemo();
                            break;
                        case "7":
                            await SamplingFilteringDemo();
                            break;
                        case "8":
                            await CompleteTracingDemo();
                            break;
                        case "0":
                            Console.WriteLine("感谢使用 PulseRPC 链路追踪示例！");
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
        /// 1. 基础链路追踪演示
        /// </summary>
        static async Task BasicTracingDemo()
        {
            Console.WriteLine("=== 基础链路追踪演示 ===");

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
            
            // 添加链路追踪
            services.AddPulseRpcTracing(options =>
            {
                options.Enabled = true;
                options.SamplingRate = 1.0; // 100% 采样
                options.ServiceName = "TracingDemo";
                options.Exporter.Type = TracingExporterType.Console;
            });

            var serviceProvider = services.BuildServiceProvider();
            var tracer = serviceProvider.GetTracer();

            Console.WriteLine("创建基础跨度...\n");

            // 创建根跨度
            using (var rootSpan = tracer.StartSpan("demo_operation"))
            {
                rootSpan.SetTag("operation.type", "demo");
                rootSpan.SetTag("user.id", "12345");
                rootSpan.Log("开始演示操作");

                Console.WriteLine($"创建根跨度: {rootSpan}");

                // 模拟操作
                await Task.Delay(100);
                rootSpan.Log("完成第一步");

                await Task.Delay(150);
                rootSpan.Log("完成第二步");

                await Task.Delay(80);
                rootSpan.SetStatus(SpanStatus.Ok, "操作成功完成");
                rootSpan.Log("操作完成");

                Console.WriteLine($"跨度标签: {string.Join(", ", rootSpan.GetTags().Select(t => $"{t.Key}={t.Value}"))}");
                Console.WriteLine($"跨度日志数量: {rootSpan.GetLogs().Count}");
            }

            // 显示跨度摘要
            if (rootSpan is Span span)
            {
                var summary = span.GetSummary();
                Console.WriteLine($"\n跨度摘要:");
                Console.WriteLine($"  TraceId: {summary.TraceId}");
                Console.WriteLine($"  SpanId: {summary.SpanId}");
                Console.WriteLine($"  持续时间: {summary.Duration?.TotalMilliseconds:F0}ms");
                Console.WriteLine($"  状态: {summary.Status}");
                Console.WriteLine($"  已完成: {summary.IsFinished}");
            }

            await serviceProvider.DisposeAsync();
        }

        /// <summary>
        /// 2. 分布式上下文传播演示
        /// </summary>
        static async Task DistributedContextDemo()
        {
            Console.WriteLine("=== 分布式上下文传播演示 ===");

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
            services.AddPulseRpcTracing(options =>
            {
                options.Enabled = true;
                options.SamplingRate = 1.0;
                options.ServiceName = "ServiceA";
            });

            var serviceProvider = services.BuildServiceProvider();
            var tracer = serviceProvider.GetTracer();

            Console.WriteLine("模拟跨服务上下文传播...\n");

            // 服务A创建跨度
            using (var serviceASpan = tracer.StartSpan("service_a_operation"))
            {
                serviceASpan.SetTag("service", "ServiceA");
                serviceASpan.SetTag("endpoint", "/api/users");
                
                Console.WriteLine($"服务A创建跨度: {serviceASpan.Context.TraceId[..8]}...");

                // 注入上下文到HTTP头
                var headers = new Dictionary<string, string>();
                tracer.Inject(serviceASpan, headers);
                
                Console.WriteLine("注入的HTTP头:");
                foreach (var header in headers)
                {
                    Console.WriteLine($"  {header.Key}: {header.Value}");
                }

                // 模拟网络调用延迟
                await Task.Delay(50);

                // 模拟服务B
                await SimulateServiceB(tracer, headers);

                serviceASpan.SetStatus(SpanStatus.Ok);
            }

            await serviceProvider.DisposeAsync();
        }

        /// <summary>
        /// 模拟服务B处理
        /// </summary>
        static async Task SimulateServiceB(ITracer tracer, Dictionary<string, string> headers)
        {
            Console.WriteLine("\n--- 服务B处理 ---");

            // 从HTTP头提取上下文
            var extractedContext = tracer.Extract(headers);
            
            if (extractedContext != null)
            {
                Console.WriteLine($"服务B提取到上下文: TraceId={extractedContext.TraceId[..8]}...");

                // 创建子跨度
                var parentSpan = tracer.StartSpan("extracted_operation", null, new Dictionary<string, object>
                {
                    ["extracted.trace_id"] = extractedContext.TraceId,
                    ["extracted.span_id"] = extractedContext.SpanId
                });

                using (var serviceBSpan = tracer.StartSpan("service_b_operation", parentSpan))
                {
                    serviceBSpan.SetTag("service", "ServiceB");
                    serviceBSpan.SetTag("endpoint", "/api/orders");
                    serviceBSpan.SetTag("parent.trace_id", extractedContext.TraceId);

                    Console.WriteLine($"服务B创建子跨度: TraceId={serviceBSpan.Context.TraceId[..8]}... ParentSpanId={serviceBSpan.Context.ParentSpanId?[..8]}...");

                    // 模拟处理
                    await Task.Delay(120);

                    serviceBSpan.Log("处理订单数据");
                    serviceBSpan.SetStatus(SpanStatus.Ok);
                }

                parentSpan.Dispose();
            }
            else
            {
                Console.WriteLine("服务B未能提取到上下文");
            }
        }

        /// <summary>
        /// 3. RPC调用追踪演示
        /// </summary>
        static async Task RpcTracingDemo()
        {
            Console.WriteLine("=== RPC调用追踪演示 ===");

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
            services.AddPulseRpcTracing(options =>
            {
                options.Enabled = true;
                options.SamplingRate = 1.0;
                options.ServiceName = "RpcClient";
                options.TraceRpcCalls = true;
                options.RecordRpcArguments = true;
                options.RecordRpcReturnValues = true;
            });

            var serviceProvider = services.BuildServiceProvider();
            var tracer = serviceProvider.GetTracer();

            Console.WriteLine("模拟RPC调用追踪...\n");

            var rpcServices = new[] { "UserService", "OrderService", "PaymentService" };
            var rpcMethods = new[] { "GetUser", "CreateOrder", "ProcessPayment", "UpdateProfile" };
            var random = new Random();

            for (int i = 0; i < 5; i++)
            {
                var serviceName = rpcServices[random.Next(rpcServices.Length)];
                var methodName = rpcMethods[random.Next(rpcMethods.Length)];

                using (var rpcSpan = tracer.StartRpcSpan(serviceName, methodName))
                {
                    rpcSpan.SetTag("rpc.request_id", Guid.NewGuid().ToString());
                    rpcSpan.SetTag("rpc.timeout", "5000ms");

                    if (random.NextDouble() > 0.8) // 20% 失败率
                    {
                        var error = new InvalidOperationException($"RPC调用 {serviceName}.{methodName} 失败");
                        rpcSpan.RecordException(error);
                        Console.WriteLine($"❌ RPC调用失败: {serviceName}.{methodName} - {error.Message}");
                    }
                    else
                    {
                        var duration = random.Next(10, 200);
                        await Task.Delay(duration);
                        
                        rpcSpan.SetTag("rpc.response_size", random.Next(100, 5000));
                        rpcSpan.SetStatus(SpanStatus.Ok);
                        Console.WriteLine($"✅ RPC调用成功: {serviceName}.{methodName} - {duration}ms");
                    }

                    var summary = ((Span)rpcSpan).GetSummary();
                    Console.WriteLine($"   TraceId: {summary.TraceId[..8]}... 状态: {summary.Status} 错误: {summary.HasError}");
                }

                await Task.Delay(100);
            }

            await serviceProvider.DisposeAsync();
        }

        /// <summary>
        /// 4. HTTP请求追踪演示
        /// </summary>
        static async Task HttpTracingDemo()
        {
            Console.WriteLine("=== HTTP请求追踪演示 ===");

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
            services.AddPulseRpcTracing(options =>
            {
                options.Enabled = true;
                options.SamplingRate = 1.0;
                options.ServiceName = "HttpClient";
                options.TraceHttpRequests = true;
            });

            var serviceProvider = services.BuildServiceProvider();
            var tracer = serviceProvider.GetTracer();

            Console.WriteLine("模拟HTTP请求追踪...\n");

            var httpMethods = new[] { "GET", "POST", "PUT", "DELETE" };
            var endpoints = new[] 
            { 
                "https://api.example.com/users", 
                "https://api.example.com/orders",
                "https://api.example.com/products",
                "https://api.example.com/auth"
            };
            var random = new Random();

            for (int i = 0; i < 4; i++)
            {
                var method = httpMethods[random.Next(httpMethods.Length)];
                var url = endpoints[random.Next(endpoints.Length)];

                using (var httpSpan = tracer.StartHttpSpan(method, url))
                {
                    httpSpan.SetTag("http.user_agent", "PulseRPC-Client/1.0");
                    httpSpan.SetTag("http.request_id", Guid.NewGuid().ToString()[..8]);

                    var statusCode = random.NextDouble() > 0.1 ? 200 : (random.NextDouble() > 0.5 ? 404 : 500);
                    var duration = random.Next(50, 500);

                    await Task.Delay(duration);

                    httpSpan.SetTag("http.status_code", statusCode);
                    httpSpan.SetTag("http.response_size", random.Next(500, 10000));

                    if (statusCode >= 400)
                    {
                        httpSpan.SetStatus(SpanStatus.Error, $"HTTP {statusCode}");
                        Console.WriteLine($"❌ HTTP {method} {url} - {statusCode} ({duration}ms)");
                    }
                    else
                    {
                        httpSpan.SetStatus(SpanStatus.Ok);
                        Console.WriteLine($"✅ HTTP {method} {url} - {statusCode} ({duration}ms)");
                    }

                    var summary = ((Span)httpSpan).GetSummary();
                    Console.WriteLine($"   TraceId: {summary.TraceId[..8]}... 持续时间: {summary.Duration?.TotalMilliseconds:F0}ms");
                }

                await Task.Delay(150);
            }

            await serviceProvider.DisposeAsync();
        }

        /// <summary>
        /// 5. 异常追踪演示
        /// </summary>
        static async Task ExceptionTracingDemo()
        {
            Console.WriteLine("=== 异常追踪演示 ===");

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
            services.AddPulseRpcTracing(options =>
            {
                options.Enabled = true;
                options.SamplingRate = 1.0;
                options.ServiceName = "ExceptionDemo";
                options.RecordExceptions = true;
            });

            var serviceProvider = services.BuildServiceProvider();
            var tracer = serviceProvider.GetTracer();

            Console.WriteLine("演示异常追踪...\n");

            // 演示各种异常类型
            var exceptionTypes = new[]
            {
                () => throw new ArgumentNullException("参数", "参数不能为空"),
                () => throw new InvalidOperationException("无效操作: 资源不可用"),
                () => throw new TimeoutException("操作超时"),
                () => throw new UnauthorizedAccessException("访问被拒绝")
            };

            for (int i = 0; i < exceptionTypes.Length; i++)
            {
                using (var span = tracer.StartSpan($"exception_demo_{i + 1}"))
                {
                    span.SetTag("demo.iteration", i + 1);
                    span.Log("开始可能引发异常的操作");

                    try
                    {
                        await Task.Delay(50);
                        exceptionTypes[i]();
                    }
                    catch (Exception ex)
                    {
                        span.RecordException(ex);
                        Console.WriteLine($"捕获异常 {i + 1}: {ex.GetType().Name} - {ex.Message}");

                        var logs = span.GetLogs();
                        Console.WriteLine($"  跨度日志数量: {logs.Count}");
                        Console.WriteLine($"  异常已记录到跨度");
                    }

                    var summary = ((Span)span).GetSummary();
                    Console.WriteLine($"  TraceId: {summary.TraceId[..8]}... 错误: {summary.HasError} 状态: {summary.Status}");
                }

                await Task.Delay(100);
            }

            await serviceProvider.DisposeAsync();
        }

        /// <summary>
        /// 6. 嵌套跨度演示
        /// </summary>
        static async Task NestedSpansDemo()
        {
            Console.WriteLine("=== 嵌套跨度演示 ===");

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
            services.AddPulseRpcTracing(options =>
            {
                options.Enabled = true;
                options.SamplingRate = 1.0;
                options.ServiceName = "NestedDemo";
            });

            var serviceProvider = services.BuildServiceProvider();
            var tracer = serviceProvider.GetTracer();

            Console.WriteLine("创建嵌套跨度层次结构...\n");

            using (var rootSpan = tracer.StartSpan("business_process"))
            {
                rootSpan.SetTag("process.type", "order_fulfillment");
                Console.WriteLine($"根跨度: {rootSpan.Context.TraceId[..8]}... - business_process");

                using (var scope1 = tracer.SetActiveSpan(rootSpan))
                {
                    // 第一层子跨度
                    using (var authSpan = tracer.StartSpan("user_authentication"))
                    {
                        authSpan.SetTag("auth.method", "jwt");
                        Console.WriteLine($"  子跨度1: {authSpan.Context.SpanId[..8]}... - user_authentication");
                        await Task.Delay(80);
                    }

                    // 第二层子跨度
                    using (var validationSpan = tracer.StartSpan("order_validation"))
                    {
                        validationSpan.SetTag("validation.rules", "inventory,pricing");
                        Console.WriteLine($"  子跨度2: {validationSpan.Context.SpanId[..8]}... - order_validation");

                        using (var scope2 = tracer.SetActiveSpan(validationSpan))
                        {
                            // 第三层子跨度
                            using (var dbSpan = tracer.StartDatabaseSpan("SELECT", "products"))
                            {
                                dbSpan.SetTag("db.query_id", "Q001");
                                Console.WriteLine($"    孙跨度: {dbSpan.Context.SpanId[..8]}... - db.SELECT");
                                await Task.Delay(120);
                            }

                            using (var inventorySpan = tracer.StartSpan("inventory_check"))
                            {
                                inventorySpan.SetTag("warehouse.id", "WH001");
                                Console.WriteLine($"    孙跨度: {inventorySpan.Context.SpanId[..8]}... - inventory_check");
                                await Task.Delay(100);
                            }
                        }

                        await Task.Delay(50);
                    }

                    // 并行子跨度
                    var parallelTasks = new[]
                    {
                        Task.Run(async () =>
                        {
                            using (var paymentSpan = tracer.StartSpan("payment_processing", rootSpan))
                            {
                                paymentSpan.SetTag("payment.method", "credit_card");
                                Console.WriteLine($"  并行跨度1: {paymentSpan.Context.SpanId[..8]}... - payment_processing");
                                await Task.Delay(200);
                            }
                        }),
                        Task.Run(async () =>
                        {
                            using (var shippingSpan = tracer.StartSpan("shipping_calculation", rootSpan))
                            {
                                shippingSpan.SetTag("shipping.method", "express");
                                Console.WriteLine($"  并行跨度2: {shippingSpan.Context.SpanId[..8]}... - shipping_calculation");
                                await Task.Delay(150);
                            }
                        })
                    };

                    await Task.WhenAll(parallelTasks);
                }

                rootSpan.SetStatus(SpanStatus.Ok, "业务流程完成");
                Console.WriteLine($"根跨度完成: 总耗时 {(DateTimeOffset.UtcNow - rootSpan.StartTime).TotalMilliseconds:F0}ms");
            }

            await serviceProvider.DisposeAsync();
        }

        /// <summary>
        /// 7. 采样和过滤演示
        /// </summary>
        static async Task SamplingFilteringDemo()
        {
            Console.WriteLine("=== 采样和过滤演示 ===");

            // 演示不同采样率
            var samplingRates = new[] { 0.0, 0.3, 0.7, 1.0 };

            foreach (var rate in samplingRates)
            {
                Console.WriteLine($"\n--- 采样率: {rate * 100}% ---");

                var services = new ServiceCollection();
                services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
                services.AddPulseRpcTracing(options =>
                {
                    options.Enabled = true;
                    options.SamplingRate = rate;
                    options.ServiceName = "SamplingDemo";
                });

                var serviceProvider = services.BuildServiceProvider();
                var tracer = serviceProvider.GetTracer();

                int totalSpans = 20;
                int sampledSpans = 0;

                for (int i = 0; i < totalSpans; i++)
                {
                    using (var span = tracer.StartSpan($"sampled_operation_{i}"))
                    {
                        if (span.Context.IsSampled)
                        {
                            sampledSpans++;
                        }
                        await Task.Delay(10);
                    }
                }

                var actualSamplingRate = (double)sampledSpans / totalSpans;
                Console.WriteLine($"实际采样率: {actualSamplingRate * 100:F1}% ({sampledSpans}/{totalSpans})");

                await serviceProvider.DisposeAsync();
            }

            // 演示操作过滤
            Console.WriteLine("\n--- 操作过滤演示 ---");
            var services2 = new ServiceCollection();
            services2.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
            services2.AddPulseRpcTracing(options =>
            {
                options.Enabled = true;
                options.SamplingRate = 1.0;
                options.ServiceName = "FilterDemo";
            })
            .IgnoreOperations("health_check", "metrics");

            var serviceProvider2 = services2.BuildServiceProvider();
            var tracer2 = serviceProvider2.GetTracer();

            var operations = new[] { "business_logic", "health_check", "user_request", "metrics", "payment" };

            foreach (var operation in operations)
            {
                using (var span = tracer2.StartSpan(operation))
                {
                    var isIgnored = operation == "health_check" || operation == "metrics";
                    Console.WriteLine($"操作: {operation} - 是否忽略: {isIgnored}");
                    await Task.Delay(50);
                }
            }

            await serviceProvider2.DisposeAsync();
        }

        /// <summary>
        /// 8. 完整追踪场景演示
        /// </summary>
        static async Task CompleteTracingDemo()
        {
            Console.WriteLine("=== 完整追踪场景演示 ===");

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
            services.AddPulseRpcTracing(options =>
            {
                options.Enabled = true;
                options.SamplingRate = 1.0;
                options.ServiceName = "E-Commerce-API";
                options.ServiceVersion = "2.1.0";
                options.Environment = "production";
                options.TraceRpcCalls = true;
                options.TraceHttpRequests = true;
                options.TraceDatabaseOperations = true;
                options.RecordExceptions = true;
                options.RecordRpcArguments = false;
                options.RecordRpcReturnValues = false;
            })
            .AddResourceTags(new Dictionary<string, string>
            {
                ["cluster"] = "us-west-2",
                ["pod"] = "api-server-1",
                ["version"] = "v2.1.0"
            })
            .AddDefaultSpanTags(new Dictionary<string, string>
            {
                ["team"] = "platform",
                ["component"] = "api-gateway"
            });

            var serviceProvider = services.BuildServiceProvider();
            var tracer = serviceProvider.GetTracer();

            Console.WriteLine("模拟完整的电商订单处理流程...\n");

            // 模拟用户下单流程
            using (var orderSpan = tracer.StartSpan("process_order_request"))
            {
                orderSpan.SetTag("user.id", "user_12345");
                orderSpan.SetTag("order.id", "order_67890");
                orderSpan.SetTag("order.value", 299.99);

                try
                {
                    Console.WriteLine($"开始处理订单: TraceId={orderSpan.Context.TraceId[..8]}...");

                    // 用户认证
                    await tracer.WithSpanAsync("authenticate_user", async () =>
                    {
                        Console.WriteLine("  执行用户认证...");
                        await Task.Delay(80);
                    }, orderSpan, new Dictionary<string, object> { ["auth.method"] = "oauth2" });

                    // 库存检查
                    var inventoryResult = await tracer.WithSpanAsync("check_inventory", async () =>
                    {
                        Console.WriteLine("  检查库存...");
                        
                        // 数据库查询
                        using (var dbSpan = tracer.StartDatabaseSpan("SELECT", "inventory", "ecommerce"))
                        {
                            dbSpan.SetTag("db.query", "SELECT * FROM inventory WHERE product_id = ?");
                            await Task.Delay(120);
                        }

                        await Task.Delay(50);
                        return new { available = true, quantity = 10 };
                    }, orderSpan);

                    if (!inventoryResult.available)
                    {
                        throw new InvalidOperationException("商品库存不足");
                    }

                    // 价格计算
                    var pricing = await tracer.WithSpanAsync("calculate_pricing", async () =>
                    {
                        Console.WriteLine("  计算价格...");
                        
                        // RPC调用定价服务
                        using (var pricingSpan = tracer.StartRpcSpan("PricingService", "CalculatePrice", orderSpan))
                        {
                            pricingSpan.SetTag("pricing.rules", "standard,member_discount");
                            await Task.Delay(100);
                        }

                        return new { total = 299.99, discount = 29.99 };
                    }, orderSpan);

                    // 支付处理
                    await tracer.WithSpanAsync("process_payment", async () =>
                    {
                        Console.WriteLine("  处理支付...");

                        // HTTP调用支付网关
                        using (var paymentSpan = tracer.StartHttpSpan("POST", "https://payment.gateway.com/charge"))
                        {
                            paymentSpan.SetTag("payment.amount", pricing.total);
                            paymentSpan.SetTag("payment.method", "credit_card");
                            
                            await Task.Delay(200);
                            
                            var success = new Random().NextDouble() > 0.1; // 90% 成功率
                            if (!success)
                            {
                                throw new Exception("支付网关返回错误");
                            }
                        }
                    }, orderSpan);

                    // 并行执行库存更新和订单创建
                    var parallelTasks = new[]
                    {
                        tracer.WithSpanAsync("update_inventory", async () =>
                        {
                            Console.WriteLine("  更新库存...");
                            using (var updateSpan = tracer.StartDatabaseSpan("UPDATE", "inventory", "ecommerce"))
                            {
                                updateSpan.SetTag("db.rows_affected", 1);
                                await Task.Delay(80);
                            }
                        }, orderSpan),

                        tracer.WithSpanAsync("create_order_record", async () =>
                        {
                            Console.WriteLine("  创建订单记录...");
                            using (var insertSpan = tracer.StartDatabaseSpan("INSERT", "orders", "ecommerce"))
                            {
                                insertSpan.SetTag("order.status", "confirmed");
                                await Task.Delay(60);
                            }
                        }, orderSpan),

                        tracer.WithSpanAsync("send_confirmation", async () =>
                        {
                            Console.WriteLine("  发送确认通知...");
                            using (var notifySpan = tracer.StartRpcSpan("NotificationService", "SendEmail", orderSpan))
                            {
                                notifySpan.SetTag("notification.type", "order_confirmation");
                                await Task.Delay(150);
                            }
                        }, orderSpan)
                    };

                    await Task.WhenAll(parallelTasks);

                    orderSpan.SetStatus(SpanStatus.Ok, "订单处理成功");
                    orderSpan.SetTag("order.status", "confirmed");
                    
                    Console.WriteLine("✅ 订单处理完成!");

                    // 显示完整的追踪信息
                    var summary = ((Span)orderSpan).GetSummary();
                    Console.WriteLine($"\n追踪摘要:");
                    Console.WriteLine($"  TraceId: {summary.TraceId}");
                    Console.WriteLine($"  SpanId: {summary.SpanId}");
                    Console.WriteLine($"  总耗时: {summary.Duration?.TotalMilliseconds:F0}ms");
                    Console.WriteLine($"  状态: {summary.Status}");
                    Console.WriteLine($"  标签数量: {summary.TagCount}");
                    Console.WriteLine($"  日志数量: {summary.LogCount}");
                    Console.WriteLine($"  有错误: {summary.HasError}");

                }
                catch (Exception ex)
                {
                    orderSpan.RecordException(ex);
                    Console.WriteLine($"❌ 订单处理失败: {ex.Message}");
                }
            }

            // 刷新追踪数据
            await tracer.FlushAsync();
            Console.WriteLine("\n追踪数据已刷新");

            await serviceProvider.DisposeAsync();
        }
    }
} 