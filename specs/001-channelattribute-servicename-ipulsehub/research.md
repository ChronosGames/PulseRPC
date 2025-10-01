# Research Report: ServiceName-Based Thread Scheduling

**Feature**: 001-channelattribute-servicename-ipulsehub
**Date**: 2025-09-30

## Research Overview

This document consolidates research findings for implementing ServiceName-based thread scheduling in PulseRPC, ensuring thread-affinity for IPulseHub services based on ServiceName+ServiceId composite keys.

## 1. Thread Scheduling Architecture

### Decision: System.Threading.Channels + Dedicated Thread Pool

**Rationale**:
- `System.Threading.Channels` provides high-performance, lock-free queuing with backpressure support
- Dedicated thread pool allows precise control over thread affinity and lifecycle
- Aligns with existing `HighPerformanceMessageEngine` architecture which already uses Channels
- Supports bounded/unbounded channels for different degradation strategies

**Alternatives Considered**:
- **TaskScheduler**: Rejected - too high-level, doesn't provide thread affinity guarantees
- **ThreadPool.QueueUserWorkItem**: Rejected - no control over which thread executes work
- **Actor Model (Akka.NET)**: Rejected - adds external dependency, constitutional violation (complexity)

**Implementation Pattern**:
```
ServiceName+ServiceId → Hash → Thread Index → Dedicated Channel → Worker Thread
```

Each worker thread continuously reads from its assigned channel, ensuring sequential execution for the same ServiceName+ServiceId combination.

## 2. ServiceId Injection Mechanism

### Decision: IServiceContext Interface with Authentication Hook

**Rationale**:
- Aligns with existing authentication flow in PulseRPC
- ServiceId can be extracted from authentication tokens or generated during auth
- IServiceContext provides clean separation between connection state and service state
- Supports both server-generated and client-provided ServiceId patterns

**Alternatives Considered**:
- **Ambient Context (AsyncLocal)**: Rejected - performance overhead, harder to test
- **Method Parameter Injection**: Rejected - breaks existing IPulseHub interface contracts
- **CallContext**: Rejected - legacy .NET Framework pattern, not recommended for modern .NET

**Authentication Integration**:
The ServiceId will be set during the authentication phase and stored in the connection context. The scheduler retrieves it when routing messages.

## 3. ChannelAttribute Extension

### Decision: Add ServiceName Property to Existing ChannelAttribute

**Rationale**:
- Minimal API surface change - leverages existing attribute
- Source generator already processes ChannelAttribute
- Backward compatible - ServiceName is optional (defaults to interface name)
- Compile-time extraction via PulseRPC.Server.SourceGenerator

**Alternatives Considered**:
- **New ServiceNameAttribute**: Rejected - unnecessary attribute proliferation
- **Convention-based (interface name)**: Rejected - not explicit enough for scheduling requirements
- **Configuration-based**: Rejected - less discoverable, not compile-time safe

**Usage Pattern**:
```csharp
[Channel("player-channel", ServiceName = "PlayerService")]
public interface IPlayerHub : IPulseHub
{
    Task HandlePlayerAction(PlayerAction action);
}
```

## 4. Thread Pool Configuration Strategy

### Decision: Configurable Initial + Max Size with Dynamic Scaling

**Rationale**:
- Supports different deployment scenarios (small dev vs large production)
- Dynamic scaling prevents resource exhaustion
- Aligns with constitution's performance-first principle
- Configuration via SchedulerConfiguration options

**Configuration Parameters**:
- `InitialThreadCount`: Starting thread pool size (default: Environment.ProcessorCount)
- `MaxThreadCount`: Maximum threads (default: Environment.ProcessorCount * 2)
- `ThreadIdleTimeout`: Time before idle thread termination (default: 30s)
- `ChannelCapacity`: Per-thread channel capacity (default: 1024)

**Alternatives Considered**:
- **Fixed Thread Pool**: Rejected - not flexible for varying loads
- **Unlimited Growth**: Rejected - resource exhaustion risk
- **Single Thread Per ServiceName**: Rejected - doesn't scale with many ServiceNames

## 5. Message Degradation Strategy

### Decision: Bounded Channels with Block/Wait Behavior + Priority-Based Dropping

**Rationale**:
- Spec requirement: "阻塞等待，内部自行使用基于Channel的消息降级"
- Bounded channels provide backpressure when thread is overloaded
- Blocking behavior ensures no message loss under normal conditions
- Priority-based dropping (L3 degradation) handles extreme overload
- Isolation: only affects the specific ServiceName+ServiceId, not entire system

