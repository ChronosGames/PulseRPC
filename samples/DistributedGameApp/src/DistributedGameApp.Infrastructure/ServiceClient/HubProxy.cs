using System.Reflection;

namespace DistributedGameApp.Infrastructure.ServiceClient;

/// <summary>
/// Hub 动态代理 - 使用 DispatchProxy 自动拦截方法调用并转换为 RPC 调用
/// </summary>
/// <typeparam name="THub">Hub 接口类型</typeparam>
public class HubProxy<THub> : DispatchProxy where THub : class
{
    private IRemoteInvoker? _invoker;
    private string? _hubName;

    /// <summary>
    /// 创建 Hub 代理实例
    /// </summary>
    public static THub Create(IRemoteInvoker invoker)
    {
        if (invoker == null)
            throw new ArgumentNullException(nameof(invoker));

        // 使用 DispatchProxy 创建代理
        var proxy = Create<THub, HubProxy<THub>>() as HubProxy<THub>;
        if (proxy == null)
            throw new InvalidOperationException($"Failed to create proxy for {typeof(THub).Name}");

        proxy._invoker = invoker;
        proxy._hubName = typeof(THub).Name;

        return (proxy as THub)!;
    }

    /// <summary>
    /// 拦截方法调用
    /// </summary>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod == null)
            throw new ArgumentNullException(nameof(targetMethod));

        if (_invoker == null || _hubName == null)
            throw new InvalidOperationException("Proxy not initialized");

        // 获取方法名
        var methodName = targetMethod.Name;

        // 处理异步方法
        if (typeof(Task).IsAssignableFrom(targetMethod.ReturnType))
        {
            // 获取泛型参数（如果有）
            var returnType = targetMethod.ReturnType;

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                // Task<T> - 有返回值的异步方法
                var resultType = returnType.GetGenericArguments()[0];
                var requestType = args?.Length > 0 ? args[0]?.GetType() : typeof(object);

                // 调用泛型 InvokeAsync<TRequest, TResponse> 方法
                var invokeMethod = typeof(IRemoteInvoker)
                    .GetMethod(nameof(IRemoteInvoker.InvokeAsync))
                    ?.MakeGenericMethod(requestType ?? typeof(object), resultType);

                if (invokeMethod == null)
                    throw new InvalidOperationException("Failed to find InvokeAsync method");

                var request = args?.Length > 0 ? args[0] : null;
                return invokeMethod.Invoke(_invoker, new[] { _hubName, methodName, request, CancellationToken.None });
            }
            else
            {
                // Task (void) - 无返回值的异步方法
                var requestType = args?.Length > 0 ? args[0]?.GetType() : typeof(object);

                var invokeMethod = typeof(IRemoteInvoker)
                    .GetMethod(nameof(IRemoteInvoker.InvokeAsync))
                    ?.MakeGenericMethod(requestType ?? typeof(object), typeof(object));

                if (invokeMethod == null)
                    throw new InvalidOperationException("Failed to find InvokeAsync method");

                var request = args?.Length > 0 ? args[0] : null;
                var task = invokeMethod.Invoke(_invoker, new[] { _hubName, methodName, request, CancellationToken.None });

                // 转换为 Task (不带返回值)
                return ConvertToTask(task as Task);
            }
        }

        throw new NotSupportedException($"Method {methodName} must return Task or Task<T>");
    }

    /// <summary>
    /// 转换 Task<T> 为 Task
    /// </summary>
    private static async Task ConvertToTask(Task? task)
    {
        if (task != null)
        {
            await task.ConfigureAwait(false);
        }
    }
}

/// <summary>
/// 远程调用接口 - 由具体的连接实现
/// </summary>
public interface IRemoteInvoker
{
    /// <summary>
    /// 调用远程方法
    /// </summary>
    Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string hubName,
        string methodName,
        TRequest? request,
        CancellationToken cancellationToken = default);
}
