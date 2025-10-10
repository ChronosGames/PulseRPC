# Tasks: Complete Message Dispatch-Process-Response Pipeline

**Input**: Design documents from `specs/004-pulserpc-server/`
**Prerequisites**: plan.md, research.md, data-model.md, contracts/, quickstart.md

## Execution Flow (main)
```
1. Load plan.md from feature directory
   ✅ Extracted: C# 11/.NET 9.0, System.Threading.Channels, xUnit/FluentAssertions
2. Load optional design documents:
   ✅ data-model.md: 5 entities extracted
   ✅ contracts/: 3 contract files found
   ✅ research.md: 6 technical decisions documented
   ✅ quickstart.md: 7 integration scenarios + 3 performance tests
3. Generate tasks by category:
   ✅ Setup: 3 tasks
   ✅ Tests: 13 tasks (3 contract + 7 integration + 3 performance)
   ✅ Core: 25 tasks (5 entities + 15 implementations + 5 enhancements)
   ✅ Integration: 6 tasks
   ✅ Polish: 8 tasks
4. Apply task rules:
   ✅ [P] marked for parallel-safe tasks (different files)
   ✅ TDD ordering enforced (tests before implementation)
5. Number tasks sequentially: T001-T055
6. Dependencies validated
7. Parallel execution examples provided
8. Validation:
   ✅ All contracts have tests
   ✅ All entities have model tasks
   ✅ All integration scenarios covered
9. Return: SUCCESS (55 tasks ready for execution)
```

## Format: `[ID] [P?] Description`
- **[P]**: Can run in parallel (different files, no dependencies)
- All file paths are absolute from repository root

---

## Phase 3.1: Setup & Configuration

### T001: [X] [P] Create new namespaces and folder structure
**Type**: Setup
**Priority**: HIGH
**Dependencies**: None
**Files**:
- Create `src/PulseRPC.Server/Response/`
- Create `src/PulseRPC.Server/Pipeline/`
- Create `src/PulseRPC.Server/ErrorHandling/`
- Create `src/PulseRPC.Server/Observability/`
- Create `tests/PulseRPC.Server.Tests/Contract/`
- Create `tests/PulseRPC.Server.Tests/Unit/Response/`
- Create `tests/PulseRPC.Server.Tests/Unit/ErrorHandling/`
- Create `tests/PulseRPC.Server.Tests/Integration/`
- Create `tests/PulseRPC.Server.Tests/Performance/`

**Description**:
Create all new namespace directories in src/PulseRPC.Server and corresponding test directories. Verify existing directories (Engine, Dispatch, Memory, Scheduling) are present.

**Acceptance Criteria**:
- All 4 new src namespaces created
- All 5 new test directories created
- Directory structure matches plan.md

---

### T002: [X] [P] Add project dependencies and NuGet packages
**Type**: Setup
**Priority**: HIGH
**Dependencies**: None
**Files**:
- `src/PulseRPC.Server/PulseRPC.Server.csproj`
- `tests/PulseRPC.Server.Tests/PulseRPC.Server.Tests.csproj`

**Description**:
Verify and add required NuGet packages:
- Server: System.Threading.Channels (already present), System.Buffers (already present)
- Tests: xUnit (present), FluentAssertions (present), NSubstitute (present), BenchmarkDotNet (add)

Add BenchmarkDotNet package reference to test project for performance benchmarks.

**Acceptance Criteria**:
- BenchmarkDotNet added to test project
- All dependencies restored (`dotnet restore`)
- Project builds without errors

---

### T003: [X] [P] Create baseline configuration classes
**Type**: Setup
**Priority**: HIGH
**Dependencies**: T001
**Files**:
- `src/PulseRPC.Server/Configuration/PipelineOptions.cs`
- `src/PulseRPC.Server/Configuration/TimeoutPolicy.cs`
- `src/PulseRPC.Server/Configuration/BackpressurePolicy.cs`

**Description**:
Create configuration option classes for pipeline configuration, timeout policies, and backpressure control. These will be populated in later tasks but need baseline structure now.

```csharp
// PipelineOptions.cs
public sealed class PipelineOptions {
    public int MaxConcurrentRequests { get; set; } = 10000;
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);
    // ... other options
}
```

**Acceptance Criteria**:
- 3 configuration classes created with default values
- XML documentation added
- Classes compile without errors

---

## Phase 3.2: Tests First (TDD) ⚠️ MUST COMPLETE BEFORE 3.3

**CRITICAL**: These tests MUST be written and MUST FAIL before ANY implementation starts.

### T004: [P] Contract test for end-to-end message flow
**Type**: Test - Contract
**Priority**: HIGH
**Dependencies**: T001
**Files**: `tests/PulseRPC.Server.Tests/Contract/EndToEndMessageFlowTests.cs`

**Description**:
Write contract tests validating the complete message flow from network ingress to egress. Based on `contracts/message-flow.yaml`.

Test cases:
1. `MessageFlow_ShouldCompleteAllStages_ForValidRequest`: Send request → validate all 5 stages execute → response received
2. `MessageFlow_ShouldHandleProtocolVersionMismatch`: Send request with wrong version → protocol error response
3. `MessageFlow_ShouldHandlePayloadTooLarge`: Send 11MB payload → error response with size limit message
4. `MessageFlow_ShouldHandleServiceNotFound`: Send request to non-existent service → service not found error
5. `MessageFlow_ShouldHandleTimeout`: Send request that times out → timeout error response
6. `MessageFlow_ShouldPreserveFIFOOrdering`: Send 10 requests → validate responses in same order

