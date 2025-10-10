# Implementation Plan: Complete Message Dispatch-Process-Response Pipeline

**Branch**: `004-pulserpc-server` | **Date**: 2025-10-10 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/004-pulserpc-server/spec.md`

## Execution Flow (/plan command scope)
```
1. Load feature spec from Input path
   ✅ Loaded: 63 functional requirements across 6 categories
2. Fill Technical Context (scan for NEEDS CLARIFICATION)
   ✅ Project Type: Enterprise .NET RPC Framework
   ✅ Structure: Server-side infrastructure (single project enhanced)
3. Fill the Constitution Check section
   ✅ Constitution loaded (v1.0.0)
4. Evaluate Constitution Check section
   ✅ All constitutional requirements aligned
   ✅ No violations detected
   ✅ Progress: Initial Constitution Check PASS
5. Execute Phase 0 → research.md
   ✅ Technical decisions documented
6. Execute Phase 1 → contracts, data-model.md, quickstart.md, CLAUDE.md
   ✅ All Phase 1 artifacts generated
7. Re-evaluate Constitution Check section
   ✅ Post-design check: PASS
   ✅ Progress: Post-Design Constitution Check PASS
8. Plan Phase 2 → Task generation approach documented
9. STOP - Ready for /tasks command
   ✅ Plan execution complete
