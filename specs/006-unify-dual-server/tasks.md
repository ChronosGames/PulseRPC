# Implementation Tasks: Unified Server Implementation

**Feature Branch**: `006-unify-dual-server`
**Date**: 2025-10-13
**Total Tasks**: 28
**Estimated Effort**: 10-15 days

---

## Task Organization by User Story

This implementation is organized to deliver value incrementally:

- **Phase 1**: Setup (shared infrastructure)
- **Phase 2**: Foundational (blocking prerequisites - core architecture)
- **Phase 3**: User Story 1 (P1) - Clear API Surface
- **Phase 4**: User Story 2 (P1) - Consistent Behavior
- **Phase 5**: User Story 3 (P2) - Simplified Maintenance
- **Phase 6**: User Story 4 (P2) - Migration Path
- **Phase 7**: Polish & Integration

**[P]** = Parallelizable (different files, can be implemented concurrently)

---

## Phase 1: Setup (Shared Infrastructure)

**Objective**: Prepare project structure and shared components needed by all user stories

### T001: Create configuration classes [P]
**File**: `src/PulseRPC.Server/Configuration/ServerConfiguration.cs`
**Story**: Setup
**Description**: Create unified `ServerConfiguration` class that merges `ServerOptions` (PulseServer) and `ServerHostOptions` (ServerHost)
**Acceptance Criteria**:
- Class contains all transport configurations (List<TransportChannelConfiguration>)
- Includes pipeline options (MessageReceiverOptions, MessageDispatcherOptions, ResponseTransmitterOptions)
- Includes connection/service/backpressure options
- Has `ShutdownTimeout` property (TimeSpan, default 30s)
- Properly documented with XML comments

### T002: Create shutdown options class [P]
**File**: `src/PulseRPC.Server/Configuration/ShutdownOptions.cs`
**Story**: Setup
**Description**: Create `ShutdownOptions` class for graceful shutdown configuration
**Acceptance Criteria**:
- `Timeout` property (TimeSpan, default 30s)
- `DrainTimeout` property (TimeSpan, default 20s)
- `ConnectionCloseTimeout` property (TimeSpan, default 5s)
- `TransportStopTimeout` property (TimeSpan, default 5s)
- `ForceShutdownOnTimeout` property (bool, default true)
- Properly documented with XML comments

---

## Phase 2: Foundational (Blocking Prerequisites)

**Objective**: Implement core architecture components that ALL user stories depend on

**Checkpoint**: These tasks MUST complete before any user story implementation begins

