using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Messaging;

namespace PulseRPC;

#nullable disable

/// <summary>
/// 服务代理生成器实现
/// </summary>
public class ServiceProxyGenerator : IServiceProxyGenerator
{
    private readonly IMessageChannel _messageChannel;

    public ServiceProxyGenerator(IMessageChannel messageChannel)
    {
        _messageChannel = messageChannel;
    }

    public T CreateProxy<T>() where T : class, INetworkService
    {
        // 创建动态代理实现
        // 实际实现可使用DynamicProxy或表达式树
        // 此处简化为基本实现
        return DispatchProxy.Create<T, ServiceProxy>() as T;
    }

    // 内部类：服务代理实现
    private class ServiceProxy : DispatchProxy
    {
        private IMessageChannel _channel;
        private Type _serviceType;

        public void Initialize(IMessageChannel channel, Type serviceType)
        {
            _channel = channel;
            _serviceType = serviceType;
        }

        protected override object Invoke(MethodInfo method, object[] args)
        {
            // 检查是否为事件订阅方法
            if (method.Name.StartsWith("SubscribeTo") && method.ReturnType == typeof(ISubscriptionToken))
            {
                return HandleEventSubscription(method, args);
            }

            // 处理普通方法调用
            return HandleMethodInvocation(method, args);
        }

        private object HandleEventSubscription(MethodInfo method, object[] args)
        {
            // 获取事件名称
            string eventName = method.Name.Substring("SubscribeTo".Length);

            // 获取回调委托
            var handlerDelegate = args[0];

            // 获取事件数据类型
            Type eventDataType = handlerDelegate.GetType().GetGenericArguments()[0];

            // 创建通用订阅方法
            var subscribeMethod = typeof(IMessageChannel).GetMethod("SubscribeToEvent")
                .MakeGenericMethod(eventDataType);

            // 调用订阅
            return subscribeMethod.Invoke(_channel, new[] { eventName, handlerDelegate });
        }

        private object HandleMethodInvocation(MethodInfo method, object[] args)
        {
            // 获取服务名和方法名
            string serviceName = _serviceType.Name;
            string methodName = method.Name;

            // 检查返回类型
            bool isVoidTask = method.ReturnType == typeof(Task);
            bool isGenericTask = method.ReturnType.IsGenericType &&
                                 method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>);
            bool isValueTask = method.ReturnType.IsGenericType &&
                               method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>);

            if (!isVoidTask && !isGenericTask && !isValueTask)
            {
                throw new NotSupportedException("Only Task, Task<T> and ValueTask<T> return types are supported");
            }

            // 获取请求参数
            object requestParam = args.Length > 0 ? args[0] : null;

            // 获取取消令牌
            CancellationToken cancellationToken = CancellationToken.None;
            if (args.Length > 0 && args[args.Length - 1] is CancellationToken token)
            {
                cancellationToken = token;
                requestParam = args.Length > 1 ? args[0] : null;
            }

            // 处理不同返回类型
            if (isVoidTask)
            {
                return SendVoidRequestAsync(serviceName, methodName, requestParam, cancellationToken);
            }
            else if (isGenericTask)
            {
                Type responseType = method.ReturnType.GetGenericArguments()[0];
                return SendGenericRequestAsync(serviceName, methodName, requestParam, responseType, cancellationToken);
            }
            else // isValueTask
            {
                Type responseType = method.ReturnType.GetGenericArguments()[0];
                return SendValueTaskRequestAsync(serviceName, methodName, requestParam, responseType,
                    cancellationToken);
            }
        }

        private async Task SendVoidRequestAsync(
            string serviceName, string methodName, object request, CancellationToken cancellationToken)
        {
            // 使用动态调用发送请求
            var sendMethod = typeof(IMessageChannel).GetMethod("SendRequestAsync")
                .MakeGenericMethod(request.GetType(), typeof(EmptyResponse));

            await (Task)sendMethod.Invoke(_channel, new[] { serviceName, methodName, request, cancellationToken });
        }

        private async Task<object> SendGenericRequestAsync(
            string serviceName, string methodName, object request, Type responseType,
            CancellationToken cancellationToken)
        {
            // 使用动态调用发送请求
            var sendMethod = typeof(IMessageChannel).GetMethod("SendRequestAsync")
                .MakeGenericMethod(request.GetType(), responseType);

            dynamic task = sendMethod.Invoke(_channel, new[] { serviceName, methodName, request, cancellationToken });
            return await task;
        }

        private object SendValueTaskRequestAsync(
            string serviceName, string methodName, object request, Type responseType,
            CancellationToken cancellationToken)
        {
            // 使用动态调用发送请求
            var sendMethod = typeof(IMessageChannel).GetMethod("SendRequestAsync")
                .MakeGenericMethod(request.GetType(), responseType);

            dynamic task = sendMethod.Invoke(_channel, new[] { serviceName, methodName, request, cancellationToken });
            Task<object> resultTask = task;

            // 构造ValueTask<T>
            var valueTaskCtor = typeof(ValueTask<>).MakeGenericType(responseType)
                .GetConstructor(new[] { typeof(Task<>).MakeGenericType(responseType) });

            return valueTaskCtor.Invoke(new[] { resultTask });
        }
    }

    // 内部类：空响应
    private class EmptyResponse
    {
    }
}

