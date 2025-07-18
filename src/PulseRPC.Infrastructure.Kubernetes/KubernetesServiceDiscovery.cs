using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using PulseRPC.ServiceRegistration;
using PulseRPC.Infrastructure.Kubernetes;

namespace PulseRPC.Infrastructure.Kubernetes;

public class KubernetesServiceDiscovery : IServiceDiscovery, IServiceRegistry, IDisposable
{
    private readonly IKubernetes _kubernetesClient;
    private readonly ILogger<KubernetesServiceDiscovery> _logger;
    private readonly KubernetesOptions _options;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public KubernetesServiceDiscovery(
        IKubernetes kubernetesClient,
        ILogger<KubernetesServiceDiscovery> logger,
        IOptions<KubernetesOptions> options)
    {
        _kubernetesClient = kubernetesClient ?? throw new ArgumentNullException(nameof(kubernetesClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        _logger.LogInformation("Kubernetes service discovery initialized for namespace: {Namespace}",
            _options.Namespace);
    }

    public async Task<IReadOnlyList<ServiceEndpoint>> DiscoverServicesAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var labelSelector = $"{_options.ServiceLabel}={serviceName}";
            var services = await _kubernetesClient.CoreV1.ListNamespacedServiceAsync(
                _options.Namespace, labelSelector: labelSelector, cancellationToken: cancellationToken);

            var endpoints = new List<ServiceEndpoint>();

            foreach (var service in services.Items)
            {
                var endpoint = ConvertServiceToEndpoint(service);
                if (endpoint != null)
                {
                    endpoints.Add(endpoint);
                }
            }

            _logger.LogDebug("Discovered {Count} endpoints for service: {ServiceName}",
                endpoints.Count, serviceName);

            return endpoints.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover services for: {ServiceName}", serviceName);
            throw;
        }
    }

    public async Task RegisterAsync(ServiceRegistration registration, CancellationToken cancellationToken = default)
    {
        if (registration == null)
            throw new ArgumentNullException(nameof(registration));

        try
        {
            await _semaphore.WaitAsync(cancellationToken);

            var service = new V1Service
            {
                Metadata = new V1ObjectMeta
                {
                    Name = registration.Id.ToLowerInvariant(),
                    NamespaceProperty = _options.Namespace,
                    Labels = new Dictionary<string, string>
                    {
                        [_options.ServiceLabel] = registration.ServiceName,
                        [_options.InstanceLabel] = registration.Id
                    },
                    Annotations = new Dictionary<string, string>
                    {
                        ["pulse-service-discovery/registration"] = JsonSerializer.Serialize(registration),
                        ["pulse-service-discovery/last-heartbeat"] = DateTime.UtcNow.ToString("O")
                    }
                },
                Spec = new V1ServiceSpec
                {
                    Ports = new List<V1ServicePort>
                    {
                        new()
                        {
                            Port = registration.Endpoint.Port,
                            TargetPort = registration.Endpoint.Port
                        }
                    }
                }
            };

            await _kubernetesClient.CoreV1.CreateNamespacedServiceAsync(
                service, _options.Namespace, cancellationToken: cancellationToken);

            _logger.LogInformation("Registered service: {ServiceName} (ID: {ServiceId})",
                registration.ServiceName, registration.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register service: {ServiceName} (ID: {ServiceId})",
                registration.ServiceName, registration.Id);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task UnregisterAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        try
        {
            await _semaphore.WaitAsync(cancellationToken);

            await _kubernetesClient.CoreV1.DeleteNamespacedServiceAsync(
                serviceId.ToLowerInvariant(), _options.Namespace, cancellationToken: cancellationToken);

            _logger.LogInformation("Unregistered service: {ServiceId}", serviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister service: {ServiceId}", serviceId);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<ServiceRegistration?> GetServiceAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var service = await _kubernetesClient.CoreV1.ReadNamespacedServiceAsync(
                serviceId.ToLowerInvariant(), _options.Namespace, cancellationToken: cancellationToken);

            return ConvertServiceToRegistration(service);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get service: {ServiceId}", serviceId);
            return null;
        }
    }

    public async Task<IReadOnlyList<ServiceRegistration>> GetAllServicesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var services = await _kubernetesClient.CoreV1.ListNamespacedServiceAsync(
                _options.Namespace, cancellationToken: cancellationToken);

            var registrations = new List<ServiceRegistration>();

            foreach (var service in services.Items)
            {
                if (service.Metadata?.Labels?.ContainsKey(_options.ServiceLabel) == true)
                {
                    var registration = ConvertServiceToRegistration(service);
                    if (registration != null)
                    {
                        registrations.Add(registration);
                    }
                }
            }

            return registrations.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all services");
            throw;
        }
    }

    public async Task UpdateHeartbeatAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var service = await GetServiceAsync(serviceId, cancellationToken);
            if (service != null)
            {
                var updatedService = service with { LastHeartbeat = DateTime.UtcNow };
                await RegisterAsync(updatedService, cancellationToken);

                _logger.LogDebug("Updated heartbeat for service: {ServiceId}", serviceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update heartbeat for service: {ServiceId}", serviceId);
            throw;
        }
    }

    private ServiceEndpoint? ConvertServiceToEndpoint(V1Service service)
    {
        if (service.Spec?.Ports?.Any() != true) return null;

        var port = service.Spec.Ports.First();
        var metadata = new Dictionary<string, string>();

        if (service.Metadata?.Annotations != null)
        {
            foreach (var annotation in service.Metadata.Annotations)
            {
                metadata[annotation.Key] = annotation.Value;
            }
        }

        return new ServiceEndpoint
        {
            Id = service.Metadata?.Labels?[_options.InstanceLabel] ?? service.Metadata?.Name ?? "unknown",
            Host = service.Spec.ClusterIP ?? "unknown",
            Port = port.Port,
            Weight = 1,
            Metadata = new ServiceMetadata(metadata)
        };
    }

    private ServiceRegistration? ConvertServiceToRegistration(V1Service service)
    {
        var annotationKey = "pulse-service-discovery/registration";
        if (service.Metadata?.Annotations?.TryGetValue(annotationKey, out var registrationJson) == true)
        {
            try
            {
                return JsonSerializer.Deserialize<ServiceRegistration>(registrationJson);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize service registration from annotation");
            }
        }

        return null;
    }

    public void Dispose()
    {
        _semaphore?.Dispose();
        _kubernetesClient?.Dispose();
    }
}
