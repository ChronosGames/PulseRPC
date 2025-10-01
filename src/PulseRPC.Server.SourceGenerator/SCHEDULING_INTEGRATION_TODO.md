# Source Generator Integration for ServiceName-based Scheduling

## T026: Extract ServiceName from ChannelAttribute

### Required Changes in PulseRPCSourceGenerator.cs

1. **When processing IPulseHub interfaces**, extract the `ServiceName` property from `ChannelAttribute`:

```csharp
// In the interface processing logic:
var channelAttr = interfaceSymbol.GetAttributes()
    .FirstOrDefault(a => a.AttributeClass?.Name == "ChannelAttribute");

string serviceName;
if (channelAttr != null)
{
    // Try to get ServiceName property
    var serviceNameArg = channelAttr.NamedArguments
        .FirstOrDefault(na => na.Key == "ServiceName");

    serviceName = serviceNameArg.Value.Value?.ToString()
        ?? interfaceSymbol.Name; // Fallback to interface name
}
else
{
    serviceName = interfaceSymbol.Name; // Default fallback
}
```

2. **Pass ServiceName to generated service registration**:

```csharp
// In generated code, add ServiceName metadata
// This will be used by the scheduler to create ServiceSchedulingKey
public static class {InterfaceName}ServiceMetadata
{
    public const string ServiceName = "{extractedServiceName}";
    public const string ChannelName = "{channelName}";
}
```

3. **Update service invocation code** to use ServiceName for scheduling:

```csharp
// In generated service proxy/dispatcher:
var serviceContext = serviceProvider.GetRequiredService<IServiceContext>();
if (scheduler != null && serviceContext.IsAuthenticated)
{
    var key = new ServiceSchedulingKey(
        {InterfaceName}ServiceMetadata.ServiceName,
        serviceContext.ServiceId!
    );

    await scheduler.ScheduleAsync(key, async () =>
    {
        // Actual service method invocation
        await serviceMethod.Invoke(...);
    });
}
```

## T027: ServiceAnalyzer Validation

### Add Analyzer Rule in ServiceAnalyzer.cs

```csharp
// Analyzer rule ID: PULSE001
// Severity: Warning
// Message: "Consider specifying ServiceName for stateful IPulseHub services to enable thread-affinity scheduling"

public override void Initialize(AnalysisContext context)
{
    context.RegisterSymbolAction(AnalyzeInterface, SymbolKind.NamedType);
}

private void AnalyzeInterface(SymbolAnalysisContext context)
{
    var interfaceSymbol = (INamedTypeSymbol)context.Symbol;

    // Check if implements IPulseHub
    if (!ImplementsIPulseHub(interfaceSymbol))
        return;

    // Check for ChannelAttribute
    var channelAttr = interfaceSymbol.GetAttributes()
        .FirstOrDefault(a => a.AttributeClass?.Name == "ChannelAttribute");

    if (channelAttr == null)
        return; // Another analyzer will handle missing ChannelAttribute

    // Check if ServiceName is specified
    var hasServiceName = channelAttr.NamedArguments
        .Any(na => na.Key == "ServiceName" && na.Value.Value != null);

    if (!hasServiceName)
    {
        var diagnostic = Diagnostic.Create(
            Rule_ServiceNameRecommended,
            interfaceSymbol.Locations[0],
            interfaceSymbol.Name
        );
        context.ReportDiagnostic(diagnostic);
    }
}

private static DiagnosticDescriptor Rule_ServiceNameRecommended = new DiagnosticDescriptor(
    id: "PULSE001",
    title: "ServiceName not specified for IPulseHub",
    messageFormat: "Consider specifying ServiceName in ChannelAttribute for '{0}' to enable thread-affinity scheduling",
    category: "Performance",
    defaultSeverity: DiagnosticSeverity.Info,
    isEnabledByDefault: true,
    description: "Specifying ServiceName enables the scheduler to ensure thread-affinity for stateful services."
);
```

## Implementation Status

- [ ] T026: Update PulseRPCSourceGenerator.cs to extract ServiceName
- [ ] T027: Add ServiceAnalyzer rule for ServiceName validation
- [ ] Test with sample IPulseHub interface
- [ ] Verify generated code includes ServiceName metadata
- [ ] Verify analyzer warning appears when ServiceName is missing

## Testing

Create a test interface:

```csharp
[Channel("player-channel", ServiceName = "PlayerService")]
public interface IPlayerHub : IPulseHub
{
    Task HandlePlayerLogin(string playerId);
}
```

Expected generated code should include:
- `PlayerServiceMetadata.ServiceName = "PlayerService"`
- Scheduler integration in method dispatch

## Notes

These changes integrate seamlessly with the existing source generator architecture and maintain backward compatibility (ServiceName is optional).