**Acceptance Criteria**:
- All 6 test cases written
- Tests compile
- Tests FAIL (expected - no implementation yet)
- Uses FluentAssertions for assertions

---

### T005: [P] Contract test for IMessageDispatcher API
**Type**: Test - Contract
**Priority**: HIGH
**Dependencies**: T001
**Files**: `tests/PulseRPC.Server.Tests/Contract/DispatcherContractTests.cs`

**Description**:
Write contract tests for `IMessageDispatcher` interface. Based on `contracts/dispatcher-api.yaml`.

Test cases:
1. `StartAsync_ShouldBeIdempotent`: Call StartAsync twice → no errors
2. `StopAsync_ShouldWaitForInFlightRequests`: Start processing → stop → validate completion
3. `DispatchMessageAsync_ShouldRouteToCorrectService`: Dispatch message → validate service lookup called
4. `DispatchMessageAsync_ShouldPreserveFIFOPerConnection`: Dispatch 10 messages same connection → validate order
5. `RegisterServiceHandler_ShouldRejectDuplicate`: Register same service twice → ArgumentException
6. `MessageProcessed_ShouldFireExactlyOnce`: Dispatch message → validate event fired once

**Acceptance Criteria**:
- All 6 test cases written
- Uses NSubstitute for mocking IServiceHandler
- Tests compile and FAIL

---

### T006: [P] Contract test for IServiceHandler API
**Type**: Test - Contract
**Priority**: HIGH
**Dependencies**: T001
**Files**: `tests/PulseRPC.Server.Tests/Contract/ServiceHandlerContractTests.cs`

**Description**:
Write contract tests for `IServiceHandler` interface. Based on `contracts/service-handler.yaml`.

Test cases:
1. `InvokeAsync_ShouldDeserializeParameters`: Call method with serialized params → validate deserialization
2. `InvokeAsync_ShouldRespectCancellationToken`: Call with cancelled token → OperationCanceledException
3. `InvokeAsync_ShouldThrowMethodNotFoundException`: Call non-existent method → MethodNotFoundException
4. `InvokeAsync_ShouldPropagateServiceException`: Service throws → exception propagated
5. `GetMethodNames_ShouldReturnAllMethods`: Get methods → validate all public methods listed

**Acceptance Criteria**:
- All 5 test cases written
- Mock service handler created for testing
- Tests compile and FAIL

---

### T007: [P] Integration test - Normal request-response flow (Scenario 1)
**Type**: Test - Integration
**Priority**: HIGH
**Dependencies**: T001
**Files**: `tests/PulseRPC.Server.Tests/Integration/NormalRequestResponseTests.cs`

**Description**:
Implement Scenario 1 from `quickstart.md`: Normal request-response flow.

Steps:
1. Start server with TestService
2. Connect test client
3. Send Echo request
4. Validate response correctness
5. Validate latency < 5ms

**Acceptance Criteria**:
- Test setup and teardown implemented
- All validation steps from quickstart.md included
- Test compiles and FAILS (no implementation)

---

### T008: [P] Integration test - Concurrent multi-client load (Scenario 2)
**Type**: Test - Integration
**Priority**: HIGH
**Dependencies**: T001
**Files**: `tests/PulseRPC.Server.Tests/Integration/ConcurrentLoadTests.cs`

**Description**:
Implement Scenario 2 from `quickstart.md`: 5000 concurrent clients sending simultaneously.

Steps:
1. Start server
2. Connect 5000 clients
3. All send 10 requests each (50K total)
4. Validate all responses succeed
5. Validate FIFO ordering per client
6. Validate P99 latency < 10ms

**Acceptance Criteria**:
- Concurrent client simulation implemented
- Latency percentile calculation included
- Test compiles and FAILS

---

### T009: [P] Integration test - Service method throws exception (Scenario 3)
**Type**: Test - Integration
**Priority**: HIGH
**Dependencies**: T001
**Files**: `tests/PulseRPC.Server.Tests/Integration/ErrorRecoveryTests.cs`

**Description**:
Implement Scenario 3 from `quickstart.md`: Service method throws exception.

Steps:
1. Register FaultyService with ThrowException method
2. Call method
3. Validate error response with exception details
4. Validate server continues processing other requests

**Acceptance Criteria**:
- FaultyService test implementation created
- Exception details validation included
- Server health check after exception included

---

### T010: [P] Integration test - Slow service method (Scenario 4)
**Type**: Test - Integration
**Priority**: HIGH
**Dependencies**: T001
**Files**: `tests/PulseRPC.Server.Tests/Integration/TimeoutHandlingTests.cs`

**Description**:
Implement Scenario 4 from `quickstart.md`: Slow method with timeout.

Steps:
1. Register SlowService (5 second operation)
2. Configure 2 second timeout
3. Call slow method
4. Validate timeout error received
5. Validate other clients not blocked

**Acceptance Criteria**:
- SlowService test implementation
- Parallel client test to validate non-blocking
- Timeout error validation

---

### T011: [P] Integration test - Message parsing failure (Scenario 5)
**Type**: Test - Integration
**Priority**: HIGH
**Dependencies**: T001
**Files**: `tests/PulseRPC.Server.Tests/Integration/ProtocolErrorTests.cs`

**Description**:
Implement Scenario 5 from `quickstart.md`: Malformed message handling.

Steps:
1. Connect raw socket to server
2. Send garbage/corrupted data
3. Validate protocol error response
4. Validate server continues normal operation

