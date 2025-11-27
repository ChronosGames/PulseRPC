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
        // 使用全限定名称，与服务端源生成器生成的协议号保持一致
        proxy._hubName = typeof(THub).FullName ?? typeof(THub).Name;

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

        // 获取方法的所有参数类型（用于协议号计算）
        // 排除 CancellationToken 参数，与服务端保持一致
        var parameterTypes = targetMethod.GetParameters()
            .Where(p => p.ParameterType != typeof(CancellationToken))
            .Select(p => p.ParameterType)
            .ToArray();

        // 处理异步方法
        if (typeof(Task).IsAssignableFrom(targetMethod.ReturnType))
        {
            // 获取泛型参数（如果有）
            var returnType = targetMethod.ReturnType;

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                // Task<T> - 有返回值的异步方法
                var resultType = returnType.GetGenericArguments()[0];

                // 使用第一个参数类型作为泛型参数（保持向后兼容）
                var requestType = parameterTypes.Length > 0 ? parameterTypes[0] : typeof(object);

                // 调用泛型 InvokeAsync<TRequest, TResponse> 方法
                var invokeMethod = typeof(IRemoteInvoker)
                    .GetMethod(nameof(IRemoteInvoker.InvokeAsync))
                    ?.MakeGenericMethod(requestType, resultType);

                if (invokeMethod == null)
                    throw new InvalidOperationException("Failed to find InvokeAsync method");

                var request = args?.Length > 0 ? args[0] : null;
                // 传递所有参数类型用于协议号计算
                return invokeMethod.Invoke(_invoker, new object?[] { _hubName, methodName, request, parameterTypes, CancellationToken.None });
            }
            else
            {
                // Task (void) - 无返回值的异步方法
                var requestType = parameterTypes.Length > 0 ? parameterTypes[0] : typeof(object);

                var invokeMethod = typeof(IRemoteInvoker)
                    .GetMethod(nameof(IRemoteInvoker.InvokeAsync))
                    ?.MakeGenericMethod(requestType, typeof(object));

                if (invokeMethod == null)
                    throw new InvalidOperationException("Failed to find InvokeAsync method");

                var request = args?.Length > 0 ? args[0] : null;
                // 传递所有参数类型用于协议号计算
                var task = invokeMethod.Invoke(_invoker, new object?[] { _hubName, methodName, request, parameterTypes, CancellationToken.None });

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
    /// <param name="hubName">Hub 名称（接口全限定名）</param>
    /// <param name="methodName">方法名</param>
    /// <param name="request">请求参数（第一个参数）</param>
    /// <param name="allParameterTypes">所有参数类型（用于协议号计算）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string hubName,
        string methodName,
        TRequest? request,
        Type[]? allParameterTypes,
        CancellationToken cancellationToken = default);
}
