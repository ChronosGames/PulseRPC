using PulseRPC.Protocol;
using System.Collections.Concurrent;
using System.Reflection;

namespace PulseRPC.Internal;

/// <summary>
/// Interface for managing service registrations.
/// </summary>
internal interface IServiceRegistry
{
    void Register<TService>(TService implementation) where TService : class, IPulseService<TService>;
    ServiceHandler? GetHandler(string serviceName);
}

/// <summary>
/// Handles registration and retrieval of service implementations.
/// </summary>
internal class ServiceRegistry : IServiceRegistry
{
    private readonly ConcurrentDictionary<string, ServiceHandler> _serviceHandlers = new();

    public void Register<TService>(TService implementation) where TService : class, IPulseService<TService>
    {
        var serviceType = typeof(TService);
        var serviceName = serviceType.FullName ?? serviceType.Name;

        var methods = serviceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                             .Where(m => m.DeclaringType == serviceType)
                             .ToDictionary(m => m.Name, m => m);

        var handler = new ServiceHandler(implementation, methods);

        if (!_serviceHandlers.TryAdd(serviceName, handler))
        {
            throw new InvalidOperationException($"Service '{serviceName}' is already registered.");
        }
    }

    public ServiceHandler? GetHandler(string serviceName)
    {
        _serviceHandlers.TryGetValue(serviceName, out var handler);
        return handler;
    }
}

/// <summary>
/// Handles requests for a specific registered service.
/// </summary>
internal class ServiceHandler : RequestHandlerBase
{
    private readonly object _implementation;
    private readonly Dictionary<string, MethodInfo> _methods;

    public ServiceHandler(object implementation, Dictionary<string, MethodInfo> methods)
    {
        _implementation = implementation;
        _methods = methods;
    }

    public async Task<PulseResponse> HandleRequestAsync(PulseRequest request)
    {
        if (!_methods.TryGetValue(request.MethodName, out var methodInfo))
        {
            return new PulseResponse
            {
                RequestId = request.RequestId,
                IsSuccess = false,
                ErrorMessage = $"Method '{request.MethodName}' not found on service '{request.ServiceName}'."
            };
        }

        try
        {
            var parameterTypes = methodInfo.GetParameters().Select(p => p.ParameterType).ToArray();
            var parameters = DeserializeParameters(request.Parameters, parameterTypes);

            var result = await InvokeMethodAsync(methodInfo, _implementation, parameters);
            var serializedResult = SerializeResult(result);

            return new PulseResponse
            {
                RequestId = request.RequestId,
                IsSuccess = true,
                Result = serializedResult
            };
        }
        catch (Exception ex)
        {
            // Log the inner exception if it exists
            var innerExceptionMessage = ex.InnerException != null ? $" Inner Exception: {ex.InnerException.Message}" : string.Empty;
            return new PulseResponse
            {
                RequestId = request.RequestId,
                IsSuccess = false,
                ErrorMessage = $"Error executing method '{request.MethodName}': {ex.Message}{innerExceptionMessage}"
            };
        }
    }
}