**Acceptance Criteria**:
- Raw socket connection test
- Malformed data generation
- Protocol error validation

---

### T012: [P] Integration test - Connection loss during processing (Scenario 6)
**Type**: Test - Integration
**Priority**: HIGH
**Dependencies**: T001
**Files**: `tests/PulseRPC.Server.Tests/Integration/ConnectionLossTests.cs`

**Description**:
Implement Scenario 6 from `quickstart.md`: Connection drops mid-processing.

Steps:
1. Start long-running operation
2. Drop connection mid-processing
3. Validate operation cancelled
4. Validate resource cleanup
5. Validate new connections work

**Acceptance Criteria**:
- Long-running operation simulation
- Connection drop simulation
- Resource cleanup validation

---

### T013: [P] Integration test - Backpressure under extreme load (Scenario 7)
**Type**: Test - Integration
**Priority**: HIGH
**Dependencies**: T001
**Files**: `tests/PulseRPC.Server.Tests/Integration/BackpressureIntegrationTests.cs`

**Description**:
Implement Scenario 7 from `quickstart.md`: Backpressure mechanisms.

Steps:
1. Configure server with limited capacity
2. Flood with 10x capacity requests
3. Validate backpressure activated
4. Validate some connections rejected
5. Validate server didn't crash/OOM
6. Validate recovery after load subsides

**Acceptance Criteria**:
- High-volume request generation
- Backpressure metrics validation
- Recovery validation

---

### T014: [P] Performance benchmark - Throughput (FR-032)
**Type**: Test - Performance
**Priority**: HIGH
**Dependencies**: T001, T002
**Files**: `tests/PulseRPC.Server.Tests/Performance/ThroughputBenchmarks.cs`

**Description**:
Implement throughput benchmark from `quickstart.md` performance validation.

Setup:
- 5000 concurrent clients
- Small payloads (256 bytes)
- 60 second duration

Pass criteria: ≥ 100,000 req/s sustained

**Acceptance Criteria**:
- BenchmarkDotNet benchmark class
- Uses [Benchmark] attribute
- Validates >= 100K req/s threshold
- Test compiles (will fail until implementation)

---

### T015: [P] Performance benchmark - Latency (FR-033, FR-034)
**Type**: Test - Performance
**Priority**: HIGH
**Dependencies**: T001, T002
**Files**: `tests/PulseRPC.Server.Tests/Performance/LatencyBenchmarks.cs`

**Description**:
Implement latency benchmark from `quickstart.md`.

Setup:
- 2500 clients (50% capacity)
- 1KB payloads
- 60 second duration

Pass criteria: P95 ≤ 5ms AND P99 ≤ 10ms

**Acceptance Criteria**:
- HdrHistogram for percentile calculation
- Validates P95 and P99 targets
- Test compiles (will fail until implementation)

---

### T016: [P] Performance benchmark - Scalability (FR-035)
**Type**: Test - Performance
**Priority**: HIGH
**Dependencies**: T001, T002
**Files**: `tests/PulseRPC.Server.Tests/Performance/ScalabilityBenchmarks.cs`

**Description**:
Implement scalability benchmark from `quickstart.md`.

Setup:
- Ramp from 0 to 10,000 connections over 5 minutes
- Measure latency at 1K, 5K, 10K connection points

Pass criteria: All connections accepted, latency stable

**Acceptance Criteria**:
- Gradual ramp-up logic
- Latency measurement at checkpoints
- Memory usage tracking
- Test compiles (will fail until implementation)

---

## Phase 3.3: Core Implementation (ONLY after tests are failing)

**All entity/model tasks below can run in parallel [P] as they modify different files**

### T017: [P] Create RpcMessage entity
**Type**: Core - Model
**Priority**: HIGH
**Dependencies**: Tests T004-T016 written and failing
**Files**: `src/PulseRPC.Server/Models/RpcMessage.cs`

**Description**:
Create `RpcMessage` struct/class based on data-model.md Message entity.

Fields:
- ProtocolVersion (byte)
- MessageType (enum)
- RequestId (Guid)
- ServiceName (string)
- MethodName (string)
- Payload (ReadOnlyMemory<byte>)
- Metadata (IReadOnlyDictionary<string, string>)
- ReceivedAt (long, Stopwatch ticks)

Validation:
- ServiceName non-empty
- Payload ≤ 10MB
- ProtocolVersion matches server

**Acceptance Criteria**:
- All fields implemented
- Validation method added
- XML documentation
- Nullable reference types enabled
- Contract test T004 progresses (some assertions may pass)

---

### T018: [P] Create ServerConnection entity
**Type**: Core - Model
**Priority**: HIGH
**Dependencies**: Tests T004-T016 written and failing
**Files**: `src/PulseRPC.Server/Models/ServerConnection.cs`

**Description**:
Create `ServerConnection` class based on data-model.md Connection entity.

Fields from data-model.md, state transitions (Connecting → Active → Closing → Closed), statistics tracking.

**Acceptance Criteria**:
- All fields and properties
- State transition validation
- Thread-safe statistics updates (Interlocked)
- XML documentation

---

### T019: [P] Create ServiceRegistration entity
**Type**: Core - Model
**Priority**: HIGH
**Dependencies**: Tests T004-T016 written and failing
**Files**: `src/PulseRPC.Server/Models/ServiceRegistration.cs`

**Description**:
Create `ServiceRegistration` class based on data-model.md.

Includes compiled method invokers dictionary, timeout policy, priority, state management.

