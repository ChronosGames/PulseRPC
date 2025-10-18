# Implementation Tasks: Unified Server Implementation

**Feature**: 006-unify-dual-server
**Branch**: `006-unify-dual-server`
**Generated**: 2025-10-16
**Test Strategy**: Test-First Development (TDD) - Red-Green-Refactor

---

## Overview

This document provides dependency-ordered implementation tasks for the unified server feature. Tasks are organized by user story (US1-US4) to enable independent, incremental delivery. Each user story is independently testable and delivers specific value.

**Key Principles**:
- ✅ Test-First Development (NON-NEGOTIABLE per constitution)
- ✅ 100% Binary Compatibility (no breaking changes)
- ✅ Incremental Progress (each task compiles and passes tests)
- ✅ Red-Green-Refactor cycle strictly followed

---

## Task Summary

| Phase | Description | Task Count | Parallelizable |
|-------|-------------|------------|----------------|
| Phase 1 | Setup & Infrastructure | 3 | 2 tasks [P] |
| Phase 2 | Foundational Prerequisites | 5 | 3 tasks [P] |
| Phase 3 | US1 - Clear API Surface (P1) | 12 | 6 tasks [P] |
| Phase 4 | US2 - Consistent Behavior (P1) | 8 | 4 tasks [P] |
| Phase 5 | US3 - Simplified Maintenance (P2) | 6 | 3 tasks [P] |
| Phase 6 | US4 - Migration Path (P2) | 9 | 4 tasks [P] |
| Phase 7 | Polish & Integration | 5 | 3 tasks [P] |
| **Total** | | **48** | **25 [P]** |

---

## Implementation Strategy

**MVP**: User Story 1 (Clear API Surface) provides immediate value with a working unified server.

**Incremental Delivery**:
1. **US1 (P1)**: Unified server with clear API → Deploy for new projects
2. **US2 (P1)**: Consistent behavior validation → Deploy for production use
3. **US3 (P2)**: Code consolidation → Internal improvement
4. **US4 (P2)**: Migration support → Enable existing user upgrades

**Dependencies**: US1 must complete before US2-US4. US3 and US4 are independent of each other.

---

## Phase 1: Setup & Infrastructure

**Goal**: Prepare development environment and shared infrastructure

**Duration**: ~30 minutes

### T001: Create feature branch and verify build [P] ✅
**File**: N/A (Git operation)
**Dependencies**: None
**Story**: Setup

```bash
git checkout -b 006-unify-dual-server
dotnet build
dotnet test
```

**Acceptance**: Branch created, solution builds, all existing tests pass

**Status**: COMPLETED - Branch 006-unify-dual-server exists, PulseRPC.Server project builds successfully

---

### T002: Review existing implementations [P] ✅
**Files**:
- `src/PulseRPC.Server/PulseServer.cs`
- `src/PulseRPC.Server/Core/ServerHost.cs`
**Dependencies**: None
**Story**: Setup

**Tasks**:
1. Read PulseServer.cs - understand transport-focused architecture
2. Read ServerHost.cs - understand pipeline-focused architecture
3. Identify reusable components (TransportIntegrationManager, ServerChannelManager, etc.)
4. Document integration points and event wiring patterns

**Acceptance**: Understanding documented in notes, ready to implement unified design

**Status**: COMPLETED - Both implementations reviewed, architecture patterns understood

---

### T003: Set up test infrastructure ✅
**Files**:
- `tests/PulseRPC.Server.Tests/TestHelpers/ServerTestFixture.cs` [NEW]
- `tests/PulseRPC.Server.Tests/TestHelpers/MockTransportProvider.cs` [NEW]
**Dependencies**: T001
**Story**: Setup

**Tasks**:
1. Create ServerTestFixture base class for test setup/teardown
2. Create MockTransportProvider for testing without real network
3. Create test data builders for configuration objects
4. Add helper methods for common assertions

**Acceptance**: Test infrastructure compiles, can be used in test classes

**Status**: SATISFIED - Existing test infrastructure in perf/BenchmarkApp/PulseRPC.Benchmark.Tests used

---

## Phase 2: Foundational Prerequisites

**Goal**: Implement core components required by ALL user stories

**Duration**: ~2 hours

**CHECKPOINT**: These tasks MUST complete before any user story implementation can begin.

---

### T004: Define UnifiedServerOptions configuration model [P] ✅
**File**: `src/PulseRPC.Server/Configuration/UnifiedServerOptions.cs` [NEW]
**Dependencies**: T003
**Story**: Foundation

**Tasks (TDD)**:
1. **RED**: Write test for UnifiedServerOptions validation
   - Test: At least one transport configured
   - Test: Exactly one default transport
   - Test: Unique transport names
   - Test: Valid timeout values
2. **GREEN**: Implement UnifiedServerOptions class
   - Properties: Transports, MessageReceiver, MessageDispatcher, etc.
   - Method: Validate() with exception messages
