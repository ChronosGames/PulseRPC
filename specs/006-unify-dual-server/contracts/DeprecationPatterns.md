# Deprecation Patterns

**Feature**: `006-unify-dual-server`
**Date**: 2025-10-13

---

## Deprecation Strategy

### ServerHost Deprecation

**ObsoleteAttribute Configuration**:
```csharp
[Obsolete(
    "ServerHost is deprecated. Use PulseServer instead. " +
    "See migration guide at https://github.com/your-org/PulseRPC/blob/main/specs/006-unify-dual-server/quickstart.md",
    error: false
)]
public sealed class ServerHost : IDisposable
{
    // Implementation delegates to unified PulseServer
}
```

**Key Elements**:
- **Message**: Clear, actionable ("Use PulseServer instead")
- **Guidance**: Link to migration documentation
- **Warning Level**: `error: false` (compiler warning, not error)
- **Timeline**: No removal planned (maintain indefinitely for maximum compatibility)

---

## Deprecation Messages

### For ServerHost Users

**Compiler Warning**:
```
warning CS0618: 'ServerHost' is obsolete: 'ServerHost is deprecated. Use PulseServer instead.
See migration guide at https://github.com/your-org/PulseRPC/blob/main/specs/006-unify-dual-server/quickstart.md'
```

**IDE Quick Fix** (suggestion):
```csharp
// Before (generates warning)
var serverHost = new ServerHost(transport, options);

// Suggested fix (IDE action: "Migrate to PulseServer")
var server = new PulseServerBuilder()
    .AddTcpTransport(port, options => { /* ... */ })
    .Build();
```

---

## Custom Extension Methods Guidance

**Breaking Change Notice** (from clarification decision):

Custom extension methods targeting `ServerHost` will require rewriting. This is an **accepted trade-off** for a cleaner unified API.

**Example Migration**:

**Before** (extension targeting ServerHost):
```csharp
public static class ServerHostExtensions
{
    public static void ConfigureBackpressure(this ServerHost server, int maxQueue)
    {
        server.BackpressurePolicy.UpdateMaxQueueDepth(maxQueue);
    }
}
```

**After** (extension targeting unified PulseServer):
```csharp
public static class PulseServerExtensions
{
    public static void ConfigureBackpressure(this PulseServer server, int maxQueue)
    {
        // Access pipeline coordinator for backpressure configuration
        var pipeline = server.GetPipelineCoordinator();
        pipeline.BackpressurePolicy.UpdateMaxQueueDepth(maxQueue);
    }
}
```

**Documentation**: Provide clear migration examples in `quickstart.md` for common extension patterns.

---

## Migration Timeline

| Phase | Timeline | Action |
|-------|----------|--------|
| **Release** | v2.0.0 | Unified PulseServer released, ServerHost marked `[Obsolete]` |
| **Warning Period** | Indefinite | ServerHost continues to work, generates compiler warnings |
| **Support** | Indefinite | ServerHost facade maintained for full binary compatibility |
| **Removal** | None planned | Facade retained indefinitely (no breaking changes) |

---

## FAQ for Users

**Q: Do I need to migrate immediately?**
A: No. Existing code continues to work via ServerHost facade. Migrate at your convenience.

**Q: What if I never migrate?**
A: Your code will continue to function correctly. You'll receive compiler warnings that can be suppressed if desired.

**Q: How do I suppress the deprecation warning?**
A: Use `#pragma warning disable CS0618` around ServerHost usage (not recommended - prefer migration).

**Q: Will ServerHost be removed in a future version?**
A: No removal is planned. The facade will be maintained indefinitely for maximum compatibility.

**Q: What about custom extension methods?**
A: Extension methods targeting ServerHost-specific types will need rewriting to target PulseServer. See migration guide for patterns.

---

**Next**: See `quickstart.md` for step-by-step migration examples.