### T003: Implement ServerLifecycleCoordinator
**File**: `src/PulseRPC.Server/Core/ServerLifecycleCoordinator.cs`
**Story**: Foundational
**Description**: Implement server state machine coordinator (Stopped → Starting → Running → Stopping → Stopped)
**Dependencies**: T001, T002
**Acceptance Criteria**:
- Implements state machine with 4 states (Stopped, Starting, Running, Stopping)
- Thread-safe state transitions using `Lock` (C# 13)
- `StateChanged` event (EventHandler<ServerStateChangedEventArgs>)
- Methods: `TransitionToStartingAsync()`, `TransitionToRunningAsync()`, `TransitionToStoppingAsync()`, `TransitionToStoppedAsync()`
- `CurrentState` property (ServerState enum)
- Validates illegal transitions (e.g., Starting → Stopping)
- Logs state changes

### T004: Implement TransportOrchestrator
**File**: `src/PulseRPC.Server/Core/TransportOrchestrator.cs`
**Story**: Foundational
**Description**: Implement multi-transport lifecycle coordinator (manages TCP/KCP listeners)
**Dependencies**: T001
**Acceptance Criteria**:
- Uses `ITransportIntegrationManager` to create listeners
- Stores listeners in `ConcurrentDictionary<string, IServerListener>`
- `AddTransport(TransportChannelConfiguration)` method
- `StartAllTransportsAsync(CancellationToken)` - parallel startup using Task.WhenAll
- `StopAcceptingAsync(CancellationToken)` - stop accepting new connections (Phase 1 shutdown)
- `StopAllTransportsAsync(CancellationToken)` - close all listeners (Phase 2 shutdown)
- `GetTransports()` returns IReadOnlyDictionary<string, TransportInfo>
- `GetDefaultTransport()` returns first transport marked as default
- Wires `ConnectionAccepted` events to IServerChannelManager
- Handles listener failures during startup (cleanup started listeners)

### T005: Implement PipelineCoordinator
**File**: `src/PulseRPC.Server/Pipeline/PipelineCoordinator.cs`
**Story**: Foundational
**Description**: Implement pipeline component coordinator (integrates MessageReceiver/Dispatcher/Transmitter)
**Dependencies**: T001
**Acceptance Criteria**:
- Owns `MessageReceiver`, `MessageDispatcher`, `ResponseTransmitter` instances
- Owns `ServiceRegistry`, `BackpressurePolicy`, `ConnectionManager` instances
- `StartPipelineAsync(CancellationToken)` - starts all components sequentially
- `DrainPipelinesAsync(CancellationToken)` - waits for in-flight messages to complete (graceful shutdown)
- `StopPipelineAsync(CancellationToken)` - stops all components in reverse order
- Wires up events: MessageReceiver.MessageReceived → backpressure check → MessageDispatcher
- Wires up events: MessageDispatcher.InvocationCompleted → ResponseTransmitter
- `RegisterService<T>(name, instance, options)` - delegates to ServiceRegistry + MessageDispatcher
- `UnregisterService(name)` - delegates to ServiceRegistry + MessageDispatcher
- `GetHealthStatus()` returns ServerHealthStatus (aggregates from all components)
- Properties: `ConnectionManager`, `ServiceRegistry`, `BackpressurePolicy` (expose for advanced scenarios)

---

## Phase 3: User Story 1 (P1) - Clear API Surface

**Goal**: Single, clear entry point for creating servers (eliminate "which class to use" confusion)

**Independent Test Criteria**:
- Can examine public API surface - exactly one public server class (PulseServer)
- IntelliSense shows one primary server class
- Documentation references only one server class
- New developer can create working server in <5 minutes

### T006: Rewrite PulseServer with unified implementation
**File**: `src/PulseRPC.Server/PulseServer.cs`
**Story**: US1 - Clear API Surface
**Description**: Completely rewrite PulseServer.cs as unified implementation (delete existing, start fresh)
**Dependencies**: T003, T004, T005
**Acceptance Criteria**:
- Implements `IPulseServer` interface completely
- Constructor takes: `ILoggerFactory`, `IOptions<ServerConfiguration>`, `IServerChannelManager`, `ITransportIntegrationManager`, `PipelineCoordinator`
- Owns instances: `ServerLifecycleCoordinator`, `TransportOrchestrator`, `PipelineCoordinator`
- Lifecycle methods: `StartAsync(ct)` delegates to coordinators in order (Lifecycle → Transport → Pipeline)
- Lifecycle methods: `StopAsync(ct)` implements graceful shutdown with timeout (30s default)
  - Phase 1: TransportOrchestrator.StopAcceptingAsync()
  - Phase 2: PipelineCoordinator.DrainPipelinesAsync() (with timeout)
  - Phase 3: IServerChannelManager.CloseAllChannelsAsync()
  - Phase 4: TransportOrchestrator.StopAllTransportsAsync()
  - Catch OperationCanceledException on timeout → log warning → ForceShutdownAsync()
- Transport management: `AddTransport(config)` delegates to TransportOrchestrator
- Transport queries: `GetTransports()`, `GetDefaultTransport()` delegate to TransportOrchestrator
- Connection management: `ActiveConnectionCount`, `GetActiveConnections()`, `BroadcastAsync()`, `SendAsync()` delegate to IServerChannelManager
- Service management: `RegisterService<T>()`, `UnregisterService()` delegate to PipelineCoordinator
- Observability: `GetPerformanceMetrics()` aggregates from all coordinators
- Observability: `GetHealthStatus()` delegates to PipelineCoordinator + adds transport stats
- Events: `StateChanged`, `ClientConnected`, `ClientDisconnected` (wire from coordinators)
- Implements `IDisposable` and `IAsyncDisposable`
- Thread-safe (uses coordinators' thread-safety)
- Properly logged (ILogger<PulseServer>)

**Checkpoint**: After T006, PulseServer exists as single unified implementation ✅

### T007: Update PulseServerBuilder to construct unified server [P]
**File**: `src/PulseRPC.Server/Builder/PulseServerBuilder.cs`
**Story**: US1 - Clear API Surface
**Description**: Update builder to construct new unified PulseServer
**Dependencies**: T006
**Acceptance Criteria**:
- Collects transport configurations via `.AddTcpTransport()`, `.AddKcpTransport()`
- Collects shutdown timeout via `.WithShutdownTimeout(TimeSpan)`
- `.Build()` constructs `ServerConfiguration` from collected settings
- `.Build()` resolves dependencies from DI or creates defaults
- `.Build()` constructs `ServerLifecycleCoordinator`, `TransportOrchestrator`, `PipelineCoordinator`
- `.Build()` constructs unified `PulseServer` and passes all coordinators
- `.Build()` calls `AddTransport()` for each collected transport config
- Maintains fluent API ergonomics (all methods return `IPulseServerBuilder`)
- Binary compatible with existing builder API (no breaking changes)

### T008: Update ServiceCollectionExtensions for DI registration [P]
**File**: `src/PulseRPC.Server/Extensions/ServiceCollectionExtensions.cs`
**Story**: US1 - Clear API Surface
**Description**: Update DI registration to construct unified PulseServer
**Dependencies**: T006
**Acceptance Criteria**:
- `.AddPulseServer(builder => ...)` extension method registers unified implementation
- Registers `IPulseServer` singleton factory
- Factory resolves: `ILoggerFactory`, `IOptions<ServerConfiguration>`, `IServerChannelManager`, `ITransportIntegrationManager`
- Factory constructs `PipelineCoordinator` with resolved dependencies
- Factory constructs `ServerLifecycleCoordinator`, `TransportOrchestrator`
- Factory constructs unified `PulseServer`
- Factory applies builder configuration (transports, options)
- Maintains backward compatibility with existing DI registration patterns
- Properly documented

**User Story 1 Deliverable**: Single public API entry point (PulseServer) via builder and DI ✅

---

## Phase 4: User Story 2 (P1) - Consistent Behavior

**Goal**: Unified implementation ensures consistent behavior regardless of configuration path

**Independent Test Criteria**:
- DI-configured server behaves identically to builder-configured server
- Performance metrics are consistent across multiple runs
- All existing integration tests pass without modification (100%)

### T009: Validate unified behavior with integration tests
**File**: `tests/PulseRPC.Server.Tests/Integration/UnifiedServerIntegrationTests.cs`
**Story**: US2 - Consistent Behavior
**Description**: Create end-to-end integration tests for unified server
**Dependencies**: T006, T007, T008
**Acceptance Criteria**:
- Test: Start/stop server via builder API → succeeds
- Test: Start/stop server via DI API → succeeds
- Test: Register service → invoke RPC → receive response (builder-configured server)
- Test: Register service → invoke RPC → receive response (DI-configured server)
- Test: Multi-transport server (TCP + KCP) → both transports accept connections
- Test: Graceful shutdown → in-flight messages complete within timeout
- Test: Graceful shutdown timeout → force shutdown after 30s
- Test: GetHealthStatus() returns accurate stats (message counts, backpressure level, etc.)
- Test: GetPerformanceMetrics() returns accurate stats (connection counts, throughput, etc.)
- All tests use xUnit + FluentAssertions
- Properly documented

### T010: Run existing integration tests against unified implementation
**Story**: US2 - Consistent Behavior
**Description**: Validate 100% compatibility by running all existing PulseServer/ServerHost integration tests
**Dependencies**: T006
**Acceptance Criteria**:
- Identify all existing integration tests in `tests/PulseRPC.Server.Tests/Integration/`
- Run tests against unified PulseServer implementation
- **Expected**: 100% pass rate (no test modifications required)
- Document any test failures with root cause analysis
- If failures occur: fix unified implementation (not tests)
- Update test documentation to reference unified implementation

**User Story 2 Deliverable**: Consistent behavior validated via integration tests ✅

---

## Phase 5: User Story 3 (P2) - Simplified Maintenance

**Goal**: Eliminate duplicate server orchestration logic (maintain only one implementation)

**Independent Test Criteria**:
- Code coverage shows zero duplication in server lifecycle logic
- Bug fixes require changes in only one location
- Code maintainability score improves by 25%

### T011: Remove old PulseServer implementation artifacts
**Story**: US3 - Simplified Maintenance
**Description**: Clean up old PulseServer implementation (already rewritten in T006)
**Dependencies**: T006, T010
**Acceptance Criteria**:
- Verify no references to old PulseServer internal components remain
- Remove any obsolete internal classes no longer used by unified implementation
- Update internal documentation references
- Verify build succeeds with zero warnings

### T012: Add unified server metrics and observability
**File**: `src/PulseRPC.Server/Observability/UnifiedServerMetrics.cs`
**Story**: US3 - Simplified Maintenance
**Description**: Create unified metrics aggregation (consolidates PulseServer + ServerHost metrics)
**Dependencies**: T006
**Acceptance Criteria**:
- Aggregates metrics from: TransportOrchestrator, PipelineCoordinator, IServerChannelManager
- `ServerPerformanceMetrics` includes:
  - ActiveConnections, TotalConnectionsAccepted (from channel manager)
  - TotalMessagesReceived, TotalMessagesDispatched, TotalResponsesSent (from pipeline)
  - QueueDepth, BackpressureLevel (from pipeline)
  - AverageLatencyMs, ThroughputMsgsPerSec (calculated from pipeline stats)
  - MemoryUsageMB (GC.GetTotalMemory), CpuUsagePercent (if available)
  - LastResetTime
- `ResetMetrics()` method resets all counters
- Thread-safe (uses Interlocked for counters)
- Properly documented

**User Story 3 Deliverable**: Single, maintainable implementation with no duplication ✅

---

## Phase 6: User Story 4 (P2) - Migration Path

**Goal**: Provide clear migration path for existing ServerHost users (binary compatibility + deprecation warnings)

**Independent Test Criteria**:
- Migration documentation completable in <30 minutes
- Existing applications upgrade without code changes (100% binary compat)
- Deprecation warnings guide users to unified API

### T013: Implement ServerHost facade with deprecation
**File**: `src/PulseRPC.Server/Core/ServerHost.cs`
**Story**: US4 - Migration Path
**Description**: Convert ServerHost to thin facade that delegates to unified PulseServer
**Dependencies**: T006
**Acceptance Criteria**:
- Mark class with `[Obsolete("ServerHost is deprecated. Use PulseServer instead. See migration guide at docs/quickstart.md", false)]`
- Constructor: `ServerHost(IPulseServerTransport transport, ServerHostOptions? options)`
- Constructor converts `ServerHostOptions` → `ServerConfiguration`
  - Maps transport parameter to TransportChannelConfiguration
  - Maps MessageReceiverOptions, MessageDispatcherOptions, etc.
  - Sets ShutdownTimeout to 30s
- Constructor constructs unified `PulseServer` internally (stores as private field)
- All public methods delegate to internal `_unifiedServer` field:
  - `StartAsync(ct)` → `_unifiedServer.StartAsync(ct)`
  - `StopAsync(ct)` → `_unifiedServer.StopAsync(ct)`
  - `IsRunning` → `_unifiedServer.IsRunning`
  - `RegisterService<T>(name, instance, options)` → `_unifiedServer.RegisterService(...)`
  - `UnregisterService(name)` → `_unifiedServer.UnregisterService(name)`
  - `GetHealthStatus()` → `_unifiedServer.GetHealthStatus()`
- Expose pipeline component properties (ServerHost-exclusive API):
  - `ConnectionManager` → `_unifiedServer.GetPipelineCoordinator().ConnectionManager`
  - `ServiceRegistry` → `_unifiedServer.GetPipelineCoordinator().ServiceRegistry`
  - `BackpressurePolicy` → `_unifiedServer.GetPipelineCoordinator().BackpressurePolicy`
- `Dispose()` → `_unifiedServer.Dispose()`
- Zero-overhead delegation (inline-candidate methods, no allocations)
- Properly documented with migration guidance

### T014: Create ServerHost facade unit tests [P]
**File**: `tests/PulseRPC.Server.Tests/Unit/ServerHostFacadeTests.cs`
**Story**: US4 - Migration Path
**Description**: Unit tests verifying ServerHost correctly delegates to PulseServer
**Dependencies**: T013
**Acceptance Criteria**:
- Test: Constructor converts ServerHostOptions correctly
- Test: StartAsync() delegates to unified server
- Test: StopAsync() delegates to unified server
- Test: RegisterService() delegates correctly
- Test: GetHealthStatus() returns accurate data
- Test: Pipeline component properties return correct instances
- Test: Dispose() disposes unified server
- All tests use xUnit + NSubstitute (mock dependencies)
- Properly documented

### T015: Create binary compatibility validation tests [P]
**File**: `tests/PulseRPC.Server.Tests/Integration/BinaryCompatibilityTests.cs`
**Story**: US4 - Migration Path
**Description**: Integration tests validating 100% binary compatibility
**Dependencies**: T013
**Acceptance Criteria**:
- Test: ServerHost facade (old API) → start/stop → succeeds
- Test: ServerHost facade → register service → invoke RPC → response received
- Test: ServerHost facade exposes ConnectionManager property
- Test: ServerHost facade exposes ServiceRegistry property
- Test: ServerHost facade exposes BackpressurePolicy property
- Test: ServerHost facade deprecation warning appears in build output
- Test: Existing ServerHost-based code compiles and runs without modifications
- All tests use xUnit + FluentAssertions
- Properly documented

### T016: Update migration documentation (quickstart.md)
**File**: `specs/006-unify-dual-server/quickstart.md` (already exists, enhance if needed)
**Story**: US4 - Migration Path
**Description**: Validate and enhance migration guide for completeness
**Dependencies**: T013
**Acceptance Criteria**:
- Scenario 1: PulseServer user (no changes) - documented ✅
- Scenario 2: ServerHost user (facade, zero changes) - documented ✅
- Scenario 3: ServerHost user (migrate to PulseServer) - step-by-step guide
- Scenario 4: Multi-transport migration - example code
- Scenario 5: DI registration - example code
- Scenario 6: Pipeline component access - before/after patterns
- Scenario 7: Custom extension methods - rewrite guidance ✅
- Each scenario has: before code, after code, explanation
- Estimated migration time: <30 minutes (validated)
- Clear deprecation warning explanation
- Troubleshooting section for common issues

**User Story 4 Deliverable**: Binary-compatible migration path with clear documentation ✅

---

## Phase 7: Polish & Integration

**Objective**: Cross-cutting concerns, performance validation, final documentation

### T017: Implement IHostedService adapter for ASP.NET Core [P]
**File**: `src/PulseRPC.Server/Extensions/HostedServiceAdapter.cs`
**Story**: Polish
**Description**: Create IHostedService wrapper for unified PulseServer (ASP.NET Core integration)
**Dependencies**: T006
**Acceptance Criteria**:
- Implements `IHostedService` interface
- Constructor takes `IPulseServer` dependency
- `StartAsync(ct)` → `_server.StartAsync(ct)`
- `StopAsync(ct)` → `_server.StopAsync(ct)` (uses host shutdown timeout)
- Extension method: `.AddPulseServerHostedService()` registers adapter
- Properly documented with usage examples

### T018: Create performance benchmark for facade delegation [P]
**File**: `tests/PulseRPC.Server.Tests/Performance/FacadeDelegationBenchmark.cs`
**Story**: Polish
**Description**: BenchmarkDotNet test measuring ServerHost facade overhead
**Dependencies**: T013
**Acceptance Criteria**:
- Benchmark: Direct unified PulseServer usage (baseline)
- Benchmark: ServerHost facade usage (measure overhead)
- Metrics: Message throughput (messages/second)
- Test scenario: 10,000 messages, measure QPS
- **Expected**: Facade overhead <5% (spec requirement)
- Results documented in benchmark report
- Uses BenchmarkDotNet with proper configuration
- Properly documented

### T019: Run full performance regression suite
**Story**: Polish
**Description**: Validate no performance regression in existing benchmarks
**Dependencies**: T006, T010
**Acceptance Criteria**:
- Run existing benchmarks in `perf/BenchmarkApp/` against unified implementation
- Compare results against baseline (from research.md):
  - Average latency: 19.5ms (expect similar)
  - P95: ~45ms, P99: ~85ms (expect similar or better)
  - QPS: 46-68 (expect similar or better)
- Document any performance regressions with root cause
- If regressions occur: optimize unified implementation
- Generate performance report

### T020: Update public API documentation [P]
**File**: `src/PulseRPC.Server/PulseServer.cs` (XML comments)
**Story**: Polish
**Description**: Ensure unified PulseServer has complete XML documentation
**Dependencies**: T006
**Acceptance Criteria**:
- All public methods have `<summary>` tags
- All public properties have `<summary>` tags
- All parameters have `<param>` tags
- All events have `<summary>` tags
- Complex methods have `<remarks>` with usage examples
- Thread-safety guarantees documented where applicable
- Async method cancellation behavior documented
- Properly formatted for IntelliSense

### T021: Update builder API documentation [P]
**File**: `src/PulseRPC.Server/Builder/PulseServerBuilder.cs` (XML comments)
**Story**: Polish
**Description**: Ensure builder API has complete documentation
**Dependencies**: T007
**Acceptance Criteria**:
- All public methods have `<summary>` tags
- Usage examples in `<example>` tags
- Fluent API chain documented
- Default values documented
- Properly formatted for IntelliSense

### T022: Update project README with unified server info
**File**: `README.md` (repository root)
**Story**: Polish
**Description**: Update main README to reference unified PulseServer (remove confusion)
**Dependencies**: T006
**Acceptance Criteria**:
- Quick start section uses unified PulseServer (not ServerHost)
- Example code shows builder API
- Remove references to "which server class to use"
- Add note about ServerHost deprecation
- Link to migration guide
- Updated code examples

### T023: Create changelog entry for unified server release
**File**: `CHANGELOG.md` (repository root)
**Story**: Polish
**Description**: Document unified server changes in changelog
**Dependencies**: All implementation tasks
**Acceptance Criteria**:
- Version entry (e.g., v2.0.0)
- Section: "Breaking Changes" (note: NONE due to facade)
- Section: "Added"
  - Unified PulseServer implementation
  - Graceful shutdown with 30s default timeout
  - Comprehensive health monitoring API
- Section: "Deprecated"
  - ServerHost class (use PulseServer instead)
- Section: "Fixed"
  - Incomplete service registration in old PulseServer
- Migration guide link
- Performance improvements noted

### T024: Verify PublicAPI analyzer compliance [P]
**Story**: Polish
**Description**: Ensure PublicAPI.Shipped.txt and PublicAPI.Unshipped.txt are updated
**Dependencies**: T006, T013
**Acceptance Criteria**:
- Run `dotnet build` with PublicAPI analyzers enabled
- Zero API analyzer warnings
- `PublicAPI.Unshipped.txt` contains new unified PulseServer APIs
- `PublicAPI.Shipped.txt` remains unchanged (no breaking changes)
- ServerHost deprecation recorded in Unshipped.txt

### T025: Run full test suite and verify coverage
**Story**: Polish
**Description**: Final validation - all tests pass, coverage targets met
**Dependencies**: All test tasks
**Acceptance Criteria**:
- Run `dotnet test` → 100% pass rate
- Unit tests: pass
- Integration tests: pass
- Binary compatibility tests: pass
- Performance benchmarks: <5% overhead validated
- Code coverage: >80% for new code (unified implementation)
- Coverage report generated

### T026: Final code review and cleanup
**Story**: Polish
**Description**: Manual code review of all changes
**Dependencies**: All implementation tasks
**Acceptance Criteria**:
- No commented-out code
- No TODO comments without tracking issues
- Consistent code style (follows project conventions)
- No compiler warnings
- No ReSharper/Rider warnings
- Nullable reference types handled correctly
- Async/await patterns correct (no blocking calls)
- Thread-safety verified

### T027: Update NuGet package metadata
**File**: `Directory.Build.props`
**Story**: Polish
**Description**: Update package metadata for unified server release
**Dependencies**: T023
**Acceptance Criteria**:
- Version bumped (e.g., 1.x.x → 2.0.0)
- Release notes reference unified server
- Deprecation notice for ServerHost in package description
- Migration guide link in package README
- Tags updated if needed

### T028: Create release branch and prepare for merge
**Story**: Polish
**Description**: Final preparation for merging to main
**Dependencies**: T025, T026, T027
**Acceptance Criteria**:
- All tasks complete
- All tests passing
- Documentation complete
- Changelog updated
- Branch ready for pull request
- Pull request description references:
  - Feature spec: `specs/006-unify-dual-server/spec.md`
  - Implementation plan: `specs/006-unify-dual-server/plan.md`
  - Research: `specs/006-unify-dual-server/research.md`
  - Migration guide: `specs/006-unify-dual-server/quickstart.md`
- Pull request checklist complete

---

## Task Dependencies Graph

```
Setup Phase (T001-T002)
    │
    ▼
Foundational Phase (T003-T005)
    │
    ├──────────────────────────────┐
    │                              │
    ▼                              ▼
US1: Clear API                 US2: Consistent
T006 → T007, T008 [P]          T009, T010
    │                              │
    └──────────────┬───────────────┘
                   │
                   ▼
              US3: Simplified
              T011, T012 [P]
                   │
                   ▼
              US4: Migration
              T013 → T014, T015, T016 [P]
                   │
                   ▼
              Polish Phase
              T017-T028 (many [P])
```

---

## Parallel Execution Opportunities

### Phase 1 (Setup)
- **Parallel**: T001, T002 (different files)

### Phase 2 (Foundational)
- **Sequential**: T003 → T004, T005 (T004/T005 depend on T003 for state coordination)
- **Parallel**: T004, T005 (different files, different concerns)

### Phase 3 (US1)
- **Sequential**: T006 first (core implementation)
- **Parallel after T006**: T007, T008 (builder and DI registration)

### Phase 4 (US2)
- **Sequential**: T009, T010 (test validation)

### Phase 5 (US3)
- **Parallel**: T011, T012 (cleanup and metrics - independent)

### Phase 6 (US4)
- **Sequential**: T013 first (facade implementation)
- **Parallel after T013**: T014, T015, T016 (tests and docs)

### Phase 7 (Polish)
- **Parallel**: T017, T018, T020, T021, T022, T024 (independent concerns)
- **Sequential**: T019 (needs T006), T023 (needs all), T025 (needs tests), T026 (needs all), T027 (needs T023), T028 (needs all)

---

## Implementation Strategy

### Recommended Approach: Incremental Delivery

1. **MVP Scope** (Minimum Viable Product):
   - Complete through User Story 1 (T001-T008)
   - Deliverable: Single unified PulseServer API that works
   - Validation: Can create and start server via builder/DI
   - Estimated: 3-5 days

2. **Production-Ready** (Add behavioral validation):
   - Complete through User Story 2 (T009-T010)
   - Deliverable: Validated unified implementation (100% test pass)
   - Validation: Existing integration tests pass unchanged
   - Estimated: +2 days (5-7 days total)

3. **Maintainability** (Eliminate duplication):
   - Complete User Story 3 (T011-T012)
   - Deliverable: Zero duplication, unified metrics
   - Validation: Code coverage analysis shows consolidation
   - Estimated: +1 day (6-8 days total)

4. **Migration Support** (Binary compatibility):
   - Complete User Story 4 (T013-T016)
   - Deliverable: ServerHost facade + migration docs
   - Validation: Existing apps upgrade without code changes
   - Estimated: +2 days (8-10 days total)

5. **Production Polish** (Final hardening):
   - Complete Phase 7 (T017-T028)
   - Deliverable: Performance validated, docs complete, ready for release
   - Validation: All quality gates pass
   - Estimated: +2-3 days (10-13 days total)

**Total Estimated Effort**: 10-15 days (matches plan.md estimate)

---

## Success Metrics (Per User Story)

### User Story 1: Clear API Surface
- ✅ Exactly one public server class (PulseServer) in API docs
- ✅ IntelliSense shows single primary entry point
- ✅ New developer creates working server in <5 minutes

### User Story 2: Consistent Behavior
- ✅ DI-configured server behaves identically to builder-configured
- ✅ Performance metrics consistent across runs
- ✅ 100% existing integration tests pass unchanged (SC-004)

### User Story 3: Simplified Maintenance
- ✅ Zero duplicated server orchestration logic (SC-003)
- ✅ Bug fixes require changes in single location
- ✅ Code maintainability score improves 25% (SC-007)

### User Story 4: Migration Path
- ✅ Migration documentation completable in <30 minutes (SC-005)
- ✅ 100% binary compatibility validated (SC-010)
- ✅ Clear deprecation warnings guide users

---

## Notes

- **Test Philosophy**: Integration tests validate behavior, unit tests validate components. Focus on integration coverage for acceptance criteria.
- **Performance Validation**: SC-009 requires <5% facade overhead (validated in T018)
- **Binary Compatibility**: SC-010 requires zero breaking changes (validated in T015)
- **Documentation**: SC-002 requires 40% onboarding time reduction (validated through migration time <30 min in SC-005)

---

**Next Step**: Begin implementation starting with Phase 1 (T001-T002), then Phase 2 (T003-T005), then User Story 1 (T006-T008).