**Acceptance Criteria**:
- All fields from data-model.md
- State transition validation
- CompiledMethodInvoker type defined (stub for now, implemented in T025)
- XML documentation

---

### T020: [P] Create RequestContext entity
**Type**: Core - Model
**Priority**: HIGH
**Dependencies**: Tests T004-T016 written and failing
**Files**: `src/PulseRPC.Server/Models/RequestContext.cs`

**Description**:
Create `RequestContext` struct/class based on data-model.md.

Immutable after creation, includes RequestId, ClientId, ConnectionId, Metadata (readonly), CancellationToken, StartTimestamp, TraceContext.

**Acceptance Criteria**:
- Immutable design (init-only setters or readonly fields)
- ActivityContext integration for distributed tracing
- IDisposable implementation
- XML documentation

---

### T021: [P] Create ResponseEnvelope entity
**Type**: Core - Model
**Priority**: HIGH
**Dependencies**: Tests T004-T016 written and failing
**Files**: `src/PulseRPC.Server/Models/ResponseEnvelope.cs`

**Description**:
Create `ResponseEnvelope` class based on data-model.md.

Includes IsSuccess, Payload XOR ExceptionDetails, CompletedAt, DurationMs. Nested ExceptionData type for structured exception serialization.

**Acceptance Criteria**:
- XOR validation (Payload OR ExceptionDetails, not both)
- ExceptionData nested type with recursive InnerException
- XML documentation

---

### T022: [P] Create MessageType and supporting enums
**Type**: Core - Model
**Priority**: HIGH
**Dependencies**: Tests T004-T016 written and failing
**Files**: `src/PulseRPC.Server/Models/Enums.cs`

**Description**:
Create all enums from data-model.md:
- MessageType (Request, Response, Error, Ping, Pong)
- ConnectionState (Connecting, Active, Closing, Closed)
- ServiceState (Registered, Active, Paused, Unregistered)
- MessagePriority (Critical, High, Normal, Low)
- TransportType (TCP, KCP)

**Acceptance Criteria**:
- All 5 enums defined
- XML documentation for each value
- Byte underlying type where specified

---

### T023: Implement CompiledServiceInvoker
**Type**: Core - Implementation
**Priority**: HIGH
**Dependencies**: T019 (ServiceRegistration), research.md decision #2
**Files**: `src/PulseRPC.Server/Dispatch/CompiledServiceInvoker.cs`

