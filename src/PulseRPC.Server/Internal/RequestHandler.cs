using PulseRPC.Protocol;
using System.Reflection;

namespace PulseRPC.Internal;

/// <summary>
/// Base class for request handlers, providing common functionality.
/// </summary>
internal abstract class RequestHandlerBase
{
    protected object DeserializeParameters(byte[] parameterData, Type[] parameterTypes)
    {
        // 支持无参数方法
        if (parameterTypes.Length == 0)
        {
            return Array.Empty<object>();
        }
        // 支持单参数方法
        if (parameterTypes.Length == 1)
        {
            return MemoryPack.MemoryPackSerializer.Deserialize(parameterTypes[0], parameterData)!;
        }

        // 支持多参数方法
        try
        {
            // 使用元组类型序列化多个参数
            var tupleType = Type.GetType($"System.ValueTuple`{parameterTypes.Length}");
            if (tupleType == null)
            {
                throw new NotSupportedException($"无法创建 {parameterTypes.Length} 个参数的元组类型");
            }

            var genericTupleType = tupleType.MakeGenericType(parameterTypes);
            var tuple = MemoryPack.MemoryPackSerializer.Deserialize(genericTupleType, parameterData)!;

            // 从元组中提取各个参数值
            var parameters = new object[parameterTypes.Length];
            for (int i = 0; i < parameterTypes.Length; i++)
            {
                parameters[i] = tuple.GetType().GetField($"Item{i + 1}")!.GetValue(tuple)!;
            }

            return parameters;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"反序列化多参数失败: {ex.Message}", ex);
        }
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
                task = (Task)method.Invoke(instance, parameters != null ? [parameters] : null)!;
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
                methodResult = method.Invoke(instance, parameters != null ? [parameters] : null);
            }
        }
        return methodResult;
    }
}
