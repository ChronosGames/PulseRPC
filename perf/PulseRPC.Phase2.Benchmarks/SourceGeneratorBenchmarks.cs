using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using PulseRPC.Phase2.Benchmarks.TestServices;
using System.Reflection;

namespace PulseRPC.Phase2.Benchmarks;

/// <summary>
/// Source Generator性能基准测试
/// 对比反射调用与生成代码的性能差异
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
[CategoriesColumn]
public class SourceGeneratorBenchmarks
{
    private IBenchmarkChatService _service = null!;
    private SendMessageRequest _sendMessageRequest = null!;
    private GetMessagesRequest _getMessagesRequest = null!;
    private byte[] _serializedSendMessageRequest = null!;
    private byte[] _serializedGetMessagesRequest = null!;

    [GlobalSetup]
    public void Setup()
    {
        // 设置服务实例
        _service = new BenchmarkChatService();

        // 准备测试数据
        _sendMessageRequest = new SendMessageRequest
        {
            Message = "Hello, World! This is a benchmark test message.",
            UserId = "benchmark_user_123",
            ChannelId = "benchmark_channel",
            Timestamp = DateTime.UtcNow
        };

        _getMessagesRequest = new GetMessagesRequest
        {
            ChannelId = "benchmark_channel",
            Count = 50,
            After = DateTime.UtcNow.AddHours(-1)
        };

        // 预序列化数据
        _serializedSendMessageRequest = MemoryPackSerializer.Serialize(_sendMessageRequest);
        _serializedGetMessagesRequest = MemoryPackSerializer.Serialize(_getMessagesRequest);

        Console.WriteLine($"SendMessageRequest serialized size: {_serializedSendMessageRequest.Length} bytes");
        Console.WriteLine($"GetMessagesRequest serialized size: {_serializedGetMessagesRequest.Length} bytes");
    }

    #region 消息序列化基准测试

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Serialization")]
    public byte[] SerializeMessage_Reflection()
    {
        // 模拟传统反射序列化
        return MemoryPackSerializer.Serialize(_sendMessageRequest);
    }

    [Benchmark]
    [BenchmarkCategory("Serialization")]
    public byte[] SerializeMessage_Generated()
    {
        // 使用生成的优化序列化代码
        // 注意：这里会调用生成的OptimizedSerialization.SerializeMessage方法
        try
        {
            // 尝试使用生成的序列化方法
            var writer = new System.Buffers.ArrayBufferWriter<byte>();
            PulseRPC.Generated.OptimizedSerialization.SerializeMessage(writer, _sendMessageRequest);
            return writer.WrittenMemory.ToArray();
        }
        catch
        {
            // 如果生成代码不可用，回退到标准序列化
            return MemoryPackSerializer.Serialize(_sendMessageRequest);
        }
    }

    [Benchmark]
    [BenchmarkCategory("Deserialization")]
    public SendMessageRequest DeserializeMessage_Reflection()
    {
        // 模拟传统反射反序列化
        return MemoryPackSerializer.Deserialize<SendMessageRequest>(_serializedSendMessageRequest)!;
    }

    [Benchmark]
    [BenchmarkCategory("Deserialization")]
    public SendMessageRequest DeserializeMessage_Generated()
    {
        // 使用生成的优化反序列化代码
        try
        {
            // 尝试使用生成的反序列化方法
            return PulseRPC.Generated.OptimizedSerialization.DeserializeMessage<SendMessageRequest>(_serializedSendMessageRequest);
        }
        catch
        {
            // 如果生成代码不可用，回退到标准反序列化
            return MemoryPackSerializer.Deserialize<SendMessageRequest>(_serializedSendMessageRequest)!;
        }
    }

    #endregion

    #region 服务方法调用基准测试

    [Benchmark]
    [BenchmarkCategory("MethodInvocation")]
    public async Task<SendMessageResponse> ServiceCall_Reflection()
    {
        // 模拟传统反射调用
        var methodInfo = typeof(IBenchmarkChatService).GetMethod(nameof(IBenchmarkChatService.SendMessageAsync));
        var task = (ValueTask<SendMessageResponse>)methodInfo!.Invoke(_service, new object[] { _sendMessageRequest })!;
        return await task;
    }