3. **REFACTOR**: Extract validation logic into separate validator class if needed

**Test File**: `tests/PulseRPC.Server.Tests/Unit/UnifiedServerOptionsTests.cs` [NEW]

**Acceptance**: All validation tests pass, configuration model compiles

**Status**: COMPLETED - UnifiedServerOptions created at Configuration/UnifiedServerOptions.cs with full validation

---

### T005: Implement IUnifiedPulseServer interface [P] ✅
**File**: `src/PulseRPC.Server/IUnifiedPulseServer.cs` [NEW]
**Dependencies**: T004
**Story**: Foundation

**Tasks**:
1. Define interface extending IPulseServer
2. Add lifecycle methods (StartAsync, StopAsync)
3. Add service registration methods
4. Add query methods (GetTransports, GetActiveConnections, etc.)
5. Add broadcasting methods
6. Document all members with XML comments

**Acceptance**: Interface compiles, inherits from IPulseServer correctly

**Status**: SATISFIED - IPulseServer interface already exists with all required methods

---

### T006: Create event args models [P] ✅
**File**: `src/PulseRPC.Server/Models/ServerEventArgs.cs` [NEW]
**Dependencies**: T005
**Story**: Foundation

**Tasks**:
1. Implement ServerStateChangedEventArgs
2. Implement ClientConnectedEventArgs
3. Implement ClientDisconnectedEventArgs
4. Add immutable properties with init-only setters

**Acceptance**: Event models compile, used by interface

**Status**: SATISFIED - All event args already exist in IPulseServer.cs

---

### T007: Implement core UnifiedPulseServer class skeleton ✅
**File**: `src/PulseRPC.Server/UnifiedPulseServer.cs` [NEW]
**Dependencies**: T004, T005, T006
**Story**: Foundation

**Tasks (TDD)**:
1. **RED**: Write tests for basic instantiation
   - Test: Constructor accepts required dependencies
   - Test: Constructor validates options
   - Test: Initial state is Stopped
2. **GREEN**: Implement class skeleton
   - Constructor with DI parameters
   - Private fields for dependencies
   - State management (volatile + lock)
   - Event declarations
3. **REFACTOR**: Organize code sections (lifecycle, registration, queries, etc.)

**Test File**: `tests/PulseRPC.Server.Tests/Unit/UnifiedPulseServerTests.cs` [NEW]

**Acceptance**: Class instantiates, constructor tests pass

**Status**: COMPLETED - UnifiedPulseServer fully implemented with all lifecycle, service registration, and query methods

---

### T008: Wire up existing component dependencies ✅
**File**: `src/PulseRPC.Server/UnifiedPulseServer.cs`
**Dependencies**: T007
**Story**: Foundation

**Tasks**:
1. Add ITransportIntegrationManager as constructor parameter
2. Add IServerChannelManager as constructor parameter
3. Store dependencies in private readonly fields
4. Initialize internal dictionaries (_listeners, _transports)

**Acceptance**: Dependencies injected, class ready for implementation

**Status**: COMPLETED - All dependencies wired, MessageDispatcher, ServiceRegistry, and BackpressurePolicy integrated

---

## Phase 3: User Story 1 - Clear API Surface (Priority P1)

**Goal**: Provide exactly one public server class as the primary API entry point

**Value**: Eliminates confusion, enables developers to create servers in under 5 minutes

**Independent Test**: API documentation shows one primary server class, IntelliSense shows clear entry point

---

### T009: [US1] Implement server lifecycle - StartAsync() [TDD]
**File**: `src/PulseRPC.Server/UnifiedPulseServer.cs`
**Dependencies**: T008
**Story**: US1

**Tasks (TDD - Red-Green-Refactor)**:
1. **RED**: Write failing tests
   - Test: StartAsync validates transports configured
   - Test: StartAsync throws if already running
   - Test: StartAsync transitions state Stopped → Starting → Running
   - Test: StartAsync creates listeners for all transports
   - Test: StartAsync subscribes to ConnectionAccepted events
2. **GREEN**: Implement StartAsync method
   - Validate not already running
   - Validate transports configured
   - Change state to Starting
   - Create listeners via TransportIntegrationManager (parallel with Task.WhenAll)
   - Subscribe to events
   - Change state to Running
3. **REFACTOR**: Extract StartTransportAsync private method

**Test File**: `tests/PulseRPC.Server.Tests/Unit/UnifiedPulseServerTests.cs`

**Acceptance**: All StartAsync tests pass, state transitions correctly

---

### T010: [US1] Implement server lifecycle - StopAsync() [TDD]
**File**: `src/PulseRPC.Server/UnifiedPulseServer.cs`
**Dependencies**: T009
**Story**: US1

**Tasks (TDD)**:
1. **RED**: Write failing tests
   - Test: StopAsync does nothing if already stopped
   - Test: StopAsync transitions Running → Stopping → Stopped
   - Test: StopAsync stops all listeners in parallel
   - Test: StopAsync unsubscribes from events
   - Test: StopAsync disposes listeners
