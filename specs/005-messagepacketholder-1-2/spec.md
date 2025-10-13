# Feature Specification: Zero-Copy Message Processing Optimization

**Feature Branch**: `005-messagepacketholder-1-2`
**Created**: 2025-10-13
**Status**: Draft
**Input**: User description: "优化 MessagePacketHolder 分配 1.重新设计消息处理流程; 2.实现零拷贝路径"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - High-Throughput Message Processing (Priority: P1)

As a PulseRPC server operator handling high message volumes (100,000+ messages/second), I need the server to process messages without creating unnecessary memory allocations, so that I can maintain consistent latency and avoid garbage collection pauses that impact user experience.

**Why this priority**: This is the core optimization that directly addresses the identified performance bottleneck. MessagePacketHolder allocation in the hot path creates GC pressure that degrades performance under load. This is critical for production deployments with high traffic.

**Independent Test**: Can be fully tested by sending 100,000 messages/second through the server and measuring memory allocation rate (should show significant reduction in Gen0/Gen1 collections) and P99 latency (should remain under 5ms). Delivers immediate value by improving server capacity and reducing infrastructure costs.

**Acceptance Scenarios**:

1. **Given** a server receiving 100,000 messages per second, **When** messages are processed through the optimized path, **Then** memory allocation rate decreases by at least 80% compared to current implementation
2. **Given** a server under sustained load, **When** monitoring garbage collection metrics, **Then** Gen0 collection frequency decreases by at least 60%
3. **Given** a message processing request, **When** the message flows through the pipeline, **Then** no MessagePacketHolder objects are allocated in the hot path

---

### User Story 2 - Consistent Low-Latency Response (Priority: P1)

As an application developer using PulseRPC for real-time communications, I need message processing to maintain consistent low latency without GC-induced spikes, so that my users experience smooth, responsive interactions.

**Why this priority**: GC pauses directly impact user-perceived performance. Even brief pauses (10-50ms) can cause noticeable lag in real-time applications like games, chat, or collaborative tools. This is essential for maintaining quality of service.

**Independent Test**: Can be fully tested by sending sustained traffic for 10 minutes and measuring P99 latency stability. Should show P99 latency under 5ms with minimal variance (standard deviation < 2ms). Delivers value by enabling real-time application use cases.

**Acceptance Scenarios**:

1. **Given** a server processing messages for 10 minutes, **When** measuring P99 latency, **Then** P99 latency remains under 5 milliseconds
2. **Given** sustained message traffic, **When** GC occurs, **Then** GC pause duration is under 10 milliseconds
3. **Given** latency measurements over 1 hour, **When** calculating latency variance, **Then** P99 latency standard deviation is less than 2 milliseconds

---

### User Story 3 - Backward Compatibility Preservation (Priority: P2)

As a PulseRPC library maintainer, I need the optimized message processing to work with existing client code without requiring changes, so that users can upgrade to the new version without breaking their applications.

**Why this priority**: Breaking changes would prevent adoption of the optimization. While important, this is secondary to achieving the performance improvements. If needed, a major version bump could be acceptable, but seamless compatibility is preferred.

**Independent Test**: Can be fully tested by running the existing integration test suite against the new implementation. All existing tests should pass without modification. Delivers value by enabling zero-downtime upgrades.

**Acceptance Scenarios**:

1. **Given** an existing client application using PulseRPC, **When** the server is upgraded to the optimized version, **Then** all message types continue to process correctly
2. **Given** the existing test suite, **When** running against the optimized implementation, **Then** 100% of tests pass without modification
3. **Given** custom serialization implementations, **When** messages are processed, **Then** custom serializers continue to work without changes

---

### User Story 4 - Observable Performance Metrics (Priority: P3)

As a DevOps engineer monitoring PulseRPC servers, I need clear metrics showing memory allocation improvements, so that I can validate the optimization is working and track performance over time.

**Why this priority**: Monitoring and observability are important for production operations but don't directly improve end-user experience. This can be added after core optimization is complete.

**Independent Test**: Can be fully tested by querying performance counters and verifying allocation metrics are exposed. Should show allocation rate, GC frequency, and hot path metrics. Delivers value by enabling performance troubleshooting.

**Acceptance Scenarios**:

1. **Given** a running server, **When** querying performance metrics, **Then** allocation rate per message is reported
2. **Given** metric collection over time, **When** analyzing trends, **Then** GC frequency reduction is visible in dashboards
3. **Given** performance monitoring tools, **When** tracking message processing, **Then** zero-copy path usage percentage is reported

---

### Edge Cases

- What happens when messages are larger than available pooled buffers?
- How does the system handle memory pressure when buffer pools are exhausted?
- What occurs when async operations need to capture message data beyond the synchronous processing window?
- How are ref struct limitations handled in async code paths?
- What happens when custom deserializers don't support zero-copy operations?
- How does the system behave under extreme load (1M+ messages/second)?
- What occurs when messages arrive faster than they can be processed?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST process messages without allocating MessagePacketHolder objects in the hot path
- **FR-002**: System MUST maintain message processing correctness for all existing message types
- **FR-003**: Message processing pipeline MUST separate synchronous parsing from asynchronous dispatch
- **FR-004**: System MUST use ReadOnlyMemory<byte> or ReadOnlySpan<byte> for message data passing between pipeline stages
- **FR-005**: System MUST maintain support for async message handlers
- **FR-006**: System MUST handle ref struct limitations in async methods through appropriate design patterns
- **FR-007**: System MUST provide fallback paths for scenarios where zero-copy processing is not possible
- **FR-008**: System MUST preserve existing public API contracts unless breaking changes are explicitly approved
- **FR-009**: System MUST handle buffer lifecycle management to prevent use-after-free errors
- **FR-010**: System MUST support buffer pooling to minimize allocations across message processing
- **FR-011**: System MUST track and expose metrics for allocation rate and GC frequency
- **FR-012**: Message header extraction MUST occur synchronously before async dispatch