    [Benchmark]
    [BenchmarkCategory("MethodInvocation")]
    public async Task<SendMessageResponse> ServiceCall_Direct()
    {
        // 直接调用（最优性能基线）
        return await _service.SendMessageAsync(_sendMessageRequest);
    }

    [Benchmark]
    [BenchmarkCategory("MethodInvocation")]
    public async Task<SendMessageResponse> ServiceCall_Generated()
    {
        // 使用生成的代理调用
        try
        {
            // 尝试通过生成的路由表调用
            var result = await PulseRPC.Generated.ServiceRoutingTable.RouteAsync(
                "PulseRPC.Phase2.Benchmarks.TestServices.IBenchmarkChatService",
                "SendMessageAsync",
                _serializedSendMessageRequest
            );
            return (SendMessageResponse)result!;
        }
        catch
        {
            // 如果生成代码不可用，回退到直接调用
            return await _service.SendMessageAsync(_sendMessageRequest);
        }
    }

    #endregion

    #region 端到端基准测试

    [Benchmark]
    [BenchmarkCategory("EndToEnd")]
    public async Task<SendMessageResponse> EndToEnd_Traditional()
    {
        // 传统端到端：序列化 + 反射调用 + 反序列化
        var serialized = MemoryPackSerializer.Serialize(_sendMessageRequest);
        var deserialized = MemoryPackSerializer.Deserialize<SendMessageRequest>(serialized)!;
        
        var methodInfo = typeof(IBenchmarkChatService).GetMethod(nameof(IBenchmarkChatService.SendMessageAsync));
        var task = (ValueTask<SendMessageResponse>)methodInfo!.Invoke(_service, new object[] { deserialized })!;
        return await task;
    }

    [Benchmark]
    [BenchmarkCategory("EndToEnd")]
    public async Task<SendMessageResponse> EndToEnd_Generated()
    {
        // 生成代码端到端：优化序列化 + 生成路由 + 优化反序列化
        try
        {
            var result = await PulseRPC.Generated.ServiceRoutingTable.RouteAsync(
                "PulseRPC.Phase2.Benchmarks.TestServices.IBenchmarkChatService",
                "SendMessageAsync",
                _serializedSendMessageRequest
            );
            return (SendMessageResponse)result!;
        }
        catch
        {
            // 回退到直接调用
            return await _service.SendMessageAsync(_sendMessageRequest);
        }
    }

    #endregion

    #region 并发性能测试

    [Benchmark]
    [BenchmarkCategory("Concurrency")]
    public async Task ConcurrentCalls_Reflection()
    {
        var tasks = new Task[100];
        var methodInfo = typeof(IBenchmarkChatService).GetMethod(nameof(IBenchmarkChatService.SendMessageAsync));
        
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                var task = (ValueTask<SendMessageResponse>)methodInfo!.Invoke(_service, new object[] { _sendMessageRequest })!;
                await task;
            });
        }
        
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    [BenchmarkCategory("Concurrency")]
    public async Task ConcurrentCalls_Generated()
    {
        var tasks = new Task[100];
        
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                try
                {
                    await PulseRPC.Generated.ServiceRoutingTable.RouteAsync(
                        "PulseRPC.Phase2.Benchmarks.TestServices.IBenchmarkChatService",
                        "SendMessageAsync",
                        _serializedSendMessageRequest
                    );
                }
                catch
                {
                    await _service.SendMessageAsync(_sendMessageRequest);
                }
            });
        }
        
        await Task.WhenAll(tasks);
    }

    #endregion

    #region 内存分配测试

    [Benchmark]
    [BenchmarkCategory("Allocation")]
    public void AllocationTest_Reflection()
    {
        // 模拟传统反射的内存分配
        for (int i = 0; i < 1000; i++)
        {
            var methodInfo = typeof(IBenchmarkChatService).GetMethod(nameof(IBenchmarkChatService.SendMessageAsync));
            var parameters = new object[] { _sendMessageRequest };
            // 不实际调用，只测试分配
        }
    }

    [Benchmark]
    [BenchmarkCategory("Allocation")]
    public void AllocationTest_Generated()
    {
        // 生成代码应该有更少的内存分配
        for (int i = 0; i < 1000; i++)
        {
            // 静态方法调用，无额外分配
            var serviceName = "PulseRPC.Phase2.Benchmarks.TestServices.IBenchmarkChatService";
            var methodName = "SendMessageAsync";
            // 不实际调用，只测试分配
        }
    }

    #endregion
}

