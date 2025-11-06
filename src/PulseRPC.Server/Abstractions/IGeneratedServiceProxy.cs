using System;

namespace PulseRPC.Server.Abstractions;

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

/// <summary>
/// Exception thrown when a protocol ID is not found
/// </summary>
public class ProtocolIdNotFoundException : Exception
{
    public ProtocolIdNotFoundException(string message) : base(message) { }
    public ProtocolIdNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}