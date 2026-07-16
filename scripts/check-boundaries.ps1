[CmdletBinding()]
param(
    [string]$Root
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Join-Path $PSScriptRoot '..'
}

$rootPath = (Resolve-Path $Root).Path.TrimEnd('\', '/')
$failures = New-Object System.Collections.Generic.List[string]

function Get-RelativePath {
    param([string]$Path)

    $fullPath = (Resolve-Path $Path).Path
    if (-not $fullPath.StartsWith($rootPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath
    }

    return $fullPath.Substring($rootPath.Length + 1).Replace('\', '/')
}

function Add-Failure {
    param([string]$Message)
    $failures.Add($Message) | Out-Null
}

$serverProject = Join-Path $rootPath 'src/PulseRPC.Server/PulseRPC.Server.csproj'
if ((Get-Content -Path $serverProject -Raw) -match 'PulseRPC\.Client') {
    Add-Failure 'PulseRPC.Server must not reference PulseRPC.Client.'
}

$abstractionsNamespaceAllowlist = @(
    'src/PulseRPC.Abstractions/Channels/HubRegistrationToken.cs|PulseRPC.Channels',
    'src/PulseRPC.Abstractions/Channels/IClientChannel.cs|PulseRPC.Client',
    'src/PulseRPC.Abstractions/Channels/ITransportChannel.cs|PulseRPC.Channels',
    'src/PulseRPC.Abstractions/Channels/ITransportChannelPool.cs|PulseRPC.Channels',
    'src/PulseRPC.Abstractions/Channels/MessageHeader.cs|PulseRPC.Messaging',
    'src/PulseRPC.Abstractions/Channels/MessageTypes.cs|PulseRPC.Messaging',
    'src/PulseRPC.Abstractions/Channels/ResponseContext.cs|PulseRPC.Channels',
    'src/PulseRPC.Abstractions/Channels/TransportChannelBase.cs|PulseRPC.Channels',
    'src/PulseRPC.Abstractions/Channels/TransportChannelPool.cs|PulseRPC.Channels',
    'src/PulseRPC.Abstractions/Client/ConnectionDescriptor.cs|PulseRPC.Client',
    'src/PulseRPC.Abstractions/Messaging/EnvelopeRelay.cs|PulseRPC.Messaging',
    'src/PulseRPC.Abstractions/Messaging/MessagePacket.cs|PulseRPC.Messaging',
    'src/PulseRPC.Abstractions/Messaging/ReadOnlyEnvelopeHeader.cs|PulseRPC.Messaging',
    'src/PulseRPC.Abstractions/Transport/ITransport.cs|PulseRPC.Shared',
    'src/PulseRPC.Abstractions/Transport/MessageProcessorModels.cs|PulseRPC.Shared',
    'src/PulseRPC.Abstractions/Transport/ProcessingResult.cs|PulseRPC.Shared',
    'src/PulseRPC.Abstractions/Transport/ProtocolConstants.cs|PulseRPC.Shared',
    'src/PulseRPC.Abstractions/Transport/TransportContext.cs|PulseRPC.Shared',
    'src/PulseRPC.Abstractions/Transport/TransportOptions.cs|PulseRPC.Shared'
)

$sharedNamespaceAllowlist = @(
    'src/PulseRPC.Shared/Batching/BatchedTransport.cs|PulseRPC.Abstractions.Transport.Batching'
)

$abstractionsPublicTypeAllowlist = @(
    'src/PulseRPC.Abstractions/Attributes.cs|AllowAnonymousAttribute',
    'src/PulseRPC.Abstractions/Attributes.cs|AuthorizeAttribute',
    'src/PulseRPC.Abstractions/Attributes.cs|ChannelAttribute',
    'src/PulseRPC.Abstractions/Attributes.cs|ClientFacingAttribute',
    'src/PulseRPC.Abstractions/Attributes.cs|GenerateEventHandlerAttribute',
    'src/PulseRPC.Abstractions/Attributes.cs|PriorityAttribute',
    'src/PulseRPC.Abstractions/Attributes.cs|PulseClientGenerationAttribute',
    'src/PulseRPC.Abstractions/Attributes.cs|ReentrantAttribute',
    'src/PulseRPC.Abstractions/Attributes.cs|RoleTypes',
    'src/PulseRPC.Abstractions/Authentication/IAuthenticationProvider.cs|AuthenticationConfiguration',
    'src/PulseRPC.Abstractions/Authentication/IAuthenticationProvider.cs|AuthenticationException',
    'src/PulseRPC.Abstractions/Authentication/IAuthenticationProvider.cs|AuthenticationMessage',
    'src/PulseRPC.Abstractions/Authentication/IAuthenticationProvider.cs|AuthenticationResult',
    'src/PulseRPC.Abstractions/Authentication/IAuthenticationProvider.cs|AuthenticationToken',
    'src/PulseRPC.Abstractions/Authentication/IAuthenticationValidator.cs|AuthenticationValidationResult',
    'src/PulseRPC.Abstractions/Channels/HubRegistrationToken.cs|HubRegistrationToken',
    'src/PulseRPC.Abstractions/Channels/MessageHeader.cs|ConnectionStateChangedEventArgs',
    'src/PulseRPC.Abstractions/Channels/MessageHeader.cs|ConnectionStateConverter',
    'src/PulseRPC.Abstractions/Channels/MessageHeader.cs|MessageHeader',
    'src/PulseRPC.Abstractions/Channels/MessageHeader.cs|NetworkMessage',
    'src/PulseRPC.Abstractions/Channels/MessageTypes.cs|ErrorResponse',
    'src/PulseRPC.Abstractions/Channels/ResponseContext.cs|ResponseContext',
    'src/PulseRPC.Abstractions/Channels/TransportChannelBase.cs|ServiceNotFoundException',
    'src/PulseRPC.Abstractions/Channels/TransportChannelBase.cs|TransportChannelBase',
    'src/PulseRPC.Abstractions/Channels/TransportChannelPool.cs|TransportChannelPool',
    'src/PulseRPC.Abstractions/Client/ConnectionDescriptor.cs|ConnectionDescriptor',
    'src/PulseRPC.Abstractions/Client/ConnectionDescriptor.cs|ConnectionStatistics',
    'src/PulseRPC.Abstractions/Client/ConnectionDescriptor.cs|ConnectionValidationResult',
    'src/PulseRPC.Abstractions/Client/ConnectionDescriptor.cs|EndpointAddress',
    'src/PulseRPC.Abstractions/Clustering/IPulseBackplane.cs|InProcessBackplane',
    # Reviewed cross-package wire contract: custom IVersionedNodeTransport implementations need
    # the negotiated session and version-tolerant DTOs without referencing PulseRPC.Server.
    'src/PulseRPC.Abstractions/Clustering/NodeWireProtocol.cs|NodeTransportSession',
    'src/PulseRPC.Abstractions/Clustering/NodeWireProtocol.cs|NodeWireProtocol',
    'src/PulseRPC.Abstractions/Clustering/NodeWireProtocol.cs|NodeNegotiationRequest',
    'src/PulseRPC.Abstractions/Clustering/NodeWireProtocol.cs|NodeNegotiationResponse',
    'src/PulseRPC.Abstractions/Clustering/NodeWireProtocol.cs|NodeActorInvocationEnvelope',
    'src/PulseRPC.Abstractions/Clustering/NodeWireProtocol.cs|NodeCallerContextSnapshot',
    'src/PulseRPC.Abstractions/Clustering/NodeWireProtocol.cs|NodeClaimsIdentitySnapshot',
    'src/PulseRPC.Abstractions/Clustering/NodeWireProtocol.cs|NodeClaimSnapshot',
    'src/PulseRPC.Abstractions/Configuration/HealthCheckOptions.cs|HealthCheckOptions',
    'src/PulseRPC.Abstractions/EmptyResponse.cs|EmptyResponse',
    'src/PulseRPC.Abstractions/Events/SubscriptionToken.cs|CompositeSubscriptionToken',
    'src/PulseRPC.Abstractions/Events/SubscriptionToken.cs|SubscriptionToken',
    'src/PulseRPC.Abstractions/Exceptions/HandshakeException.cs|HandshakeException',
    'src/PulseRPC.Abstractions/Exceptions/PulseRemoteException.cs|PulseRemoteException',
    'src/PulseRPC.Abstractions/Exceptions/PulseReverseCallException.cs|PulseReverseCallException',
    'src/PulseRPC.Abstractions/Gateway/GatewayProtocolIds.cs|GatewayProtocolIds',
    'src/PulseRPC.Abstractions/Internal/CompatibilityHelpers.cs|CompatibilityHelpers',
    'src/PulseRPC.Abstractions/Memory/ZeroCopyCircularBuffer.cs|BufferStatistics',
    'src/PulseRPC.Abstractions/Memory/ZeroCopyCircularBuffer.cs|MemorySpecs',
    'src/PulseRPC.Abstractions/Memory/ZeroCopyCircularBuffer.cs|PerformanceSpecs',
    'src/PulseRPC.Abstractions/Memory/ZeroCopyCircularBuffer.cs|ZeroCopyCircularBuffer',
    'src/PulseRPC.Abstractions/Messaging/EnvelopeRelay.cs|EnvelopeRelay',
    'src/PulseRPC.Abstractions/Messaging/MessagePacket.cs|MessagePacketHolder',
    'src/PulseRPC.Abstractions/Protocol/PendingRequestManager.cs|PendingRequestManager',
    'src/PulseRPC.Abstractions/Protocol/ProtocolAttribute.cs|ProtocolAttribute',
    'src/PulseRPC.Abstractions/Protocol/ProtocolAttribute.cs|ProtocolAttributeExtensions',
    'src/PulseRPC.Abstractions/PulseRPCSerializerProvider.cs|PulseRPCSerializerProvider',
    'src/PulseRPC.Abstractions/RouterGenerationAttributes.cs|PulseRouterGenerationAttribute',
    'src/PulseRPC.Abstractions/Routing/IMultiInstanceServiceManager.cs|BroadcastResult',
    'src/PulseRPC.Abstractions/Routing/IMultiInstanceServiceManager.cs|BroadcastResultItem',
    'src/PulseRPC.Abstractions/Routing/IMultiInstanceServiceManager.cs|LoadBalancingEventArgs',
    'src/PulseRPC.Abstractions/Routing/IMultiInstanceServiceManager.cs|ParallelResult',
    'src/PulseRPC.Abstractions/Routing/IMultiInstanceServiceManager.cs|ServiceInstanceEventArgs',
    'src/PulseRPC.Abstractions/Routing/IRoutingContext.cs|RoutingContext',
    'src/PulseRPC.Abstractions/Routing/ServiceInstanceInfo.cs|ServiceInstanceInfo',
    'src/PulseRPC.Abstractions/Routing/ServiceRoutingStrategy.cs|CircuitBreakerConfiguration',
    'src/PulseRPC.Abstractions/Routing/ServiceRoutingStrategy.cs|FailoverConfiguration',
    'src/PulseRPC.Abstractions/Routing/ServiceRoutingStrategy.cs|ServiceRoutingConfiguration',
    'src/PulseRPC.Abstractions/Scheduling/SchedulerMetrics.cs|SchedulerMetrics',
    'src/PulseRPC.Abstractions/Transport/Batching/BatchedTransportOptions.cs|BatchedTransportOptions',
    'src/PulseRPC.Abstractions/Transport/Batching/IBatchedTransport.cs|TransportMetricsSnapshot',
    'src/PulseRPC.Abstractions/Transport/Batching/TransportBackpressureController.cs|TransportBackpressureController',
    'src/PulseRPC.Abstractions/Transport/Batching/TransportBackpressurePolicy.cs|BackpressureRejectedException',
    'src/PulseRPC.Abstractions/Transport/Batching/TransportMetrics.cs|TransportMetrics',
    'src/PulseRPC.Abstractions/Transport/ITransport.cs|ServerConnectionEventArgs',
    'src/PulseRPC.Abstractions/Transport/ITransport.cs|TransportDataEventArgs',
    'src/PulseRPC.Abstractions/Transport/ITransport.cs|TransportStateEventArgs',
    'src/PulseRPC.Abstractions/Transport/MessageProcessorModels.cs|ClientMessage',
    'src/PulseRPC.Abstractions/Transport/ProcessingResult.cs|ProcessingResult',
    'src/PulseRPC.Abstractions/Transport/ProtocolConstants.cs|ProtocolConstants',
    'src/PulseRPC.Abstractions/Transport/TransportContext.cs|TransportContext',
    'src/PulseRPC.Abstractions/Transport/TransportContext.cs|TransportStatistics',
    'src/PulseRPC.Abstractions/Transport/TransportOptions.cs|KcpTransportOptions',
    'src/PulseRPC.Abstractions/Transport/TransportOptions.cs|TcpTransportOptions',
    'src/PulseRPC.Abstractions/Transport/TransportOptions.cs|TransportOptions',
    'src/PulseRPC.Abstractions/UnifiedHubAttributes.cs|DeliveryAttribute',
    'src/PulseRPC.Abstractions/UnifiedHubAttributes.cs|PulseHubAttribute'
)

$namespaceRegex = '^\s*namespace\s+([A-Za-z0-9_.]+)\s*[;{]'
$abstractionsNamespaceRegex = '^PulseRPC\.(Client|Shared|Channels|Messaging)$'
$sharedNamespaceRegex = '^PulseRPC\.Abstractions\.'
$publicConcreteRegex = '^\s*public\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+)*(?:class|record|struct)\s+([A-Za-z_][A-Za-z0-9_]*)'

Get-ChildItem -Path (Join-Path $rootPath 'src/PulseRPC.Abstractions') -Recurse -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    ForEach-Object {
    $relativePath = Get-RelativePath $_.FullName
    $lines = Get-Content -Path $_.FullName

    foreach ($line in $lines) {
        if ($line -match $namespaceRegex) {
            $namespace = $Matches[1]
            $key = "$relativePath|$namespace"
            if ($namespace -match $abstractionsNamespaceRegex -and $abstractionsNamespaceAllowlist -notcontains $key) {
                Add-Failure "Abstractions namespace drift: $key"
            }
        }

        if ($line -match $publicConcreteRegex) {
            $typeName = $Matches[1]
            $key = "$relativePath|$typeName"
            if ($abstractionsPublicTypeAllowlist -notcontains $key) {
                Add-Failure "New public concrete type in Abstractions requires boundary review: $key"
            }
        }
    }
    }

Get-ChildItem -Path (Join-Path $rootPath 'src/PulseRPC.Shared') -Recurse -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    ForEach-Object {
    $relativePath = Get-RelativePath $_.FullName
    $lines = Get-Content -Path $_.FullName

    foreach ($line in $lines) {
        if ($line -match $namespaceRegex) {
            $namespace = $Matches[1]
            $key = "$relativePath|$namespace"
            if ($namespace -match $sharedNamespaceRegex -and $sharedNamespaceAllowlist -notcontains $key) {
                Add-Failure "Shared namespace drift: $key"
            }
        }
    }
    }

if ($failures.Count -gt 0) {
    Write-Error ("Boundary check failed:`n" + ($failures -join "`n"))
    exit 1
}

Write-Host 'Boundary check passed.'