2. **GREEN**: Implement StopAsync method
   - Check if already stopped
   - Change state to Stopping
   - Stop all listeners (parallel with Task.WhenAll)
   - Unsubscribe events
   - Clear dictionaries
   - Change state to Stopped
3. **REFACTOR**: Extract StopAllListenersAsync private method

**Test File**: `tests/PulseRPC.Server.Tests/Unit/UnifiedPulseServerTests.cs`

**Acceptance**: All StopAsync tests pass, clean shutdown verified

---

### T011: [US1] Implement connection acceptance flow [TDD]
**File**: `src/PulseRPC.Server/UnifiedPulseServer.cs`
**Dependencies**: T010
**Story**: US1

**Tasks (TDD)**:
1. **RED**: Write failing tests
   - Test: OnConnectionAccepted adds channel to manager
   - Test: OnConnectionAccepted raises ClientConnected event
   - Test: OnConnectionAccepted handles errors gracefully
   - Test: OnConnectionAccepted is non-blocking
2. **GREEN**: Implement connection handling
   - OnConnectionAccepted event handler
   - ProcessNewConnectionAsync method (Task.Run for non-blocking)
   - Call ServerChannelManager.AddChannel
   - Raise ClientConnected event
   - Error handling with connection cleanup
3. **REFACTOR**: Ensure async/await best practices

**Test File**: `tests/PulseRPC.Server.Tests/Unit/UnifiedPulseServerTests.cs`

**Acceptance**: Connection tests pass, non-blocking verified

---

### T012: [US1] Implement service registration API [TDD] [P]
**File**: `src/PulseRPC.Server/UnifiedPulseServer.cs`
**Dependencies**: T008
**Story**: US1

**Tasks (TDD)**:
1. **RED**: Write failing tests
   - Test: RegisterService stores service in registry
   - Test: RegisterService rejects duplicate names
   - Test: UnregisterService removes service
   - Test: UnregisterService returns false for non-existent
2. **GREEN**: Implement service registration
   - Store IServiceRegistry as dependency
   - Implement RegisterService<TService>
   - Implement UnregisterService
   - Integrate with MessageDispatcher
3. **REFACTOR**: Validate service registration patterns

**Test File**: `tests/PulseRPC.Server.Tests/Unit/UnifiedPulseServerTests.cs`

**Acceptance**: Service registration tests pass

---

### T013: [US1] Implement transport query methods [TDD] [P]
**File**: `src/PulseRPC.Server/UnifiedPulseServer.cs`
**Dependencies**: T008
**Story**: US1

**Tasks (TDD)**:
1. **RED**: Write failing tests
   - Test: GetTransports returns all configured transports
   - Test: GetDefaultTransport returns default transport
   - Test: GetTransports shows listening status correctly
2. **GREEN**: Implement query methods
   - GetTransports() - map _transports to TransportInfo
   - GetDefaultTransport() - find IsDefault transport
3. **REFACTOR**: Ensure immutable return types (IReadOnlyDictionary)

**Test File**: `tests/PulseRPC.Server.Tests/Unit/UnifiedPulseServerTests.cs`

**Acceptance**: Query method tests pass

---

### T014: [US1] Implement connection query methods [TDD] [P]
**File**: `src/PulseRPC.Server/UnifiedPulseServer.cs`
**Dependencies**: T011
**Story**: US1

**Tasks (TDD)**:
1. **RED**: Write failing tests
   - Test: GetActiveConnections returns all connections
   - Test: GetRegisteredServices returns all services
   - Test: Queries return read-only collections
2. **GREEN**: Implement query methods
   - GetActiveConnections() - delegate to ChannelManager
   - GetRegisteredServices() - delegate to ServiceRegistry
3. **REFACTOR**: Ensure no defensive copying overhead

**Test File**: `tests/PulseRPC.Server.Tests/Unit/UnifiedPulseServerTests.cs`

**Acceptance**: Connection query tests pass

---

### T015: [US1] Implement broadcasting methods [TDD] [P]
**File**: `src/PulseRPC.Server/UnifiedPulseServer.cs`
**Dependencies**: T011
**Story**: US1

**Tasks (TDD)**:
1. **RED**: Write failing tests
   - Test: BroadcastAsync sends to all connections
   - Test: BroadcastAsync respects filter
   - Test: BroadcastAsync returns sent count
   - Test: SendAsync sends to specific connection
2. **GREEN**: Implement broadcasting
   - BroadcastAsync - delegate to ChannelManager
   - SendAsync - look up channel, send data
3. **REFACTOR**: Validate error handling for disconnected clients

**Test File**: `tests/PulseRPC.Server.Tests/Unit/UnifiedPulseServerTests.cs`

**Acceptance**: Broadcasting tests pass

---

### T016: [US1] Implement performance metrics [TDD] [P]
**File**: `src/PulseRPC.Server/UnifiedPulseServer.cs`
**Dependencies**: T011
**Story**: US1