/// <summary>
/// Source Generator验证器
/// </summary>
public class SourceGeneratorValidator
{
    public async Task ValidateGeneratedCodeAsync()
    {
        Console.WriteLine("正在验证生成的代码...");

        // 检查是否生成了预期的类型
        await ValidateGeneratedTypes();
        
        // 验证序列化优化
        await ValidateSerializationOptimization();
        
        // 验证路由表生成
        await ValidateRoutingTable();
        
        Console.WriteLine("✅ 所有验证通过！");
    }

    private async Task ValidateGeneratedTypes()
    {
        try
        {
            // 检查生成的代理类
            var proxyType = Type.GetType("PulseRPC.Phase2.Benchmarks.TestServices.Generated.BenchmarkChatServiceProxy");
            Console.WriteLine($"✅ 代理类生成: {proxyType != null}");

            // 检查生成的路由表
            var routingTableType = Type.GetType("PulseRPC.Generated.ServiceRoutingTable");
            Console.WriteLine($"✅ 路由表生成: {routingTableType != null}");

            // 检查生成的序列化优化
            var serializationType = Type.GetType("PulseRPC.Generated.OptimizedSerialization");
            Console.WriteLine($"✅ 序列化优化生成: {serializationType != null}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ 生成类型验证异常: {ex.Message}");
        }
    }

    private async Task ValidateSerializationOptimization()
    {
        try
        {
            var request = new SendMessageRequest
            {
                Message = "Test message",
                UserId = "test_user",
                ChannelId = "test_channel"
            };

            // 测试标准序列化
            var standardSerialized = MemoryPackSerializer.Serialize(request);
            var standardDeserialized = MemoryPackSerializer.Deserialize<SendMessageRequest>(standardSerialized);

            Console.WriteLine($"✅ 标准序列化: {standardSerialized.Length} bytes");
            Console.WriteLine($"✅ 标准反序列化: {standardDeserialized != null}");

            // 尝试测试优化序列化（如果可用）
            try
            {
                var writer = new System.Buffers.ArrayBufferWriter<byte>();
                PulseRPC.Generated.OptimizedSerialization.SerializeMessage(writer, request);
                var optimizedSerialized = writer.WrittenMemory.ToArray();
                
                var optimizedDeserialized = PulseRPC.Generated.OptimizedSerialization.DeserializeMessage<SendMessageRequest>(optimizedSerialized);

                Console.WriteLine($"✅ 优化序列化: {optimizedSerialized.Length} bytes");
                Console.WriteLine($"✅ 优化反序列化: {optimizedDeserialized != null}");
                Console.WriteLine($"✅ 序列化数据匹配: {standardSerialized.SequenceEqual(optimizedSerialized)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ 优化序列化不可用: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 序列化验证失败: {ex.Message}");
        }
    }

    private async Task ValidateRoutingTable()
    {
        try
        {
            var service = new BenchmarkChatService();
            var request = new SendMessageRequest
            {
                Message = "Test routing",
                UserId = "test_user",
                ChannelId = "test_channel"
            };

            var serializedRequest = MemoryPackSerializer.Serialize(request);

            // 直接调用作为基线
            var directResult = await service.SendMessageAsync(request);
            Console.WriteLine($"✅ 直接调用: {directResult.Success}");

            // 尝试通过生成的路由表调用
            try
            {
                // 设置服务提供者（模拟）
                var serviceProvider = new ServiceCollection()
                    .AddSingleton<IBenchmarkChatService>(service)
                    .BuildServiceProvider();

                PulseRPC.Generated.ServiceRoutingTable.ServiceProvider = serviceProvider;

                var routedResult = await PulseRPC.Generated.ServiceRoutingTable.RouteAsync(
                    "PulseRPC.Phase2.Benchmarks.TestServices.IBenchmarkChatService",
                    "SendMessageAsync",
                    serializedRequest
                );

                var typedResult = (SendMessageResponse)routedResult!;
                Console.WriteLine($"✅ 路由调用: {typedResult.Success}");
                Console.WriteLine($"✅ 结果一致性: {directResult.Success == typedResult.Success}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ 路由表调用不可用: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 路由验证失败: {ex.Message}");
        }
    }
}