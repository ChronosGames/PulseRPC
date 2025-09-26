using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Server.Abstractions;

/// <summary>
/// Interface for generated service proxies
/// </summary>
public interface IGeneratedServiceProxy
{
    /// <summary>
    /// Invoke a method on the service
    /// </summary>
    ValueTask<object?> InvokeAsync(string methodName, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
}

/// <summary>
/// Exception thrown when a service is not found
/// </summary>
public class ServiceNotFoundException : Exception
{
    public ServiceNotFoundException(string message) : base(message) { }
    public ServiceNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when a method is not found
/// </summary>
public class MethodNotFoundException : Exception
{
    public MethodNotFoundException(string message) : base(message) { }
    public MethodNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}