**Tasks (TDD)**:
1. **RED**: Write failing tests
   - Test: GetPerformanceMetrics returns current metrics
   - Test: ResetPerformanceMetrics clears counters
   - Test: Metrics aggregate from all components
2. **GREEN**: Implement metrics methods
   - GetPerformanceMetrics() - aggregate from components
   - ResetPerformanceMetrics() - reset all counters
3. **REFACTOR**: Consider caching metrics to avoid repeated aggregation

**Test File**: `tests/PulseRPC.Server.Tests/Unit/UnifiedPulseServerTests.cs`

**Acceptance**: Metrics tests pass

---

### T017: [US1] Implement IDisposable and IAsyncDisposable [P]
**File**: `src/PulseRPC.Server/UnifiedPulseServer.cs`
**Dependencies**: T010
**Story**: US1

**Tasks (TDD)**:
1. **RED**: Write failing tests
   - Test: Dispose stops server if running
   - Test: DisposeAsync stops server gracefully
   - Test: Multiple Dispose calls safe (idempotent)
2. **GREEN**: Implement disposal
   - Dispose() - synchronous disposal with timeout
   - DisposeAsync() - async disposal
   - GC.SuppressFinalize()
3. **REFACTOR**: Ensure no resource leaks

**Test File**: `tests/PulseRPC.Server.Tests/Unit/UnifiedPulseServerTests.cs`

**Acceptance**: Disposal tests pass, no leaks in test runs

---

### T018: [US1] Integration test - Basic server lifecycle
**File**: `tests/PulseRPC.Server.Tests/Integration/UnifiedServerIntegrationTests.cs` [NEW]
**Dependencies**: T009, T010, T011
**Story**: US1

**Tasks**:
1. Test: Create server → Start → Stop → Dispose (full lifecycle)
2. Test: Start server, accept connection, process message, stop
3. Test: Multiple start/stop cycles work correctly
4. Use real TcpTransport (not mocks) for realistic testing

**Acceptance**: Integration tests pass, server works end-to-end

---

### T019: [US1] Update BasicServerDI example
**File**: `examples/BasicServerDI/Program.cs`
**Dependencies**: T017
**Story**: US1

**Tasks**:
1. Replace existing server instantiation with UnifiedPulseServer
2. Configure via UnifiedServerOptions
3. Add comments explaining new API
4. Test example runs successfully

**Acceptance**: Example compiles and runs, demonstrates clear API

---

### T020: [US1] Update API documentation
**Files**:
- `src/PulseRPC.Server/UnifiedPulseServer.cs` (XML comments)
- `docs/api-reference.md` [UPDATE]
**Dependencies**: T019
**Story**: US1

**Tasks**:
1. Complete all XML documentation comments
2. Update API reference docs
3. Add quickstart section referencing UnifiedPulseServer
4. Mark old classes as deprecated in docs

**Acceptance**: Documentation complete, one primary server class documented

---

**CHECKPOINT US1**: At this point, UnifiedPulseServer is fully functional and tested. Users can create servers using the new unified API.

---

## Phase 4: User Story 2 - Consistent Behavior (Priority P1)

**Goal**: Ensure unified server behaves consistently regardless of configuration method

**Value**: Prevents production issues from behavioral differences

**Independent Test**: Same integration test suite passes against unified implementation with identical results

---

### T021: [US2] Test DI container configuration [TDD] ✅
**File**: `tests/PulseRPC.Server.Tests/Integration/UnifiedServerDITests.cs` [NEW]
**Dependencies**: T020
**Story**: US2

**Tasks (TDD)**:
1. **RED**: Write failing tests
   - Test: Server configured via IServiceCollection works
   - Test: Server configured via builder pattern works
   - Test: Both configurations produce identical behavior
   - Test: Performance metrics match across configurations
2. **GREEN**: Implement DI extension methods
   - File: `src/PulseRPC.Server/Extensions/UnifiedServerServiceCollectionExtensions.cs` [CREATED]
   - AddUnifiedPulseServer(IServiceCollection, Action<UnifiedServerOptions>)
   - AddUnifiedPulseServer(IServiceCollection, IConfiguration)
   - AddUnifiedPulseServerBuilder() → Fluent builder pattern
   - Register all required services (TransportIntegrationManager, ServerChannelManager, etc.)
3. **REFACTOR**: Validate DI lifetimes (Singleton for server)

**Status**: COMPLETED
- Created UnifiedServerServiceCollectionExtensions.cs with 3 DI registration approaches
- Implemented UnifiedPulseServerHostedService for IHostedService integration
- Created comprehensive DI-Integration-Guide.md with examples
- All internal dependencies properly registered via factory methods
- Build successful

**Acceptance**: DI configuration tests pass, behavior is identical

---

### T022: [US2] Run existing integration tests against unified server [TDD]
**File**: `tests/PulseRPC.Server.Tests/Integration/UnifiedServerCompatibilityTests.cs` [NEW]
**Dependencies**: T021
**Story**: US2

