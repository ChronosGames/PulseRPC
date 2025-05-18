using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Network;
using System.Runtime.CompilerServices;
using MemoryPack;

namespace PulseRPC.Client
{
    /// <summary>
    /// PulseRPC客户端工厂类，用于动态创建服务客户端和处理接收器
    /// </summary>
    public static class PulseRPCFactory
    {
        private static readonly ConcurrentDictionary<Type, Type> _proxyTypes = new();

        /// <summary>
        /// 创建服务客户端
        /// </summary>
        /// <typeparam name="THub">流Hub接口类型</typeparam>
        /// <param name="client">网络客户端</param>
        /// <returns>服务客户端实例</returns>
        public static THub CreateClient<THub>(NetworkClient client) where THub : class
        {
            if (client == null) throw new ArgumentNullException(nameof(client));

            var hubType = typeof(THub);
            if (!hubType.IsInterface)
            {
                throw new ArgumentException($"THub必须是接口类型: {hubType.FullName}");
            }

            // 创建动态代理
            return CreateClientProxy<THub>(client);
        }

        /// <summary>
        /// 注册接收器处理器
        /// </summary>
        /// <typeparam name="TReceiver">接收器接口类型</typeparam>
        /// <param name="client">网络客户端</param>
        /// <param name="receiver">接收器实例</param>
        /// <returns>是否成功注册</returns>
        public static bool RegisterReceiver<TReceiver>(NetworkClient client, TReceiver receiver)
            where TReceiver : class
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (receiver == null) throw new ArgumentNullException(nameof(receiver));

            var receiverType = typeof(TReceiver);
            if (!receiverType.IsInterface)
            {
                throw new ArgumentException($"TReceiver必须是接口类型: {receiverType.FullName}");
            }

            // 注册接收器方法
            return RegisterReceiverHandler(client, receiver);
        }

        private static THub CreateClientProxy<THub>(NetworkClient client) where THub : class
        {
            var hubType = typeof(THub);

            // 创建代理实例
            var proxy = new HubClientProxy<THub>(client);

            // 查找所有接口方法，并将调用映射到代理
            foreach (var method in hubType.GetMethods())
            {
                if (method.IsSpecialName) continue; // 跳过属性的getter/setter

                // 特殊处理IStreamingHub接口的配置方法
                if (method.Name == "WithDeadline" || method.Name == "WithCancellationToken" || method.Name == "WithHost")
                {
                    continue; // 这些方法已在HubClientProxy中实现
                }

                // 注册方法处理
                proxy.RegisterMethodHandler(method);
            }

            // 返回动态创建的代理对象
            return (proxy as THub)!;
        }

        private static bool RegisterReceiverHandler(NetworkClient client, object receiver)
        {
            try
            {
                var receiverType = receiver.GetType();

                // 获取所有接口方法
                foreach (var method in receiverType.GetMethods())
                {
                    if (method.IsSpecialName) continue; // 跳过属性的getter/setter

                    // 获取方法ID
                    var methodId = GetMethodId(method);

                    // 注册方法处理委托
                    var methodHandler = CreateMethodHandler(client, receiver, method);
                    client.RegisterHandler($"{receiverType.FullName}.{method.Name}", methodHandler);
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"注册接收器处理器失败: {ex.Message}");
                return false;
            }
        }

        private static ushort GetMethodId(MethodInfo method)
        {
            // 尝试从特性获取方法ID
            var attr = method.GetCustomAttribute(typeof(MethodIdAttribute)) as MethodIdAttribute;
            if (attr != null)
            {
                return attr.Id;
            }

            // 如果没有特性，计算哈希码
            var fullName = $"{method.DeclaringType?.FullName}.{method.Name}";
            var hash = Math.Abs(fullName.GetHashCode()) % 65536;
            return (ushort)hash;
        }

        private static Delegate CreateMethodHandler(NetworkClient client, object receiver, MethodInfo method)
        {
            // 创建基本的Action委托，实际应该根据方法参数和返回类型创建
            return new Action<object>(async (data) =>
            {
                try
                {
                    // 注意：这是简化的示例，实际实现会更复杂
                    await Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"处理方法调用失败: {ex.Message}");
                }
            });
        }
    }

    /// <summary>
    /// Hub客户端代理基类，用于动态处理方法调用
    /// </summary>
    internal class HubClientProxy<THub> where THub : class
    {
        private readonly NetworkClient _client;
        private readonly ConcurrentDictionary<string, Func<object[], Task<object>>> _methodHandlers = new();
        private CancellationToken _cancellationToken = CancellationToken.None;
        private DateTime _deadline = DateTime.MaxValue;
        private string _host = string.Empty;

        public HubClientProxy(NetworkClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public void RegisterMethodHandler(MethodInfo method)
        {
            var methodKey = method.Name;

            _methodHandlers[methodKey] = async (parameters) =>
            {
                // 确定是否为异步方法
                bool isAsync = method.ReturnType.IsGenericType &&
                               method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>);

                try
                {
                    // 创建请求参数对象
                    object requestObj;
                    if (parameters.Length == 1)
                    {
                        // 单参数，直接使用
                        requestObj = parameters[0];
                    }
                    else
                    {
                        // 暂不处理多参数情况，应该创建包装类
                        throw new NotImplementedException("暂不支持多参数方法");
                    }

                    // 确定返回类型
                    Type returnType;
                    if (isAsync)
                    {
                        returnType = method.ReturnType.GetGenericArguments()[0];
                    }
                    else
                    {
                        returnType = method.ReturnType;
                    }

                    // 发送请求并等待响应
                    var responseObj = await _client.SendRequestAsync(requestObj, returnType, _cancellationToken);

                    return responseObj;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"调用方法{method.Name}失败: {ex.Message}");
                    throw;
                }
            };
        }

        public async Task<TResult> InvokeAsync<TResult>(string methodName, params object[] parameters)
        {
            if (!_methodHandlers.TryGetValue(methodName, out var handler))
            {
                throw new InvalidOperationException($"未找到方法处理器: {methodName}");
            }

            var result = await handler(parameters);
            return (TResult)result;
        }

        public THub WithDeadline(DateTime deadline)
        {
            var proxy = new HubClientProxy<THub>(_client)
            {
                _cancellationToken = this._cancellationToken,
                _deadline = deadline,
                _host = this._host
            };

            // 复制方法处理器
            foreach (var handler in this._methodHandlers)
            {
                proxy._methodHandlers[handler.Key] = handler.Value;
            }

            return (proxy as THub)!;
        }

        public THub WithCancellationToken(CancellationToken cancellationToken)
        {
            var proxy = new HubClientProxy<THub>(_client)
            {
                _cancellationToken = cancellationToken,
                _deadline = this._deadline,
                _host = this._host
            };

            // 复制方法处理器
            foreach (var handler in this._methodHandlers)
            {
                proxy._methodHandlers[handler.Key] = handler.Value;
            }

            return (proxy as THub)!;
        }

        public THub WithHost(string host)
        {
            var proxy = new HubClientProxy<THub>(_client)
            {
                _cancellationToken = this._cancellationToken,
                _deadline = this._deadline,
                _host = host
            };

            // 复制方法处理器
            foreach (var handler in this._methodHandlers)
            {
                proxy._methodHandlers[handler.Key] = handler.Value;
            }

            return (proxy as THub)!;
        }
    }

    /// <summary>
    /// 方法ID特性
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class MethodIdAttribute : Attribute
    {
        public ushort Id { get; }

        public MethodIdAttribute(ushort id)
        {
            Id = id;
        }
    }
}
