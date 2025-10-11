using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Models;
using PulseRPC.Server.Pipeline;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace PulseRPC.Server.Core;

/// <summary>
/// Thread-safe registry for RPC services with CompiledServiceInvoker integration.
/// Manages service registration, unregistration, and lookup.
/// </summary>
public sealed class ServiceRegistry
{
    private readonly ConcurrentDictionary<string, ServiceRegistration> _services = new();
    private readonly ServiceRegistryOptions _options;

    public ServiceRegistry(ServiceRegistryOptions? options = null)
    {
        _options = options ?? new ServiceRegistryOptions();
    }

    /// <summary>
    /// Gets the number of registered services.
    /// </summary>
    public int RegisteredServiceCount => _services.Count;

    /// <summary>
    /// Gets all registered service names.
    /// </summary>
    public IReadOnlyList<string> GetServiceNames() => _services.Keys.ToArray();

    /// <summary>
    /// Registers a service with its implementation instance.
    /// </summary>
    public void RegisterService<TService>(string serviceName, TService serviceInstance, ServiceOptions? options = null)
        where TService : class
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));
        }

        if (serviceInstance == null)
        {
            throw new ArgumentNullException(nameof(serviceInstance));
        }

        // Check if already registered
        if (_services.ContainsKey(serviceName))
        {
            throw new InvalidOperationException($"Service '{serviceName}' is already registered");
        }

        // Create service invoker with compiled methods
        var timeout = options?.DefaultTimeout ?? _options.DefaultTimeout;
        var invoker = new ServiceInvoker(serviceInstance, timeout);

        // Create registration
        var registration = new ServiceRegistration
        {
            ServiceName = serviceName,
            ServiceType = typeof(TService),
            Handler = invoker,
            State = ServiceState.Active,
            Options = options ?? new Models.ServiceOptions(),
            RegisteredAt = DateTime.UtcNow
        };

        if (!_services.TryAdd(serviceName, registration))
        {
            throw new InvalidOperationException($"Failed to register service '{serviceName}' (concurrent registration detected)");
        }
    }

    /// <summary>
    /// Unregisters a service by name.
    /// </summary>
    public bool UnregisterService(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return false;
        }

        if (_services.TryRemove(serviceName, out var registration))
        {
            registration.State = ServiceState.Unregistered;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets a service handler by name.
    /// </summary>
    public IServiceHandler? GetServiceHandler(string serviceName)
    {
        if (_services.TryGetValue(serviceName, out var registration))
        {
            return registration.State == ServiceState.Active ? registration.Handler : null;
        }

        return null;
    }

    /// <summary>
    /// Gets service registration details.
    /// </summary>
    public ServiceRegistration? GetServiceRegistration(string serviceName)
    {
        _services.TryGetValue(serviceName, out var registration);
        return registration;
    }

    /// <summary>
    /// Checks if a service is registered and active.
    /// </summary>
    public bool IsServiceRegistered(string serviceName)
    {
        return _services.TryGetValue(serviceName, out var registration) && registration.State == ServiceState.Active;
    }

    /// <summary>
    /// Pauses a service (stops accepting new requests).
    /// </summary>
    public bool PauseService(string serviceName)
    {
        if (_services.TryGetValue(serviceName, out var registration))
        {
            registration.State = ServiceState.Paused;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resumes a paused service.
    /// </summary>
    public bool ResumeService(string serviceName)
    {
        if (_services.TryGetValue(serviceName, out var registration))
        {
            if (registration.State == ServiceState.Paused)
            {
                registration.State = ServiceState.Active;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets all available methods for a service.
    /// </summary>
    public IReadOnlyList<string> GetServiceMethods(string serviceName)
    {
        if (_services.TryGetValue(serviceName, out var registration))
        {
            return registration.Handler?.GetMethodNames() ?? Array.Empty<string>();
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Clears all registered services.
    /// </summary>
    public void Clear()
    {
        foreach (var registration in _services.Values)
        {
            registration.State = ServiceState.Unregistered;
        }

        _services.Clear();
    }
}

/// <summary>
/// Configuration options for ServiceRegistry.
/// </summary>
public sealed class ServiceRegistryOptions
{
    /// <summary>
    /// Default timeout for service invocations (default: 30 seconds).
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum number of services that can be registered (default: 1000).
    /// </summary>
    public int MaxServices { get; set; } = 1000;
}