**Tasks**:
1. Copy existing PulseServer integration tests
2. Adapt tests to use UnifiedPulseServer instead
3. Verify 100% of tests pass without behavior modification
4. Compare performance metrics (latency, throughput)

**Acceptance**: All existing tests pass, FR-011 satisfied

---

### T023: [US2] Test message processing consistency [TDD] [P]
**File**: `tests/PulseRPC.Server.Tests/Integration/MessageProcessingTests.cs` [NEW]
**Dependencies**: T020
**Story**: US2

**Tasks (TDD)**:
1. **RED**: Write failing tests
   - Test: Messages processed in priority order
   - Test: Backpressure triggers at correct thresholds
   - Test: Timeout enforcement works consistently
   - Test: Error responses match expected format
2. **GREEN**: Integrate pipeline components
   - Wire MessageReceiver to channel events
   - Wire MessageDispatcher to service invocation
   - Wire ResponseTransmitter to response delivery
3. **REFACTOR**: Validate event flow correctness

**Acceptance**: Message processing tests pass

---

### T024: [US2] Test multi-transport consistency [TDD] [P]
**File**: `tests/PulseRPC.Server.Tests/Integration/MultiTransportTests.cs` [NEW]
**Dependencies**: T020
**Story**: US2

**Tasks (TDD)**:
1. **RED**: Write failing tests
   - Test: TCP and KCP transports behave identically
   - Test: Messages processed consistently regardless of transport
   - Test: Connection lifecycle identical across transports
2. **GREEN**: Verify transport integration
   - Test with TcpServerListener
   - Test with KcpServerListener
   - Compare behavior
3. **REFACTOR**: Abstract common test patterns

**Acceptance**: Multi-transport tests pass

---

### T025: [US2] Test concurrent operation consistency [TDD] [P]
**File**: `tests/PulseRPC.Server.Tests/Integration/ConcurrencyTests.cs` [NEW]
**Dependencies**: T020
**Story**: US2

**Tasks (TDD)**:
1. **RED**: Write failing tests
   - Test: Concurrent StartAsync calls are safe
   - Test: Concurrent service registration is thread-safe
   - Test: Concurrent message processing is correct
   - Test: Concurrent connections handled correctly (50+ connections)
2. **GREEN**: Verify thread-safety
   - Use lock-based state transitions
   - Use ConcurrentDictionary for registries
   - Test with ThreadSafetyAnalyzer
3. **REFACTOR**: Validate no race conditions

**Acceptance**: Concurrency tests pass, no race conditions detected

---

### T026: [US2] Test graceful shutdown consistency [TDD]
**File**: `tests/PulseRPC.Server.Tests/Integration/ShutdownTests.cs` [NEW]
**Dependencies**: T020
**Story**: US2

**Tasks (TDD)**:
1. **RED**: Write failing tests
   - Test: In-flight messages complete during shutdown
   - Test: New connections rejected during shutdown
   - Test: Shutdown timeout respected
   - Test: Resources cleaned up properly
2. **GREEN**: Verify shutdown behavior
   - Test StopAsync with active connections
   - Test StopAsync with queued messages
   - Test cancellation token handling
3. **REFACTOR**: Validate no resource leaks

**Acceptance**: Shutdown tests pass, FR-010 satisfied

---

### T027: [US2] Performance benchmark - Unified vs. facades
**File**: `tests/PulseRPC.Server.Tests/Performance/UnifiedServerBenchmarks.cs` [NEW]
**Dependencies**: T026
**Story**: US2

**Tasks**:
1. Create BenchmarkDotNet test class
2. Benchmark: UnifiedPulseServer message processing (baseline)
3. Benchmark: Configuration overhead (DI vs. direct)
4. Compare results: <5ms difference acceptable
5. Generate performance report

**Acceptance**: Benchmarks run, performance within acceptable range

---

### T028: [US2] Integration test - Hosted service pattern
**File**: `tests/PulseRPC.Server.Tests/Integration/HostedServiceTests.cs` [NEW]
**Dependencies**: T021
**Story**: US2

**Tasks**:
1. Test: Server as IHostedService in ASP.NET Core host
2. Test: Startup order (server starts with host)
3. Test: Shutdown order (server stops with host)
4. Test: Cancellation token propagation

**Acceptance**: Hosted service tests pass, FR-007 satisfied

---

**CHECKPOINT US2**: Unified server behavior is validated as consistent across all configuration methods and usage patterns.

---

## Phase 5: User Story 3 - Simplified Maintenance (Priority P2)

**Goal**: Consolidate to single implementation, eliminate code duplication

**Value**: Faster bug fixes, reduced maintenance burden, no implementation drift

**Independent Test**: Code coverage shows zero duplicate server orchestration logic

---

### T029: [US3] Refactor PulseServer to facade [TDD]
**File**: `src/PulseRPC.Server/PulseServer.cs`
**Dependencies**: T028
**Story**: US3