### Key Entities

- **MessagePacket**: Ref struct containing message header and payload as memory spans, used for zero-copy parsing
- **MessageView**: Class or struct holding message metadata (service name, method name, message ID) and ReadOnlyMemory<byte> payload reference, bridges sync parsing to async processing
- **BufferPool**: Memory pool providing reusable byte buffers to minimize allocations
- **MessagePipeline**: Orchestrates message flow from network receive → parse → dispatch → process → respond

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Memory allocation per message decreases by at least 80% compared to current MessagePacketHolder-based implementation (measured via PerfView or dotnet-counters)
- **SC-002**: Gen0 garbage collection frequency decreases by at least 60% under sustained load of 100,000 messages/second
- **SC-003**: P99 message processing latency remains under 5 milliseconds for messages up to 64KB in size
- **SC-004**: Server can sustain 100,000 messages/second for 10 minutes with P99 latency variance (standard deviation) under 2 milliseconds
- **SC-005**: All existing integration tests pass without modification (100% compatibility)
- **SC-006**: Throughput per CPU core increases by at least 40% compared to current implementation
- **SC-007**: Memory usage per 10,000 concurrent connections decreases by at least 30%
- **SC-008**: GC pause duration remains under 10 milliseconds at P99 under peak load

## Scope & Boundaries *(mandatory)*

### In Scope

- Redesigning message processing pipeline to separate sync parsing from async dispatch
- Replacing MessagePacketHolder with zero-allocation data structures
- Implementing buffer pooling for message data
- Updating HighPerformanceMessageDispatcher to support new processing model
- Modifying ServerChannelManager message routing to avoid allocations
- Creating MessageView or equivalent bridge structure for async processing
- Performance testing and validation
- Documenting new processing flow

### Out of Scope

- Changes to serialization layer (MemoryPack usage remains unchanged)
- Modifications to network transport layer (TCP/KCP listeners unchanged)
- Changes to Source Generator (code generation logic remains the same)
- Client-side optimizations
- Changes to authentication or authorization layers
- Modifications to scheduling or threading models

### Success Indicators

- BenchmarkDotNet tests show allocation reduction
- Production monitoring shows GC pressure reduction
- Latency percentiles improve under load
- No regression in functionality or test coverage

### Failure Indicators

- Allocations increase instead of decrease
- Test failures or compatibility issues
- Latency regression
- Memory leaks or buffer pool exhaustion
- Increased CPU usage

## Assumptions & Constraints *(mandatory)*

### Assumptions

- MessagePacket can remain as a ref struct (C# language constraint)
- Most message processing time is spent in async operations (justifies sync/async split)
- Buffer pools can be sized appropriately for expected message sizes (under 64KB typical)
- Existing Source Generator produces correct dispatching code
- Current MemoryPack serialization is efficient and doesn't need changes
- Performance testing environment can simulate production-like load

### Technical Constraints

- Ref structs cannot be used in async methods (C# language limitation)
- Buffer pools have finite capacity (must handle exhaustion gracefully)
- ReadOnlyMemory<byte> must remain valid for the lifetime of async processing
- Breaking changes require major version bump (semantic versioning)
- Must support .NET 8+ runtime (current project target)

### Business Constraints

- Cannot break existing client applications without major version change
- Must maintain compatibility with current Source Generator output
- Changes should be testable with existing BenchmarkApp infrastructure
- Documentation must be updated to reflect new processing model

## Dependencies *(include if external dependencies exist)*

### Internal Dependencies

- PulseRPC.Messaging: MessagePacket and MessageHeader definitions
- PulseRPC.Server.Dispatch: HighPerformanceMessageDispatcher implementation
- PulseRPC.Server.Channels: ServerChannelManager routing logic
- PulseRPC.Server.Engine: AbstractCompiledMessageDispatcher interface

### External Dependencies

- System.Buffers: ArrayPool<byte> for buffer pooling
- System.Memory: ReadOnlyMemory<byte> and ReadOnlySpan<byte> APIs
- MemoryPack: Serialization remains unchanged but must work with new data structures

### Risk Assessment

- **High Risk**: Breaking existing async handler compatibility if MessageView design is incorrect
- **Medium Risk**: Buffer pool tuning may require multiple iterations to optimize
- **Medium Risk**: Performance gains may not meet 80% allocation reduction target
- **Low Risk**: Existing tests may need minor adjustments for new internal APIs

## Open Questions & Clarifications

None - the feature is well-defined based on the technical review findings. The implementation approach is clear: separate synchronous MessagePacket parsing from asynchronous dispatch using ReadOnlyMemory<byte>, and eliminate MessagePacketHolder allocations in the hot path.