```

## Summary

This feature implements a production-grade, high-performance message dispatch-process-response pipeline for PulseRPC.Server. The system must handle network message reception from multiple transports (TCP/KCP), route messages to registered service handlers, invoke business logic with comprehensive error handling, generate responses, and transmit them back to clients—all while maintaining 100,000+ req/s throughput with P95 latency under 5ms.

**Technical Approach**: Build upon PulseRPC.Server's existing three-tier architecture (HighPerformanceMessageEngine → TieredMessageProcessor → HighPerformanceMessageDispatcher) by completing the end-to-end flow from network I/O through service invocation to response transmission. Leverage zero-copy techniques, lock-free data structures, and adaptive batching to achieve performance targets while ensuring fault isolation, graceful degradation, and comprehensive observability.

## Technical Context
**Language/Version**: C# 11 / .NET 9.0
**Primary Dependencies**:
- PulseRPC.Abstractions (transport layer)
- Microsoft.Extensions.* (DI, logging, configuration)
- System.Threading.Channels (high-perf queuing)
- System.Buffers (memory pooling)
**Storage**: In-memory state management (connections, metrics), no persistent storage required
**Testing**: xUnit, FluentAssertions, NSubstitute for mocking, BenchmarkDotNet for performance validation
**Target Platform**: Linux/Windows server (.NET 9.0), Docker containers
**Project Type**: Server infrastructure (enhanced single project with layered architecture)
**Performance Goals**:
- Throughput: 100,000 requests/second minimum (8-core server)
- Latency: P95 < 5ms, P99 < 10ms (small payloads <1KB, normal load)
- Scalability: 10,000 concurrent connections
**Constraints**:
- Zero-copy network I/O where possible
- GC pause times P99 < 10ms
- CPU utilization 95%+ under sustained load
- Message parsing < 100 microseconds
**Scale/Scope**:
- Core server infrastructure affecting all RPC operations
- ~20,000 lines of implementation code
- 50+ unit tests, 20+ integration tests, 10+ performance benchmarks

## Constitution Check
*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Performance-First**: ✅ PASS
- All requirements include measurable performance targets (FR-032 to FR-040)
- Design leverages existing high-performance components (TieredMessageProcessor, lock-free queues)
- Automated performance benchmarks planned with specific thresholds
- Performance regression detection integrated in CI/CD

**Source Generation Over Reflection**: ✅ PASS (Server-side exception)
- Server dispatching uses compiled service handlers (no reflection in hot path)
- Message deserialization uses MemoryPack (source-generated)
- Service registration happens at startup (reflection acceptable during initialization)
- Runtime dispatch uses dictionary lookups with compiled delegates

**Enterprise-Grade Reliability**: ✅ PASS
- Comprehensive error handling (FR-041 to FR-050): exception isolation, graceful degradation
- Health check endpoints (FR-043)
- Resource exhaustion prevention (FR-044, FR-045)
- Graceful shutdown and connection draining (FR-046, FR-047)
- Input validation against injection attacks (FR-048)
- Rate limiting per client (FR-049)

**Test-Driven Development**: ✅ PASS
- Contract tests written first (validate message flow end-to-end)
- Unit tests for all components (dispatcher, processor, serializer)
- Integration tests for complete request-response cycle
- Performance benchmarks with pass/fail criteria
- Target >90% code coverage for core pipeline

**Modern .NET Standards**: ✅ PASS
- Async/await throughout (ValueTask for hot paths)
- Nullable reference types enabled
- Records for DTOs and immutable data
- Dependency injection for all components
- IAsyncEnumerable for streaming responses (FR-026)
- CancellationToken support for timeout and disconnection (FR-017, FR-018)

*No constitutional violations requiring justification*

## Project Structure

### Documentation (this feature)
```
specs/004-pulserpc-server/
├── spec.md              # Feature specification (input)
├── plan.md              # This file (/plan command output)
├── research.md          # Phase 0 output (technical decisions)
├── data-model.md        # Phase 1 output (entities and state)
├── quickstart.md        # Phase 1 output (validation scenarios)
├── contracts/           # Phase 1 output (internal contracts)
│   ├── message-flow.yaml        # End-to-end message flow contract
│   ├── dispatcher-api.yaml      # IMessageDispatcher contract
│   └── service-handler.yaml     # IServiceHandler contract
└── tasks.md             # Phase 2 output (/tasks command)
```

### Source Code (repository root)
```
src/PulseRPC.Server/
├── Engine/
│   ├── HighPerformanceMessageEngine.cs         # Enhanced: Complete message flow
│   ├── TieredMessageProcessor.cs               # Existing: Three-tier buffering
│   └── MessageEngineConfiguration.cs           # Enhanced: New config options
├── Dispatch/
│   ├── HighPerformanceMessageDispatcher.cs     # Enhanced: Service routing
│   ├── IServiceHandler.cs                      # New: Service handler interface
│   ├── CompiledServiceInvoker.cs               # New: Compiled method invocation
│   └── RequestContextFactory.cs                # New: Context creation
├── Response/
│   ├── ResponseProcessor.cs                    # New: Response generation
│   ├── ResponseSerializer.cs                   # New: Response serialization
│   └── ResponseTransmitter.cs                  # New: Network transmission
├── Pipeline/
│   ├── MessagePipeline.cs                      # New: End-to-end orchestration
│   ├── PipelineStage.cs                        # New: Stage abstraction
│   └── PipelineMetrics.cs                      # New: Pipeline observability
├── ErrorHandling/
│   ├── ErrorResponseFactory.cs                 # New: Error response creation
│   ├── ExceptionSerializer.cs                  # New: Exception serialization
│   └── FaultIsolationPolicy.cs                 # New: Fault isolation
├── Observability/
│   ├── PipelineMetricsCollector.cs             # New: Metrics collection
│   ├── DistributedTracingIntegration.cs        # New: Tracing support
│   └── DiagnosticEndpoints.cs                  # New: Debug endpoints
└── Configuration/
    ├── PipelineOptions.cs                      # New: Pipeline configuration
    ├── TimeoutPolicy.cs                        # New: Timeout configuration
    └── BackpressurePolicy.cs                   # New: Backpressure configuration

