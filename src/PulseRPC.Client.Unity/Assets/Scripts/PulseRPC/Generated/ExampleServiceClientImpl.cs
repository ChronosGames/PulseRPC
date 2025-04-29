using System;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Protocol;
using PulseRPC.Client.Unity;
using UnityEngine;

namespace PulseRPC.Client.Unity.Generated
{
    // 注意：这是一个示例生成的代理类，实际代码会由代码生成器生成
    public class ExampleServiceClientImpl : IExampleService
    {
        private readonly IPulseConnection _connection;

        public ExampleServiceClientImpl(IPulseConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        // 示例RPC方法 - 实际实现会通过代码生成器生成
        public async Task<int> AddAsync(int x, int y, CancellationToken cancellationToken = default)
        {
            try
            {
                // 创建请求
                var request = new PulseRequest
                {
                    RequestId = Guid.NewGuid(),
                    ServiceName = "ExampleService",
                    MethodName = "Add",
                    Parameters = MemoryPackSerializer.Serialize(new object[] { x, y })
                };

                // 发送请求并等待响应
                var response = await _connection.SendRequestAsync(request, cancellationToken);

                // 反序列化响应
                if (response.ReturnValue != null && response.ReturnValue.Length > 0)
                {
                    return MemoryPackSerializer.Deserialize<int>(response.ReturnValue);
                }

                return default;
            }
            catch (Exception ex)
            {
                Debug.LogError($"调用AddAsync时发生错误: {ex.Message}");
                throw;
            }
        }

        // 另一个示例RPC方法
        public async Task<string> GreetAsync(string name, CancellationToken cancellationToken = default)
        {
            try
            {
                // 创建请求
                var request = new PulseRequest
                {
                    RequestId = Guid.NewGuid(),
                    ServiceName = "ExampleService",
                    MethodName = "Greet",
                    Parameters = MemoryPackSerializer.Serialize(new object[] { name })
                };

                // 发送请求并等待响应
                var response = await _connection.SendRequestAsync(request, cancellationToken);

                // 反序列化响应
                if (response.ReturnValue != null && response.ReturnValue.Length > 0)
                {
                    return MemoryPackSerializer.Deserialize<string>(response.ReturnValue);
                }

                return default;
            }
            catch (Exception ex)
            {
                Debug.LogError($"调用GreetAsync时发生错误: {ex.Message}");
                throw;
            }
        }
    }

    // 服务接口定义 - 实际上会是一个共享的接口
    public interface IExampleService : IPulseService<IExampleService>
    {
        Task<int> AddAsync(int x, int y, CancellationToken cancellationToken = default);
        Task<string> GreetAsync(string name, CancellationToken cancellationToken = default);
    }
}