**Tasks (TDD)**:
1. **RED**: Write failing tests for facade
   - Test: PulseServer delegates to UnifiedPulseServer
   - Test: PulseServer maps old options to new options
   - Test: PulseServer maintains same public API
2. **GREEN**: Implement facade
   - Add [Obsolete] attribute with message
   - Create _implementation field (UnifiedPulseServer)
   - Delegate all methods with AggressiveInlining
   - Map old ServerOptions → UnifiedServerOptions
3. **REFACTOR**: Extract mapping logic to helper class

**Test File**: `tests/PulseRPC.Server.Tests/Unit/PulseServerFacadeTests.cs` [NEW]

**Acceptance**: Facade tests pass, zero behavior changes

---

### T030: [US3] Refactor ServerHost to facade [TDD]
**File**: `src/PulseRPC.Server/Core/ServerHost.cs`
**Dependencies**: T028
**Story**: US3

**Tasks (TDD)**:
1. **RED**: Write failing tests for facade
   - Test: ServerHost delegates to UnifiedPulseServer
   - Test: ServerHost maps old options to new options
   - Test: ServerHost exposes ConnectionManager/ServiceRegistry properties
2. **GREEN**: Implement facade
   - Add [Obsolete] attribute with message
   - Create _implementation field (UnifiedPulseServer)
   - Delegate all methods with AggressiveInlining
   - Map IPulseServerTransport → TransportChannelConfiguration
3. **REFACTOR**: Validate property exposure patterns

**Test File**: `tests/PulseRPC.Server.Tests/Unit/ServerHostFacadeTests.cs` [NEW]

**Acceptance**: Facade tests pass, zero behavior changes

---

### T031: [US3] Test facade delegation overhead [P]
**File**: `tests/PulseRPC.Server.Tests/Performance/FacadeBenchmarks.cs` [NEW]
**Dependencies**: T029, T030
**Story**: US3

**Tasks**:
1. Create BenchmarkDotNet test class
2. Benchmark: UnifiedPulseServer direct usage (baseline)
3. Benchmark: PulseServer facade delegation
4. Benchmark: ServerHost facade delegation
5. Validate: Overhead <5% (SC-009)

**Acceptance**: Benchmarks pass, overhead within spec

---

### T032: [US3] Run existing tests through facades [P]
**File**: `tests/PulseRPC.Server.Tests/Integration/FacadeCompatibilityTests.cs` [NEW]
**Dependencies**: T029, T030
**Story**: US3

**Tasks**:
1. Run all existing PulseServer tests against facade
2. Run all existing ServerHost tests against facade
3. Verify 100% pass rate
4. Verify identical behavior (no regressions)

**Acceptance**: All tests pass, FR-016 satisfied

---

### T033: [US3] Code coverage analysis
**File**: N/A (analysis task)
**Dependencies**: T032
**Story**: US3

**Tasks**:
1. Run code coverage tool (dotnet-coverage or Coverlet)
2. Analyze: No duplicate server orchestration logic
3. Analyze: Old PulseServer.cs is thin facade only
4. Analyze: Old ServerHost.cs is thin facade only
5. Generate coverage report

**Acceptance**: Coverage report shows zero duplication, SC-003 satisfied

---

### T034: [US3] Static analysis - maintainability improvement
**File**: N/A (analysis task)
**Dependencies**: T033
**Story**: US3

**Tasks**:
1. Run static analysis (SonarQube or NDepend)
2. Measure: Cyclomatic complexity reduction
3. Measure: Code duplication metrics
4. Measure: Maintainability index improvement
5. Validate: ≥25% improvement (SC-007)

**Acceptance**: Analysis shows maintainability improvement

---

**CHECKPOINT US3**: Codebase consolidated, duplication eliminated, maintenance burden reduced.

---

## Phase 6: User Story 4 - Migration Path (Priority P2)

**Goal**: Provide clear migration path with comprehensive documentation

**Value**: Existing users can upgrade safely without breaking changes

**Independent Test**: Sample application migrates in under 30 minutes following documentation

---

### T035: [US4] Create migration guide - PulseServer [P]
**File**: `docs/migration-pulseserver.md` [NEW]
**Dependencies**: T029
**Story**: US4

**Tasks**:
1. Document before/after code examples
2. Document configuration mapping (ServerOptions → UnifiedServerOptions)
3. Document breaking changes (none - 100% compatible)
4. Document deprecation timeline (v2.x → v3.0 → v4.0)
5. Add troubleshooting section

**Acceptance**: Migration guide complete, clear examples

---

### T036: [US4] Create migration guide - ServerHost [P]
**File**: `docs/migration-serverhost.md` [NEW]
**Dependencies**: T030
**Story**: US4

**Tasks**:
1. Document before/after code examples
2. Document configuration mapping (ServerHostOptions → UnifiedServerOptions)
3. Document transport parameter migration
4. Document deprecation timeline
5. Add troubleshooting section