tests/PulseRPC.Server.Tests/
├── Contract/
│   ├── EndToEndMessageFlowTests.cs             # New: Complete flow validation
│   ├── DispatcherContractTests.cs              # New: Dispatcher interface tests
│   └── ServiceHandlerContractTests.cs          # New: Handler interface tests
├── Unit/
│   ├── Engine/
│   │   ├── MessageEngineTests.cs               # Enhanced: Full flow tests
│   │   └── PipelineStageTests.cs               # New: Stage unit tests
│   ├── Dispatch/
│   │   ├── DispatcherTests.cs                  # Enhanced: Routing tests
│   │   ├── ServiceInvokerTests.cs              # New: Invocation tests
│   │   └── RequestContextTests.cs              # New: Context tests
│   ├── Response/
│   │   ├── ResponseProcessorTests.cs           # New: Response generation tests
│   │   ├── ResponseSerializerTests.cs          # New: Serialization tests
│   │   └── ResponseTransmitterTests.cs         # New: Transmission tests
│   └── ErrorHandling/
│       ├── ErrorResponseTests.cs               # New: Error handling tests
│       └── FaultIsolationTests.cs              # New: Isolation tests
├── Integration/
│   ├── FullPipelineIntegrationTests.cs         # New: End-to-end integration
│   ├── ConcurrentLoadTests.cs                  # New: Multi-client scenarios
│   ├── ErrorRecoveryTests.cs                   # New: Failure scenarios
│   └── BackpressureIntegrationTests.cs         # New: Overload scenarios
└── Performance/
    ├── ThroughputBenchmarks.cs                 # New: 100K req/s validation
    ├── LatencyBenchmarks.cs                    # New: P95/P99 validation
    └── ScalabilityBenchmarks.cs                # New: 10K connections test