**Degradation Levels**:
1. **Normal**: Messages queued in bounded channel
2. **Degraded**: Channel reaches capacity, producer blocks/waits
3. **Critical** (L3): Oldest low-priority messages dropped to make room

**Alternatives Considered**:
- **Unbounded Channels**: Rejected - memory exhaustion risk
- **Immediate Rejection**: Rejected - doesn't meet "阻塞等待" requirement
- **Global Degradation**: Rejected - violates isolation requirement

## 6. ServiceName+ServiceId Hashing Strategy

### Decision: Consistent Hashing with Murmur3

**Rationale**:
- Fast, low-collision hash function suitable for thread routing
- Consistent hashing minimizes thread reassignment on pool resizing
- Well-tested in distributed systems (similar to connection pooling)

**Implementation**:
```csharp
int threadIndex = Math.Abs(hash(ServiceName + ServiceId)) % threadPoolSize;
```

**Alternatives Considered**:
- **Round-robin**: Rejected - doesn't guarantee same thread for same key
- **Random**: Rejected - no affinity guarantee
- **Built-in GetHashCode**: Rejected - non-deterministic across app domains

## 7. Performance Monitoring and Diagnostics

### Decision: Built-in Metrics via ILogger + Performance Counters

**Rationale**:
- Constitution requires observability (FR-008)
- Leverage existing Microsoft.Extensions.Logging infrastructure
- Minimal performance overhead via structured logging
- Supports integration with Application Insights, Prometheus, etc.

**Metrics to Track**:
- Queue depth per thread
- Message processing latency (P50/P95/P99)
- Thread utilization percentage
- Dropped message count (degradation events)
- ServiceName+ServiceId distribution across threads

**Alternatives Considered**:
- **Custom Metrics System**: Rejected - unnecessary complexity
- **No Metrics**: Rejected - violates constitution (FR-008)

## 8. Error Handling: Missing ServiceId

### Decision: Throw InvalidOperationException Early

**Rationale**:
- Spec requirement: "未设置 ServiceId 则无法进行调度，抛出异常"
- Fail-fast principle prevents silent failures
- Forces developers to properly configure authentication flow
- Clear exception message aids debugging

**Exception Message Pattern**:
```
"ServiceId not set for ServiceName '{serviceName}'.
Ensure authentication middleware sets ServiceId in IServiceContext."
```

**Alternatives Considered**:
- **Default/Fallback ServiceId**: Rejected - masks configuration errors
- **Logging + Continue**: Rejected - doesn't meet explicit requirement
- **Use ConnectionId as fallback**: Rejected - incorrect semantics

## 9. Integration with Existing HighPerformanceMessageEngine

### Decision: Inject IServiceScheduler Before Message Dispatch

**Rationale**:
- Existing `HighPerformanceMessageEngine` already has tiered processing (L1→L2→L3)
- Scheduler injection point: after L2 (batch processing), before service invocation
- Maintains existing performance characteristics
- Minimal changes to established architecture

**Integration Point**:
```
L1 (Ring Buffer) → L2 (Batch) → L3 (Priority) → [NEW: ServiceScheduler] → Service Invocation
```

**Alternatives Considered**:
- **Replace Existing Engine**: Rejected - too invasive, breaks existing features
- **Parallel Scheduler**: Rejected - complexity, potential for inconsistent behavior
- **Pre-L1 Injection**: Rejected - adds latency to hot path

## Research Validation

All unknowns from Technical Context have been resolved:
- ✅ Thread pool sizing strategy defined
- ✅ Scheduling algorithm selected (consistent hashing)
- ✅ ServiceId injection mechanism designed (IServiceContext)
- ✅ Degradation strategy specified (bounded channels + L3 dropping)
- ✅ Performance monitoring approach determined
- ✅ Integration points with existing architecture identified

## Constitutional Compliance

- **Performance-First**: System.Threading.Channels selected for high throughput, benchmarks planned
- **Source Generation**: ServiceName extraction via existing SourceGenerator
- **Enterprise Reliability**: Comprehensive error handling, degradation, and monitoring
- **TDD**: Test strategy defined in Phase 1
- **Modern .NET**: Async/await, Channels, DI, nullable types throughout

---

**Next Phase**: Design & Contracts (Phase 1)