**Description**:
Implement compiled delegate invocation using Expression Trees (research.md decision #2: 10,000x speedup vs reflection).

Key methods:
- `CompileMethod(MethodInfo)`: Build expression tree, compile to delegate
- `InvokeAsync(object[] parameters, RequestContext context)`: Call compiled delegate

**Acceptance Criteria**:
- Expression tree compilation at registration time
- Delegate invocation at runtime (zero reflection in hot path)
- Support for both sync and async methods
- CancellationToken wiring for timeout
- Contract test T006 passes partially

---

### T024: Enhance HighPerformanceMessageEngine - Reception integration
**Type**: Core - Enhancement
**Priority**: HIGH
**Dependencies**: T017 (RpcMessage), T020 (RequestContext)
**Files**: `src/PulseRPC.Server/Engine/HighPerformanceMessageEngine.cs`

**Description**:
Enhance existing `HighPerformanceMessageEngine` to integrate message reception from TieredMessageProcessor into RpcMessage parsing.

Changes:
- Parse incoming bytes → RpcMessage
- Validate protocol version, message size
- Create RequestContext from Message + Connection
- Pass to dispatcher

**Acceptance Criteria**:
- Message parsing < 100 microseconds (FR-036)
- Protocol validation implemented
- Integration with existing TieredMessageProcessor maintained
- No performance regression
- Contract test T004 progresses (reception stage passes)

---

### T025: Enhance HighPerformanceMessageDispatcher - Service routing
**Type**: Core - Enhancement
**Priority**: HIGH
**Dependencies**: T023 (CompiledServiceInvoker), T019 (ServiceRegistration)
**Files**: `src/PulseRPC.Server/Dispatch/HighPerformanceMessageDispatcher.cs`

**Description**:
Enhance existing `HighPerformanceMessageDispatcher` to route messages to registered services using compiled invokers.

Changes:
- Service registry (Dictionary<string, ServiceRegistration>)
- Lookup by ServiceName (O(1))
- Invoke via CompiledServiceInvoker
- Handle service not found error

**Acceptance Criteria**:
- Service registration API added
- Dictionary lookup ~10 microseconds (research.md)
- FIFO ordering maintained per connection (FR-008)
- Contract test T005 passes mostly
- Contract test T004 progresses (dispatching stage passes)

---

### T026: Implement ResponseProcessor
**Type**: Core - New Component
**Priority**: HIGH
**Dependencies**: T021 (ResponseEnvelope), T025 (service invocation)
**Files**: `src/PulseRPC.Server/Response/ResponseProcessor.cs`

**Description**:
Create `ResponseProcessor` to generate ResponseEnvelope from service method results or exceptions.

Key methods:
- `CreateSuccessResponse(Guid requestId, object result)`: Serialize result → ResponseEnvelope
- `CreateErrorResponse(Guid requestId, Exception exception)`: Serialize exception → ResponseEnvelope

**Acceptance Criteria**:
- Success response generation
- Error response generation with ExceptionData
- MemoryPack serialization for payload
- Exception sanitization (no sensitive paths in stack trace)
- Contract test T004 progresses (response generation stage passes)

---

### T027: Implement ErrorResponseFactory
**Type**: Core - New Component
**Priority**: HIGH
**Dependencies**: T021 (ResponseEnvelope), research.md decision #4
**Files**: `src/PulseRPC.Server/ErrorHandling/ErrorResponseFactory.cs`

**Description**:
Create `ErrorResponseFactory` for structured error responses (research.md decision #4: exception boundary).

Error types:
- ProtocolError (version mismatch, parse failure)
- ServiceNotFoundError
- TimeoutError
- SerializationError
- MethodNotFoundError

**Acceptance Criteria**:
- Factory methods for each error type
- Structured ExceptionData creation
- Stack trace sanitization
- Integration test T009 progresses

---

### T028: Implement ExceptionSerializer
**Type**: Core - New Component
**Priority**: HIGH
**Dependencies**: T027 (ErrorResponseFactory)
**Files**: `src/PulseRPC.Server/ErrorHandling/ExceptionSerializer.cs`

**Description**:
Create `ExceptionSerializer` for safe exception serialization.

Handles:
- Recursive InnerException serialization
- Stack trace sanitization (remove sensitive paths)
- Type name extraction
- Message extraction

**Acceptance Criteria**:
- Recursive serialization working
- No sensitive information leaked
- Integration test T009 passes

---

### T029: Implement FaultIsolationPolicy
**Type**: Core - New Component
**Priority**: HIGH
**Dependencies**: T027 (ErrorResponseFactory), research.md decision #4
**Files**: `src/PulseRPC.Server/ErrorHandling/FaultIsolationPolicy.cs`

**Description**:
Implement fault isolation policy (research.md decision #4: exception boundary at service invocation).

Try-catch wrapper around service method invocation:
```csharp
try {
    result = await serviceMethod();
    return SuccessResponse(result);
} catch (Exception ex) {
    Log(ex);
    return ErrorResponse(ex);
}
```

**Acceptance Criteria**:
- Exception caught without crashing server
- Logging with full context
- Other requests continue processing
- Integration test T009 passes fully

---

### T030: Implement ResponseSerializer
**Type**: Core - New Component
**Priority**: HIGH
**Dependencies**: T026 (ResponseProcessor)
**Files**: `src/PulseRPC.Server/Response/ResponseSerializer.cs`

**Description**:
Create `ResponseSerializer` to serialize ResponseEnvelope to wire format.

Uses MemoryPack for high-performance serialization.

**Acceptance Criteria**:
- Serialization using MemoryPack
- Compression for large responses (> threshold)
- ArrayPool usage for buffers
- Serialization failure handling (return error response)

---

### T031: Implement ResponseTransmitter
**Type**: Core - New Component
**Priority**: HIGH
**Dependencies**: T030 (ResponseSerializer), research.md decision #3
**Files**: `src/PulseRPC.Server/Response/ResponseTransmitter.cs`

**Description**:
Create `ResponseTransmitter` for batched network writes (research.md decision #3: Channels + batching).

Architecture:
- Channel per connection (or shared channel with routing)
- Dedicated I/O thread pool for socket writes
- Batching for small responses (< 1KB)

**Acceptance Criteria**:
- System.Threading.Channels integration
- Batching reduces syscalls (3x-5x improvement measured)
- Connection loss detection
- Retry logic for transient failures
- Contract test T004 progresses (transmission stage passes)
- Integration test T012 progresses (connection loss handling)

---

### T032: Implement MessagePipeline orchestrator
**Type**: Core - New Component
**Priority**: HIGH
**Dependencies**: T024, T025, T026, T031 (all pipeline stages)
**Files**: `src/PulseRPC.Server/Pipeline/MessagePipeline.cs`

**Description**:
Create `MessagePipeline` as end-to-end orchestrator coordinating all 5 stages:
1. Reception (MessageEngine)
2. Dispatching (Dispatcher)
3. Invocation (CompiledInvoker)
4. Response Generation (ResponseProcessor)
5. Transmission (ResponseTransmitter)

**Acceptance Criteria**:
- All stages coordinated
- Error handling at each stage
- Metrics collection per stage
- Contract test T004 passes fully (end-to-end flow working)

---

### T033: Implement TimeoutPolicy
**Type**: Core - New Component
**Priority**: HIGH
**Dependencies**: T020 (RequestContext), T025 (dispatcher)
**Files**: `src/PulseRPC.Server/Configuration/TimeoutPolicy.cs` (enhance from T003)

**Description**:
Enhance TimeoutPolicy stub from T003 with actual timeout enforcement logic.

Features:
- Per-service timeout configuration
- Per-method timeout override
- CancellationToken creation with timeout
- Timeout error response generation

**Acceptance Criteria**:
- CancellationTokenSource with timeout
- Wired to RequestContext.CancellationToken
- OperationCanceledException → TimeoutError response
- Integration test T010 passes

---

### T034: Implement BackpressurePolicy
**Type**: Core - New Component
**Priority**: HIGH
**Dependencies**: T003 (stub), research.md decision #6
**Files**: `src/PulseRPC.Server/Configuration/BackpressurePolicy.cs` (enhance from T003)

**Description**:
Enhance BackpressurePolicy stub with multi-level backpressure (research.md decision #6).

Three levels:
1. Queue depth monitoring (warning at 80%)
2. Connection throttling (at 90% for 5+ seconds)
3. Connection rejection (at 100% for 10+ seconds)

**Acceptance Criteria**:
- Three-level state machine
- Hysteresis to prevent flapping
- Metrics for backpressure activation
- Integration test T013 passes

---

## Phase 3.4: Observability & Diagnostics

### T035: [P] Implement PipelineMetricsCollector
**Type**: Integration - Observability
**Priority**: MEDIUM
**Dependencies**: T032 (MessagePipeline)
**Files**: `src/PulseRPC.Server/Observability/PipelineMetricsCollector.cs`

**Description**:
Implement metrics collection (research.md decision #5: custom metrics).

Metrics to collect (from spec FR-051, FR-052):
- Requests/second (gauge)
- Error rate (counter)
- Latency percentiles (histogram): P50, P75, P95, P99
- Active connections (gauge)
- Queue depths (gauge): L1, L2, L3
- CPU usage (gauge)
- Memory usage (gauge)

**Acceptance Criteria**:
- All metrics implemented
- Minimal hot path overhead (< 1% impact)
- Prometheus-compatible export format
- Performance benchmarks T014-T016 can measure metrics

---

### T036: [P] Implement DistributedTracingIntegration
**Type**: Integration - Observability
**Priority**: MEDIUM
**Dependencies**: T020 (RequestContext with ActivityContext)
**Files**: `src/PulseRPC.Server/Observability/DistributedTracingIntegration.cs`

**Description**:
Implement Activity-based distributed tracing (research.md decision #5).

Uses System.Diagnostics.Activity for W3C Trace Context propagation.

**Acceptance Criteria**:
- Activity created per request
- TraceContext propagated through pipeline
- Activity tags for service name, method name, RequestId
- Zero allocations in fast path
- Integration tests can validate trace context

---

### T037: [P] Implement DiagnosticEndpoints
**Type**: Integration - Observability
**Priority**: LOW
**Dependencies**: T035 (metrics), T036 (tracing)
**Files**: `src/PulseRPC.Server/Observability/DiagnosticEndpoints.cs`

**Description**:
Implement diagnostic HTTP endpoints (from spec FR-058):
- `/diagnostics/health`: Server health check
- `/diagnostics/metrics`: Prometheus metrics
- `/diagnostics/connections`: Active connection list
- `/diagnostics/queue-stats`: Queue depth and saturation
- `/diagnostics/thread-dump`: Thread pool state (debug only)

**Acceptance Criteria**:
- 5 endpoints implemented
- JSON responses
- Security considerations (optional auth)
- Can be called during integration tests

---

## Phase 3.5: Integration & Wiring

### T038: Wire MessagePipeline into PulseServer
**Type**: Integration
**Priority**: HIGH
**Dependencies**: T032 (MessagePipeline)
**Files**: `src/PulseRPC.Server/PulseServer.cs`

**Description**:
Integrate MessagePipeline into main PulseServer startup/shutdown flow.

Changes:
- Create MessagePipeline instance
- Wire to transport listeners (TCP/KCP)
- Start pipeline on server start
- Stop pipeline on server shutdown

**Acceptance Criteria**:
- Pipeline started/stopped correctly
- Integration tests T007-T013 can run end-to-end
- No lifecycle issues (resources released)

---

### T039: Implement connection state management
**Type**: Integration
**Priority**: HIGH
**Dependencies**: T018 (ServerConnection), T038 (wiring)
**Files**: `src/PulseRPC.Server/Channels/ServerChannelManager.cs` (enhance existing)

**Description**:
Enhance existing ServerChannelManager to track ServerConnection entities.

State transitions:
- New connection → Connecting → Active
- Graceful shutdown → Closing → Closed
- Connection loss → Closed

**Acceptance Criteria**:
- Connection state tracking
- Statistics updates (messages sent/received)
- Integration test T012 passes (connection loss cleanup)

---

### T040: Implement request context factory
**Type**: Integration
**Priority**: HIGH
**Dependencies**: T020 (RequestContext), T036 (tracing)
**Files**: `src/PulseRPC.Server/Dispatch/RequestContextFactory.cs`

**Description**:
Create factory for RequestContext creation from Message + Connection + Activity.

**Acceptance Criteria**:
- Creates immutable RequestContext
- Wires CancellationToken from TimeoutPolicy
- Extracts ActivityContext for tracing
- Object pooling for reduced allocations

---

### T041: Add comprehensive logging
**Type**: Integration
**Priority**: MEDIUM
**Dependencies**: All core components
**Files**: Multiple files (add logging to all components)

**Description**:
Add structured logging throughout pipeline (spec FR-053).

Log at:
- Error level: All exceptions with full context
- Warning level: Backpressure activation, timeout warnings
- Info level: Service registration, pipeline start/stop
- Debug level: Message routing decisions

**Acceptance Criteria**:
- ILogger<T> injection in all components
- Structured logging (JSON-compatible)
- Log context includes RequestId, ConnectionId, ServiceName
- No PII in logs

---

## Phase 3.6: Polish & Validation

### T042: [P] Unit tests for ResponseProcessor
**Type**: Test - Unit
**Priority**: MEDIUM
**Dependencies**: T026 (ResponseProcessor implemented)
**Files**: `tests/PulseRPC.Server.Tests/Unit/Response/ResponseProcessorTests.cs`

**Description**:
Write unit tests for ResponseProcessor:
1. Success response serialization
2. Error response serialization
3. Serialization failure handling
4. Compression threshold logic
5. RequestId preservation

**Acceptance Criteria**:
- 10+ test cases
- >90% code coverage for ResponseProcessor
- All tests pass

---

### T043: [P] Unit tests for ErrorResponseFactory
**Type**: Test - Unit
**Priority**: MEDIUM
**Dependencies**: T027 (ErrorResponseFactory implemented)
**Files**: `tests/PulseRPC.Server.Tests/Unit/ErrorHandling/ErrorResponseTests.cs`

**Description**:
Write unit tests for ErrorResponseFactory:
1. Each error type generation
2. ExceptionData structure validation
3. Stack trace sanitization
4. Recursive InnerException serialization

**Acceptance Criteria**:
- 8+ test cases
- >90% code coverage
- All tests pass

---

### T044: [P] Unit tests for FaultIsolationPolicy
**Type**: Test - Unit
**Priority**: MEDIUM
**Dependencies**: T029 (FaultIsolationPolicy implemented)
**Files**: `tests/PulseRPC.Server.Tests/Unit/ErrorHandling/FaultIsolationTests.cs`

**Description**:
Write unit tests for FaultIsolationPolicy:
1. Exception caught without crash
2. Logging with context
3. Other requests continue
4. Various exception types handled

**Acceptance Criteria**:
- 6+ test cases
- >90% code coverage
- All tests pass

---

### T045: [P] Unit tests for CompiledServiceInvoker
**Type**: Test - Unit
**Priority**: MEDIUM
**Dependencies**: T023 (CompiledServiceInvoker implemented)
**Files**: `tests/PulseRPC.Server.Tests/Unit/Dispatch/ServiceInvokerTests.cs`

**Description**:
Write unit tests for CompiledServiceInvoker:
1. Method compilation success
2. Invocation correctness
3. Parameter passing
4. Return value capture
5. CancellationToken wiring

**Acceptance Criteria**:
- 8+ test cases
- >90% code coverage
- All tests pass

---

### T046: [P] Unit tests for ResponseTransmitter
**Type**: Test - Unit
**Priority**: MEDIUM
**Dependencies**: T031 (ResponseTransmitter implemented)
**Files**: `tests/PulseRPC.Server.Tests/Unit/Response/ResponseTransmitterTests.cs`

**Description**:
Write unit tests for ResponseTransmitter:
1. Batching logic
2. Channel coordination
3. Connection loss detection
4. Retry logic

**Acceptance Criteria**:
- 8+ test cases
- >90% code coverage
- All tests pass

---

### T047: [P] Unit tests for TimeoutPolicy
**Type**: Test - Unit
**Priority**: MEDIUM
**Dependencies**: T033 (TimeoutPolicy implemented)
**Files**: `tests/PulseRPC.Server.Tests/Unit/Configuration/TimeoutPolicyTests.cs`

**Description**:
Write unit tests for TimeoutPolicy:
1. Timeout enforcement
2. CancellationToken creation
3. Per-service timeout
4. Per-method override

**Acceptance Criteria**:
- 6+ test cases
- >90% code coverage
- All tests pass

---

### T048: [P] Unit tests for BackpressurePolicy
**Type**: Test - Unit
**Priority**: MEDIUM
**Dependencies**: T034 (BackpressurePolicy implemented)
**Files**: `tests/PulseRPC.Server.Tests/Unit/Configuration/BackpressurePolicyTests.cs`

**Description**:
Write unit tests for BackpressurePolicy:
1. Level 1 activation (80% threshold)
2. Level 2 activation (90% threshold)
3. Level 3 activation (100% threshold)
4. Hysteresis behavior
5. Recovery on load decrease

**Acceptance Criteria**:
- 8+ test cases
- >90% code coverage
- All tests pass

---

### T049: Run all integration tests and fix failures
**Type**: Validation
**Priority**: HIGH
**Dependencies**: All implementation tasks (T017-T041)
**Files**: N/A (validation task)

**Description**:
Execute all integration tests (T007-T013) and fix any remaining failures.

Scenarios:
1. Normal request-response (T007)
2. Concurrent load (T008)
3. Exception handling (T009)
4. Timeout handling (T010)
5. Protocol errors (T011)
6. Connection loss (T012)
7. Backpressure (T013)

**Acceptance Criteria**:
- All 7 integration tests pass
- No flaky tests
- All edge cases covered

---

### T050: Run all performance benchmarks and validate targets
**Type**: Validation
**Priority**: HIGH
**Dependencies**: All implementation tasks, T014-T016 (benchmarks)
**Files**: N/A (validation task)

**Description**:
Execute all performance benchmarks and validate against targets.

Benchmarks:
- Throughput (T014): >= 100,000 req/s
- Latency (T015): P95 < 5ms, P99 < 10ms
- Scalability (T016): 10,000 connections

**Acceptance Criteria**:
- All performance targets met
- No performance regressions vs baseline
- Benchmark results documented

---

### T051: Fix performance regressions if any
**Type**: Optimization
**Priority**: HIGH
**Dependencies**: T050 (benchmark results)
**Files**: Various (based on profiling)

**Description**:
If performance targets not met, profile and optimize hot paths.

Common optimization areas:
- Reduce allocations (object pooling)
- Optimize dictionary lookups
- Reduce lock contention
- Batch operations

**Acceptance Criteria**:
- All performance targets met after optimization
- No correctness regressions
- Optimizations documented

---

### T052: [P] Update XML documentation for all public APIs
**Type**: Documentation
**Priority**: MEDIUM
**Dependencies**: All implementation tasks
**Files**: All public classes/interfaces in src/PulseRPC.Server/

**Description**:
Ensure all public APIs have complete XML documentation.

Include:
- Class/interface summary
- Method descriptions
- Parameter descriptions
- Return value descriptions
- Exception conditions
- Usage examples where helpful

**Acceptance Criteria**:
- All public types documented
- All public methods documented
- No CS1591 warnings (missing XML docs)
- Documentation builds without errors

---

### T053: [P] Update CHANGELOG.md with new features
**Type**: Documentation
**Priority**: LOW
**Dependencies**: Feature complete
**Files**: `CHANGELOG.md` (repository root)

**Description**:
Add entry to CHANGELOG.md documenting the complete message dispatch-process-response pipeline.

Include:
- Feature summary
- Performance improvements
- Breaking changes (if any)
- Migration guide (if needed)

**Acceptance Criteria**:
- CHANGELOG entry added
- Follows existing format
- Lists all major new components

---

### T054: Run 72-hour stress test
**Type**: Validation
**Priority**: HIGH
**Dependencies**: All implementation and optimization complete
**Files**: N/A (validation task)

**Description**:
Execute 72-hour stress test to validate production readiness (spec success criteria).

Test conditions:
- Sustained 50,000 req/s load
- 5,000 concurrent connections
- Monitor for memory leaks, performance degradation

**Acceptance Criteria**:
- No crashes or hangs
- No memory leaks detected
- Performance stable throughout 72 hours
- Error rate < 0.5%

---

### T055: Final code review and cleanup
**Type**: Polish
**Priority**: MEDIUM
**Dependencies**: All implementation, documentation, validation complete
**Files**: All source files

**Description**:
Final code review pass:
- Remove dead code
- Remove debug statements
- Verify consistent code style
- Check for TODOs/FIXMEs
- Verify no sensitive information in logs

**Acceptance Criteria**:
- No TODO/FIXME comments remain
- Code style consistent
- No dead code
- Ready for production deployment

---

## Dependencies Summary

### Critical Path
```
Setup (T001-T003)
  ↓
Tests (T004-T016) - ALL PARALLEL
  ↓
Models (T017-T022) - ALL PARALLEL
  ↓
Core Components (T023-T034) - Mostly sequential with some parallelism
  ↓
Observability (T035-T037) - ALL PARALLEL
  ↓
Integration (T038-T041) - Sequential
  ↓
Unit Tests (T042-T048) - ALL PARALLEL
  ↓
Validation (T049-T051) - Sequential
  ↓
Polish (T052-T055) - Mostly parallel
```

### Parallel Execution Opportunities

**Phase 1: Setup (parallel)**
- T001, T002, T003 can all run together

**Phase 2: Tests (all parallel)**
- T004-T016 (13 tests) can all run in parallel

**Phase 3: Models (all parallel)**
- T017-T022 (6 models) can all run in parallel

**Phase 4: Core Components (partial parallelism)**
- T023 blocks T025
- T024, T026, T027, T030 can run in parallel after their dependencies
- T031 depends on T030
- T032 depends on all previous
- T033, T034 can run in parallel with earlier tasks

**Phase 5: Observability (all parallel)**
- T035, T036, T037 can all run in parallel

**Phase 6: Integration (sequential)**
- T038-T041 must run sequentially

**Phase 7: Unit Tests (all parallel)**
- T042-T048 (7 test files) can all run in parallel

**Phase 8: Polish (partial parallelism)**
- T052, T053 can run in parallel
- T049, T050, T051, T054, T055 must be sequential

---

## Validation Checklist

*GATE: All items must be checked before feature is complete*

- [x] All 3 contracts have corresponding tests (T004, T005, T006)
- [x] All 5 entities have model tasks (T017-T021)
- [x] All 7 integration scenarios have tests (T007-T013)
- [x] All 3 performance benchmarks have tests (T014-T016)
- [x] All tests come before implementation (Phase 3.2 before 3.3)
- [x] Parallel tasks [P] are truly independent (different files)
- [x] Each task specifies exact file path
- [x] No task modifies same file as another [P] task
- [x] TDD workflow enforced (tests fail, implement, tests pass)

---

## Execution Strategy

### Recommended Execution Order

1. **Sprint 1 (Setup + Tests)**: T001-T016 (16 tasks, ~2-3 days)
   - Complete all setup and write all tests
   - Tests will fail - this is expected and correct

2. **Sprint 2 (Models + Core)**: T017-T034 (18 tasks, ~5-7 days)
   - Implement all entities and core components
   - Tests should start passing incrementally

3. **Sprint 3 (Observability + Integration)**: T035-T041 (7 tasks, ~2-3 days)
   - Add observability and wire everything together
   - Integration tests should all pass

4. **Sprint 4 (Polish + Validation)**: T042-T055 (14 tasks, ~3-4 days)
   - Unit tests, documentation, performance validation
   - 72-hour stress test can run in parallel with other tasks

**Total Estimated Timeline**: 12-17 days (with parallel execution)

### High-Value Parallel Execution Batches

**Batch 1 - All Tests** (can launch together):
```
T004, T005, T006, T007, T008, T009, T010, T011, T012, T013, T014, T015, T016
```

**Batch 2 - All Models** (can launch together):
```
T017, T018, T019, T020, T021, T022
```

**Batch 3 - All Unit Tests** (can launch together):
```
T042, T043, T044, T045, T046, T047, T048
```

---

**Tasks Generated**: 55 tasks across 6 phases
**Estimated LOC**: ~20,000 lines (plan.md estimate)
**Estimated Duration**: 12-17 days with parallel execution
**Ready for**: Immediate execution via TDD workflow