```

**Structure Decision**: Enhanced existing PulseRPC.Server project with new namespaces (Response, Pipeline, ErrorHandling, Observability) to complete the message flow while preserving existing high-performance components (Engine, Dispatch, Memory, Scheduling). Tests organized by contract/unit/integration/performance to support TDD workflow.

## Phase 0: Outline & Research

### Research Areas

1. **Current PulseRPC.Server Architecture Analysis**
   - **Decision**: Build on existing three-tier architecture (L1→L2→L3 buffering)
   - **Rationale**: HighPerformanceMessageEngine + TieredMessageProcessor already implement zero-copy reception and adaptive batching. Extending this proven architecture ensures consistency and leverages existing optimizations.
   - **Alternatives Considered**:
     - Full rewrite: Rejected due to risk and loss of proven performance characteristics
     - Parallel pipeline: Rejected as existing architecture already handles concurrency via lock-free queues

2. **Service Invocation Strategy**
   - **Decision**: Compiled delegate invocation with expression trees
   - **Rationale**: Compile service method calls to delegates at registration time (acceptable reflection during startup), then use delegates in hot path (zero reflection at runtime). Achieves ~5-10x speedup vs runtime reflection.
   - **Alternatives Considered**:
     - Runtime reflection: Rejected due to performance cost (100+ microseconds per invocation)
     - Source generation for server: Rejected as services are dynamically registered at runtime

3. **Response Transmission Approach**
   - **Decision**: Batched writes with Channels for coordination
   - **Rationale**: System.Threading.Channels provides high-throughput, low-overhead coordination between processing threads and I/O threads. Batching small responses reduces syscalls and TCP overhead.
   - **Alternatives Considered**:
     - One thread per connection: Rejected due to thread pool exhaustion with 10K connections
     - Direct socket writes: Rejected as blocking I/O kills scalability

4. **Error Handling and Fault Isolation**
   - **Decision**: Exception boundary at service invocation with structured error responses
   - **Rationale**: Catch all exceptions at dispatcher level, serialize to error response, continue processing other requests. Prevents one bad service from crashing server.
   - **Alternatives Considered**:
     - Process isolation (separate AppDomain): Rejected as .NET Core doesn't support AppDomain isolation
     - Circuit breaker pattern: Deferred to future work (not required for MVP)

5. **Observability and Diagnostics**
   - **Decision**: Activity-based distributed tracing + custom metrics
   - **Rationale**: System.Diagnostics.Activity provides W3C Trace Context propagation with minimal overhead. Custom metrics via IMetricsCollector capture pipeline-specific KPIs.
   - **Alternatives Considered**:
     - Full OpenTelemetry SDK: Rejected due to allocation overhead (adds 1-2ms to P95)
     - No tracing: Rejected as production debugging requires correlation across services

6. **Backpressure and Flow Control**
   - **Decision**: Multi-level backpressure (queue depth → connection throttling → connection rejection)
   - **Rationale**: Gradual degradation prevents cascading failures. L3 queue depth triggers connection accept slowdown; if still overloaded, reject new connections with 503.
   - **Alternatives Considered**:
     - Drop messages silently: Rejected as clients need explicit error
     - Unlimited queuing: Rejected due to out-of-memory risk

**Output**: research.md documenting all technical decisions

## Phase 1: Design & Contracts

### Data Model (data-model.md)

#### Core Entities

**Message**
- Fields: ProtocolVersion (byte), MessageType (enum), RequestId (Guid), ServiceName (string), MethodName (string), Payload (ReadOnlyMemory<byte>), Metadata (Dictionary<string, string>)
- Validation: ServiceName non-empty, MethodName non-empty, Payload ≤ 10MB, ProtocolVersion matches server
- State Transitions: Received → Parsed → Dispatched → Processed → Response Created → Sent → Disposed
- Relationships: Belongs to Connection (1:1 for request, 1:1 for response)

**Connection**
- Fields: ConnectionId (string), ClientAddress (IPEndPoint), TransportProtocol (enum), State (enum), CreatedAt (DateTime), LastActivityAt (DateTime), MessagesSent (long), MessagesReceived (long), ErrorCount (long)
- Validation: ConnectionId unique, State valid transition
- State Transitions: Connecting → Active → Closing → Closed (one-way)
- Relationships: Has many Messages (1:N inbound, 1:N outbound)

**ServiceRegistration**
- Fields: ServiceName (string), ServiceType (Type), Methods (Dictionary<string, CompiledMethodInvoker>), TimeoutPolicy (TimeSpan), Priority (enum), State (enum)
- Validation: ServiceName unique, all methods have compiled invokers
- State Transitions: Registered → Active → Paused → Active → Unregistered
- Relationships: Handles Messages (1:N)

**RequestContext**
- Fields: RequestId (Guid), ClientId (string), ConnectionId (string), Metadata (IReadOnlyDictionary<string, string>), CancellationToken (CancellationToken), StartTimestamp (long)
- Validation: RequestId non-zero, CancellationToken not cancelled at creation
- Lifecycle: Created → Passed to Service → Disposed
- Relationships: Associated with Message (1:1) and Connection (N:1)

**ResponseEnvelope**
- Fields: RequestId (Guid), IsSuccess (bool), Payload (ReadOnlyMemory<byte>), ExceptionDetails (ExceptionData?), CompletedAt (DateTime)
- Validation: RequestId matches request, Payload XOR ExceptionDetails populated
- Lifecycle: Created → Serialized → Transmitted → Disposed
- Relationships: Response to Message (1:1)

### API Contracts (contracts/)

**contract/message-flow.yaml**: End-to-end message flow from network ingress to egress
**contract/dispatcher-api.yaml**: IMessageDispatcher interface contract (service registration, message dispatch)
**contract/service-handler.yaml**: IServiceHandler interface contract (method invocation, context passing)

(Full YAML schemas in contracts/ directory)

### Contract Tests

**tests/Contract/EndToEndMessageFlowTests.cs**: Validate complete request-response cycle including error cases
**tests/Contract/DispatcherContractTests.cs**: Verify dispatcher respects interface contract
**tests/Contract/ServiceHandlerContractTests.cs**: Verify service handlers receive correct context

(These tests will FAIL until implementation complete)

### Integration Test Scenarios (quickstart.md)

1. **Normal Request-Response**: Send request, validate response received with correct RequestId
2. **Concurrent Load**: 5000 clients send simultaneously, validate all responses correct
3. **Service Exception**: Service throws, validate error response with exception details
4. **Slow Method**: 5-second method, validate timeout handling
5. **Malformed Message**: Send corrupted data, validate protocol error response
6. **Connection Loss**: Drop connection mid-processing, validate cleanup
7. **Backpressure**: Overload server, validate graceful degradation

### Agent Context Update

Running update script to add new technical context to CLAUDE.md...

**Output**: data-model.md, contracts/*.yaml, contract test files (failing), quickstart.md, CLAUDE.md updated

## Phase 2: Task Planning Approach
*This section describes what the /tasks command will do - DO NOT execute during /plan*

**Task Generation Strategy**:
1. Load `tasks-template.md` as base structure
2. Extract contract tests from contracts/ → contract test implementation tasks [P]
3. Extract entities from data-model.md → entity/model creation tasks [P]
4. Extract pipeline stages from design → stage implementation tasks (sequential by dependency)
5. Extract integration scenarios from quickstart.md → integration test tasks
6. Generate unit tests for each new component → unit test tasks [P]
7. Generate performance benchmarks from performance requirements (FR-032 to FR-040) → benchmark tasks
8. Add observability tasks (metrics, logging, tracing) → observability tasks
9. Add documentation tasks (XML docs, README updates) → documentation tasks [P]

**Ordering Strategy**:
- **Phase 1: Setup & Contracts** [P] (parallel)
  - Create entities and models (Message, Connection, RequestContext, ResponseEnvelope)
  - Define interfaces (IServiceHandler, IPipelineStage, IResponseProcessor)
  - Write contract tests (all failing)
- **Phase 2: Core Pipeline** (sequential with internal parallelism)
  - Implement request reception (integrate with existing HighPerformanceMessageEngine)
  - Implement message dispatching (enhance HighPerformanceMessageDispatcher)
  - Implement service invocation (CompiledServiceInvoker)
  - Implement response generation (ResponseProcessor, ErrorResponseFactory)
  - Implement response transmission (ResponseTransmitter)
- **Phase 3: Error Handling & Resilience** [P]
  - Implement fault isolation (FaultIsolationPolicy)
  - Implement timeout enforcement (TimeoutPolicy)
  - Implement backpressure (BackpressurePolicy)
- **Phase 4: Observability** [P]
  - Implement metrics collection (PipelineMetricsCollector)
  - Implement distributed tracing (DistributedTracingIntegration)
  - Implement diagnostic endpoints (DiagnosticEndpoints)
- **Phase 5: Integration & Performance** (sequential)
  - Run integration tests (validate quickstart scenarios)
  - Run performance benchmarks (validate FR-032 to FR-040)
  - Fix performance regressions
  - Final validation (72-hour stress test)

**Estimated Output**: 45-50 numbered tasks in tasks.md

**IMPORTANT**: Tasks.md will be generated by /tasks command, NOT by /plan

## Phase 3+: Future Implementation
*These phases are beyond the scope of the /plan command*

**Phase 3**: Task execution (/tasks command creates tasks.md with 45-50 tasks)
**Phase 4**: Implementation (TDD: write tests → implement → refactor → verify performance)
**Phase 5**: Validation (run full test suite, 72-hour stress test, production canary)

## Complexity Tracking
*No constitutional violations requiring justification*

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None      | N/A        | N/A                                 |

## Progress Tracking
*This checklist is updated during execution flow*

**Phase Status**:
- [x] Phase 0: Research complete (research.md created)
- [x] Phase 1: Design complete (data-model.md, contracts/, quickstart.md, CLAUDE.md)
- [x] Phase 2: Task planning approach documented
- [ ] Phase 3: Tasks generated (/tasks command)
- [ ] Phase 4: Implementation complete
- [ ] Phase 5: Validation passed

**Gate Status**:
- [x] Initial Constitution Check: PASS
- [x] Post-Design Constitution Check: PASS
- [x] All NEEDS CLARIFICATION resolved
- [x] Complexity deviations documented (none)

---
*Based on Constitution v1.0.0 - See `.specify/memory/constitution.md`*
