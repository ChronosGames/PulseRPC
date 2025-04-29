using PulseRPC.Protocol;
using System.Reflection;

namespace PulseRPC.Internal;

/// <summary>
/// Interface for handling requests for a specific service or hub.
/// </summary>
internal interface IRequestHandler
{
    Task<PulseResponse> HandleRequestAsync(PulseRequest request);
}

/// <summary>
/// Base class for request handlers, providing common functionality.
/// </summary>
internal abstract class RequestHandlerBase
{
    protected object DeserializeParameters(byte[] parameterData, Type[] parameterTypes)
    {
        // Currently supports single parameter or parameterless methods
        if (parameterTypes.Length == 0)
        {
            return Array.Empty<object>();
        }
        if (parameterTypes.Length == 1)
        {
            return MemoryPack.MemoryPackSerializer.Deserialize(parameterTypes[0], parameterData)!;
        }
        // TODO: Support multiple parameters (e.g., using tuples or arrays)
        throw new NotSupportedException("Methods with multiple parameters are not yet supported by the base handler.");
    }

    protected byte[]? SerializeResult(object? result)
    {
        if (result == null)
        {
            return null;
        }
        // We need the actual type of the result, not Task<T>
        var resultType = result.GetType();
        return MemoryPack.MemoryPackSerializer.Serialize(resultType, result);
    }

    protected async Task<object?> InvokeMethodAsync(MethodInfo method, object instance, object? parameters)
    {
        object? methodResult = null;
        if (method.ReturnType == typeof(Task))
        {
            if (parameters is object[] paramsArray)
            {
                 await (Task)method.Invoke(instance, paramsArray)!;
            }
            else
            {
                await (Task)method.Invoke(instance, parameters != null ? new[] { parameters } : null)!;
            }
        }
        else if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            Task task;
             if (parameters is object[] paramsArray)
            {
                 task = (Task)method.Invoke(instance, paramsArray)!;
            }
            else
            {
                task = (Task)method.Invoke(instance, parameters != null ? new[] { parameters } : null)!;
            }
            await task;
            methodResult = ((dynamic)task).Result; // Use dynamic to access Task<T>.Result
        }
        else // Synchronous methods (consider disallowing or handling differently)
        {
            if (parameters is object[] paramsArray)
            {
                 methodResult = method.Invoke(instance, paramsArray);
            }
            else
            {
                methodResult = method.Invoke(instance, parameters != null ? new[] { parameters } : null);
            }
        }
        return methodResult;
    }
}