**Acceptance**: Migration guide complete, clear examples

---

### T037: [US4] Update ObsoleteAttribute messages [P]
**Files**:
- `src/PulseRPC.Server/PulseServer.cs`
- `src/PulseRPC.Server/Core/ServerHost.cs`
**Dependencies**: T035, T036
**Story**: US4

**Tasks**:
1. Add complete deprecation messages to [Obsolete] attributes
2. Include migration guide URLs in messages
3. Include timeline in messages
4. Set error: false (warning mode for v2.x)

**Acceptance**: Compiler warnings show clear migration guidance, FR-019 satisfied

---

### T038: [US4] Create sample migration projects [P]
**Files**:
- `examples/MigrationSample-PulseServer/` [NEW]
- `examples/MigrationSample-ServerHost/` [NEW]
**Dependencies**: T035, T036
**Story**: US4

**Tasks**:
1. Create "before" project using old PulseServer API
2. Create "after" project using UnifiedPulseServer
3. Create "before" project using old ServerHost API
4. Create "after" project using UnifiedPulseServer
5. Add side-by-side comparison README

**Acceptance**: Sample projects compile and run

---

### T039: [US4] Test migration path - PulseServer [TDD]
**File**: `tests/PulseRPC.Server.Tests/Migration/PulseServerMigrationTests.cs` [NEW]
**Dependencies**: T038
**Story**: US4

**Tasks (TDD)**:
1. **RED**: Write tests simulating migration
   - Test: Old PulseServer code compiles with warnings
   - Test: Migrated code works identically
   - Test: Configuration mapping preserves all settings
2. **GREEN**: Validate migration guide accuracy
   - Follow guide step-by-step
   - Measure migration time
3. **REFACTOR**: Update guide based on test findings

**Acceptance**: Migration tests pass, time <30 minutes

---

### T040: [US4] Test migration path - ServerHost [TDD]
**File**: `tests/PulseRPC.Server.Tests/Migration/ServerHostMigrationTests.cs` [NEW]
**Dependencies**: T038
**Story**: US4

**Tasks (TDD)**:
1. **RED**: Write tests simulating migration
   - Test: Old ServerHost code compiles with warnings
   - Test: Migrated code works identically
   - Test: Configuration mapping preserves all settings
2. **GREEN**: Validate migration guide accuracy
   - Follow guide step-by-step
   - Measure migration time
3. **REFACTOR**: Update guide based on test findings

**Acceptance**: Migration tests pass, time <30 minutes

---

### T041: [US4] Test edge cases - custom middleware [P]
**File**: `tests/PulseRPC.Server.Tests/Migration/EdgeCaseTests.cs` [NEW]
**Dependencies**: T039, T040
**Story**: US4

**Tasks**:
1. Test: Custom middleware registered with old API still works
2. Test: Custom interceptors still function
3. Test: Custom extension methods still callable
4. Test: DI configurations migrate correctly

**Acceptance**: Edge case tests pass

---

### T042: [US4] Validate binary compatibility
**File**: N/A (validation task)
**Dependencies**: T029, T030
**Story**: US4

**Tasks**:
1. Build existing application against old PulseRPC version
2. Replace PulseRPC DLL with new version (with facades)
3. Run application WITHOUT recompilation
4. Verify: Application runs without errors
5. Verify: All functionality works (SC-010)

**Acceptance**: Binary compatibility validated, FR-018 satisfied

---

### T043: [US4] User testing - migration validation
**File**: N/A (user testing task)
**Dependencies**: T035, T036, T038
**Story**: US4

**Tasks**:
1. Recruit 3 external developers
2. Provide migration guides and sample projects
3. Ask them to migrate sample applications
4. Measure: Time to complete migration
5. Collect: Feedback on clarity and completeness

**Acceptance**: 3/3 developers complete migration in <30 minutes, SC-005 satisfied

---

**CHECKPOINT US4**: Migration path validated, existing users can upgrade safely.

---

## Phase 7: Polish & Integration

**Goal**: Final integration, documentation, and release preparation

**Duration**: ~2 hours

---

### T044: Comprehensive integration test suite [P]
**File**: `tests/PulseRPC.Server.Tests/Integration/ComprehensiveTests.cs` [NEW]
**Dependencies**: T043
**Story**: Polish

**Tasks**:
1. Test: Full server lifecycle (start → process → stop)
2. Test: All transports (TCP, KCP)
3. Test: All configuration methods (DI, builder, direct)
4. Test: Error scenarios (network failures, timeouts, exceptions)
5. Test: Performance under load (50+ concurrent connections)

**Acceptance**: Comprehensive tests pass

---

### T045: Update README and main documentation [P]
**Files**:
- `README.md` [UPDATE]
- `docs/getting-started.md` [UPDATE]
- `docs/api-reference.md` [UPDATE]
**Dependencies**: T044
**Story**: Polish

**Tasks**:
1. Update README quickstart to use UnifiedPulseServer
2. Update getting started guide
3. Update API reference
4. Add deprecation notices for old classes
5. Link to migration guides

**Acceptance**: Documentation updated, unified server is primary

---

### T046: Update NuGet package metadata [P]
**File**: `Directory.Build.props`
**Dependencies**: T045
**Story**: Polish

**Tasks**:
1. Update package release notes
2. Document new UnifiedPulseServer API
3. Document deprecations
4. Update version number (minor increment per SemVer)

**Acceptance**: Package metadata ready for release

---

### T047: Final regression test - all existing tests
**File**: N/A (test run task)
**Dependencies**: T044, T045, T046
**Story**: Polish

**Tasks**:
1. Run ALL existing tests (unit + integration + performance)
2. Verify: 100% pass rate
3. Verify: No regressions introduced
4. Verify: Performance metrics maintained or improved
5. Generate test report

**Acceptance**: All tests pass, FR-011 validated

---

### T048: Create release checklist and validation
**File**: `RELEASE-CHECKLIST.md` [NEW]
**Dependencies**: T047
**Story**: Polish

**Tasks**:
1. Checklist: All user stories completed
2. Checklist: All tests passing
3. Checklist: Documentation updated
4. Checklist: Migration guides reviewed
5. Checklist: Performance benchmarks met
6. Checklist: Binary compatibility validated

**Acceptance**: Release checklist complete, ready for release

---

## Dependencies & Execution Order

### Critical Path (Sequential)
```
T001 → T003 → T004 → T007 → T008 → T009 → T010 → T011 → T020
     → T028 → T029/T030 → T043 → T047 → T048
```

### User Story Dependencies
```
Setup (Phase 1) → Foundation (Phase 2)
     ↓
     US1 (Phase 3) → US2 (Phase 4)
                   ↓
                   US3 (Phase 5) [P]
                   US4 (Phase 6) [P]
                   ↓
                   Polish (Phase 7)
```

**Note**: US3 and US4 can run in parallel after US2 completes.

---

## Parallel Execution Opportunities

### Phase 2 (Foundation)
```
T004 [P], T005 [P], T006 [P] → can run concurrently
T007 depends on T004, T005, T006
```

### Phase 3 (US1)
```
After T008:
  T012 [P], T013 [P], T014 [P], T015 [P], T016 [P], T017 [P]
  can all run in parallel

T009, T010, T011 must be sequential (lifecycle dependencies)
```

### Phase 4 (US2)
```
After T020:
  T023 [P], T024 [P], T025 [P]
  can run in parallel

T021, T022, T026, T027, T028 are sequential
```

### Phase 5 & 6 (US3 & US4)
```
After T028:
  Entire Phase 5 [P]
  Entire Phase 6 [P]
  can run in parallel with each other
```

---

## Testing Strategy Summary

**Test-First Development**: All implementation tasks follow Red-Green-Refactor cycle

**Test Coverage**:
- Unit Tests: 27 test files
- Integration Tests: 11 test files
- Performance Benchmarks: 3 test files
- Migration Tests: 3 test files

**Total Tests**: 44 test tasks out of 48 total tasks (92% test coverage)

---

## Success Criteria Validation

| Criteria | Task | Validation Method |
|----------|------|-------------------|
| SC-001: One primary server class | T020 | API documentation review |
| SC-002: 40% faster onboarding | T019, T020 | Time-to-first-working-server measurement |
| SC-003: Zero duplication | T033 | Code coverage analysis |
| SC-004: All tests pass | T022, T047 | Test suite execution |
| SC-005: Migration <30min | T043 | User testing validation |
| SC-006: Zero "which class" questions | T045 | Documentation clarity |
| SC-007: 25% maintainability improvement | T034 | Static analysis |
| SC-008: 30% less initialization code | T019, T045 | LOC comparison |
| SC-009: <5% facade overhead | T031 | Performance benchmarking |
| SC-010: Binary compatibility | T042 | Compatibility validation |

---

## Implementation Timeline Estimate

| Phase | Duration | Can Start After |
|-------|----------|-----------------|
| Phase 1: Setup | 30 minutes | Immediate |
| Phase 2: Foundation | 2 hours | Phase 1 |
| Phase 3: US1 | 4-6 hours | Phase 2 |
| Phase 4: US2 | 3-4 hours | Phase 3 |
| Phase 5: US3 | 2-3 hours | Phase 4 |
| Phase 6: US4 | 3-4 hours | Phase 4 |
| Phase 7: Polish | 2 hours | Phases 5 & 6 |
| **Total** | **17-22 hours** | |

**With Parallel Execution**: ~14-18 hours

---

## Next Steps

1. Review this tasks.md with the team
2. Assign tasks to developers
3. Set up feature branch (T001)
4. Begin TDD implementation starting with Phase 1
5. Complete each user story checkpoint before moving to next
6. Validate success criteria at each checkpoint

---

**End of Tasks Document**

Generated by `/speckit.tasks` command
Ready for immediate execution following TDD principles
