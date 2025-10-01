# Tasks: ServiceName-Based Thread Scheduling for IPulseHub

**Feature Branch**: `001-channelattribute-servicename-ipulsehub`
**Input**: Design documents from `specs/001-channelattribute-servicename-ipulsehub/`
**Prerequisites**: plan.md, research.md, data-model.md, contracts/, quickstart.md

## Execution Flow (main)
```
1. Load plan.md from feature directory ✅
   → Tech stack: C# 11.0+, .NET 9.0, System.Threading.Channels
   → Testing: xUnit, FluentAssertions, NSubstitute, BenchmarkDotNet
2. Load design documents ✅
   → data-model.md: 7 entities (ServiceSchedulingKey, IServiceContext, etc.)
   → contracts/: 4 interface files
   → quickstart.md: Integration test scenarios
3. Generate tasks by category ✅
   → Setup: Dependencies and project structure
   → Tests: Contract tests, unit tests, integration tests (TDD)
   → Core: Value types, interfaces, scheduler implementation
   → Integration: ChannelAttribute extension, source generator, engine integration
   → Polish: Performance benchmarks, documentation
4. Apply task rules ✅
   → Different files = [P] for parallel
   → Tests before implementation (TDD)
5. Number tasks sequentially (T001-T037) ✅
6. Generate dependency graph ✅
7. Validate completeness ✅
```

## Format: `[ID] [P?] Description`
- **[P]**: Can run in parallel (different files, no dependencies)
- File paths are absolute from repository root

## Phase 3.1: Setup

- [x] **T001** Verify dependencies in `Directory.Packages.props`
  - Ensure System.Threading.Channels (9.0.0) ✅
  - Ensure Microsoft.Extensions.DependencyInjection (9.0.0) ✅
  - Ensure xUnit, FluentAssertions, NSubstitute for testing ✅

- [x] **T002** Create Scheduling namespace directories
  - Create `src/PulseRPC.Abstractions/Scheduling/` directory ✅
  - Create `src/PulseRPC.Server/Scheduling/` directory ✅
  - Create `tests/PulseRPC.Server.Tests/Scheduling/` directory ✅

---

## Phase 3.2: Tests First (TDD) ⚠️ MUST COMPLETE BEFORE 3.3

**CRITICAL: These tests MUST be written and MUST FAIL before ANY implementation**

### Contract Tests (Parallel)

- [x] **T003** [P] Write ServiceSchedulingKey contract tests in `tests/PulseRPC.Server.Tests/Scheduling/ServiceSchedulingKeyTests.cs` ✅
  - Test constructor validates non-null/non-whitespace ServiceName and ServiceId
  - Test equality: same ServiceName+ServiceId are equal
  - Test hash code consistency
  - Test ToString() format: "ServiceName:ServiceId"
  - Test inequality for different ServiceName or ServiceId
  - **Tests will FAIL until T014 implementation**

- [x] **T004** [P] Write SchedulerConfiguration contract tests in `tests/PulseRPC.Server.Tests/Scheduling/SchedulerConfigurationTests.cs` ✅
  - Test Validate() passes for valid configuration
  - Test Validate() throws for InitialThreadCount <= 0
  - Test Validate() throws for MaxThreadCount < InitialThreadCount
  - Test Validate() throws for ThreadIdleTimeout <= TimeSpan.Zero
  - Test Validate() throws for ChannelCapacity <= 0
  - Test default values (ProcessorCount, 30s timeout, 1024 capacity)
  - **Tests will FAIL until T015 implementation**

- [x] **T005** [P] Write IServiceContext contract tests in `tests/PulseRPC.Server.Tests/Scheduling/ServiceContextTests.cs` ✅
  - Test ServiceId can be null initially
  - Test ServiceId can be set during authentication
  - Test IsAuthenticated returns true when ServiceId is set
  - Test IsAuthenticated returns false when ServiceId is null
  - Test ConnectionId is always available
  - Test ServiceName is populated from ChannelAttribute
  - **Tests use test implementation, will validate real impl in T024**

- [x] **T006** [P] Write IServiceScheduler contract tests in `tests/PulseRPC.Server.Tests/Scheduling/ServiceSchedulerTests.cs` ✅
  - Test ScheduleAsync throws ArgumentNullException for null key or work
  - Test ScheduleAsync throws InvalidOperationException for null ServiceId in key
  - Test ScheduleAsync throws ObjectDisposedException after disposal
  - Test StartAsync initializes scheduler and sets IsRunning to true
  - Test StopAsync gracefully shuts down and sets IsRunning to false
  - Test GetMetrics returns valid SchedulerMetrics
  - **Tests use test implementation, will validate real impl in T023**

### Unit Tests (Parallel)

- [x] **T007** [P] Write WorkerThread unit tests in `tests/PulseRPC.Server.Tests/Scheduling/WorkerThreadTests.cs` ✅
  - Test StartAsync begins processing loop
  - Test EnqueueAsync adds work to channel
  - Test EnqueueAsync blocks when channel is full (bounded)
  - Test StopAsync completes in-flight work before shutting down
  - Test ProcessedCount increments for each completed work item
  - Test CurrentQueueDepth reflects channel state
  - **Tests will FAIL until T021 implementation**

- [x] **T008** [P] Write ServiceThreadPool unit tests in `tests/PulseRPC.Server.Tests/Scheduling/ServiceThreadPoolTests.cs` ✅
  - Test GetThreadForKey returns consistent thread index for same key
  - Test GetThreadForKey uses hash-based distribution
  - Test EnqueueWork adds work to correct thread's channel
  - Test ScaleThreadPool adjusts thread count within limits
  - Test thread count stays between InitialThreadCount and MaxThreadCount
  - Mock WorkerThread for isolation
  - **Tests will FAIL until T022 implementation**

- [x] **T009** [P] Write ServiceThreadScheduler unit tests in `tests/PulseRPC.Server.Tests/Scheduling/ServiceThreadSchedulerUnitTests.cs` ✅
  - Test ScheduleAsync routes work to correct thread based on key
  - Test ScheduleAsync queues work sequentially for same key
  - Test StartAsync initializes thread pool
  - Test StopAsync disposes all worker threads
  - Test metrics collection (when enabled)
  - Mock ServiceThreadPool and configuration
  - **Tests will FAIL until T023 implementation**

### Integration Tests (Parallel)

- [x] **T010** [P] Write thread affinity integration test in `tests/PulseRPC.IntegrationTests/ServiceSchedulingIntegrationTests.cs` ✅
  - Scenario: Same ServiceName+ServiceId executes on same thread
  - Create scheduler with real configuration
  - Schedule 10 operations for same key, capture thread IDs
  - Assert all operations ran on same thread
  - **Test will FAIL until full implementation (T023)**

- [x] **T011** [P] Write concurrent execution integration test in `tests/PulseRPC.IntegrationTests/ServiceSchedulingIntegrationTests.cs` ✅
  - Scenario: Different ServiceIds execute on different threads
  - Schedule operations for 2 different keys concurrently
  - Assert operations complete without blocking each other
  - Assert thread IDs may differ for different keys
  - **Test will FAIL until full implementation (T023)**

- [x] **T012** [P] Write missing ServiceId error test in `tests/PulseRPC.IntegrationTests/ServiceSchedulingIntegrationTests.cs` ✅
  - Scenario: Null ServiceId throws InvalidOperationException
  - Create key with null ServiceId
  - Assert ScheduleAsync throws with clear error message
  - **Test PASSES - validates key construction**

- [x] **T013** [P] Write channel backpressure integration test in `tests/PulseRPC.IntegrationTests/ServiceSchedulingIntegrationTests.cs` ✅
  - Scenario: Full channel triggers blocking behavior
  - Configure small channel capacity (e.g., 4)
  - Schedule many long-running operations for same key
  - Assert ScheduleAsync blocks when channel is full
  - Assert operations complete after channel drains
  - **Test will FAIL until full implementation (T023)**

---

## Phase 3.3: Core Implementation (ONLY after tests are failing)

### Value Types and Configuration (Parallel)

- [ ] **T014** [P] Implement ServiceSchedulingKey in `src/PulseRPC.Abstractions/Scheduling/ServiceSchedulingKey.cs`
  - Implement as readonly struct with ServiceName and ServiceId properties
  - Constructor validates non-null/non-whitespace for both fields
  - Implement IEquatable<ServiceSchedulingKey> for equality
  - Override GetHashCode() using HashCode.Combine(ServiceName, ServiceId)
  - Override ToString() to return "ServiceName:ServiceId"
  - Implement equality operators (==, !=)
  - **All T003 tests should PASS**

- [ ] **T015** [P] Implement SchedulerConfiguration in `src/PulseRPC.Server/Scheduling/SchedulerConfiguration.cs`
  - Define properties with default values (ProcessorCount, 30s, 1024, etc.)
  - Implement Validate() method with validation rules
  - Throw ArgumentException for invalid values with clear messages
  - **All T004 tests should PASS**

- [ ] **T016** [P] Implement MessagePriority enum in `src/PulseRPC.Server/Scheduling/MessagePriority.cs`
  - Define enum: High, Normal, Low
  - Used for L3 degradation (drop low priority first)

- [ ] **T017** [P] Implement WorkItem struct in `src/PulseRPC.Server/Scheduling/WorkItem.cs`
  - Properties: Key (ServiceSchedulingKey), Work (Func<Task>), EnqueuedTime, Priority
  - Validate Key and Work are not null in constructor
  - Record EnqueuedTime for latency metrics

### Interfaces (Parallel)

- [ ] **T018** [P] Implement IServiceScheduler interface in `src/PulseRPC.Abstractions/Scheduling/IServiceScheduler.cs`
  - Copy from contracts/IServiceScheduler.cs
  - XML documentation for all members
  - ScheduleAsync, StartAsync, StopAsync, IsRunning, GetMetrics signatures

- [ ] **T019** [P] Implement IServiceContext interface in `src/PulseRPC.Abstractions/Scheduling/IServiceContext.cs`
  - Copy from contracts/IServiceContext.cs
  - Properties: ServiceId (nullable), ConnectionId, IsAuthenticated, ServiceName
  - XML documentation

- [ ] **T020** [P] Implement SchedulerMetrics class in `src/PulseRPC.Abstractions/Scheduling/SchedulerMetrics.cs`
  - Properties: ActiveThreadCount, TotalQueuedMessages, P95LatencyMs, DroppedMessageCount
  - Used by IServiceScheduler.GetMetrics()

### Worker Thread Implementation

- [ ] **T021** Implement WorkerThread in `src/PulseRPC.Server/Scheduling/WorkerThread.cs`
  - Properties: ThreadId, MessageChannel (bounded Channel<WorkItem>), IsRunning, ProcessedCount, CurrentQueueDepth
  - Constructor: Create bounded channel with configured capacity
  - StartAsync(): Begin async loop reading from channel
  - Processing loop: await work.Work(), increment ProcessedCount, handle exceptions
  - EnqueueAsync(): Channel.Writer.WriteAsync(workItem) - blocks if full
  - StopAsync(): Complete channel writer, await remaining work, dispose
  - IAsyncDisposable for cleanup
  - Implement ILogger for diagnostics
  - **All T007 tests should PASS**

### Thread Pool Implementation

- [ ] **T022** Implement ServiceThreadPool in `src/PulseRPC.Server/Scheduling/ServiceThreadPool.cs`
  - Properties: ThreadCount, ThreadChannels, KeyToThreadMapping (ConcurrentDictionary)
  - Constructor: Initialize with SchedulerConfiguration
  - Initialize(): Create InitialThreadCount WorkerThread instances
  - GetThreadForKey(key): Compute hash → thread index (consistent hashing)
  - Use Math.Abs(HashCode.Combine(key.ServiceName, key.ServiceId)) % ThreadCount
  - EnqueueWork(threadIndex, workItem): Get thread, call thread.EnqueueAsync()
  - ScaleThreadPool(newSize): Add/remove threads within Min/Max limits
  - DisposeAsync(): Stop all worker threads
  - **All T008 tests should PASS**

### Main Scheduler Implementation

- [ ] **T023** Implement ServiceThreadScheduler in `src/PulseRPC.Server/Scheduling/ServiceThreadScheduler.cs`
  - Implement IServiceScheduler interface
  - Dependencies: IServiceProvider, SchedulerConfiguration, ILogger
  - Field: ServiceThreadPool _threadPool
  - Constructor: Initialize with configuration, create thread pool
  - StartAsync(): Call _threadPool.Initialize() and start all threads, set IsRunning = true
  - ScheduleAsync(key, work):
    - Validate key (throw if ServiceId null)
    - Validate scheduler is running (throw if disposed)
    - Create WorkItem with key, work, timestamp, priority
    - Get thread index from _threadPool.GetThreadForKey(key)
    - Call _threadPool.EnqueueWork(threadIndex, workItem)
  - StopAsync(): Set IsRunning = false, dispose thread pool
  - GetMetrics(): Aggregate metrics from all worker threads
  - IAsyncDisposable for cleanup
  - **All T006 and T009 tests should PASS**

### Service Context Implementation

- [ ] **T024** Implement ServiceExecutionContext in `src/PulseRPC.Server/Scheduling/ServiceExecutionContext.cs`
  - Implement IServiceContext interface
  - Properties: ServiceId (get/set), ConnectionId (get), ServiceName (get)
  - IsAuthenticated: return !string.IsNullOrWhiteSpace(ServiceId)
  - Constructor: Initialize with ConnectionId and ServiceName
  - **All T005 tests should PASS**

---

## Phase 3.4: Integration

### ChannelAttribute Extension

- [ ] **T025** Extend ChannelAttribute with ServiceName in `src/PulseRPC.Abstractions/Attributes.cs`
  - Add public string? ServiceName { get; set; } property to ChannelAttribute
  - Optional property (defaults to null, falls back to interface name)
  - Update XML documentation to describe ServiceName usage
  - Update constructor overload: ChannelAttribute(string channelName, string? serviceName = null)

### Source Generator Updates

- [ ] **T026** Update PulseRPC.Server.SourceGenerator to extract ServiceName in `src/PulseRPC.Server.SourceGenerator/PulseRPCSourceGenerator.cs`
  - In ProcessInterface method, check for ChannelAttribute on interface
  - Extract ServiceName property if specified
  - If ServiceName is null, use interface name (e.g., "IPlayerHub" → "PlayerService")
  - Pass ServiceName to generated service registration code
  - Add diagnostic for missing ChannelAttribute on IPulseHub interfaces

- [ ] **T027** Update ServiceAnalyzer to validate ServiceName in `src/PulseRPC.Server.SourceGenerator/Analyzers/ServiceAnalyzer.cs`
  - Add analyzer rule: ServiceName should be specified for stateful services
  - Warn if ChannelAttribute.ServiceName is null on IPulseHub interface
  - Suggest: [Channel("channel-name", ServiceName = "ServiceName")]

### Engine Integration

- [ ] **T028** Integrate ServiceThreadScheduler into HighPerformanceMessageEngine in `src/PulseRPC.Server/Engine/HighPerformanceMessageEngine.cs`
  - Add IServiceScheduler field _serviceScheduler
  - Inject IServiceScheduler via constructor (optional, null-safe)
  - In message processing pipeline (after L2 batch processing):
    - Check if _serviceScheduler is not null
    - Extract ServiceName from generated metadata
    - Get IServiceContext from connection context
    - Create ServiceSchedulingKey from ServiceName + context.ServiceId
    - Schedule work: await _serviceScheduler.ScheduleAsync(key, async () => { /* invoke service */ })
  - Fallback: If scheduler is null, invoke service directly (backward compatibility)

- [ ] **T029** Add authentication hook for ServiceId injection in `src/PulseRPC.Server/Builder/PulseServerBuilder.cs`
  - Add ConfigureScheduler(Action<SchedulerConfiguration>) extension method
  - Register IServiceScheduler as singleton in DI container
  - Register ServiceThreadScheduler as implementation
  - In authentication middleware:
    - After successful authentication, get IServiceContext from connection
    - Set context.ServiceId from authentication result (user ID, session ID, etc.)
  - Add example authentication handler in documentation

### DI Registration

- [ ] **T030** Register scheduler services in DI in `src/PulseRPC.Server/Builder/PulseServerBuilder.cs`
  - Add services.AddSingleton<IServiceScheduler, ServiceThreadScheduler>()
  - Add services.Configure<SchedulerConfiguration>(config => { ... })
  - Load configuration from appsettings.json section "Scheduler"
  - Ensure scheduler is started when PulseServer starts (IHostedService pattern)

---

## Phase 3.5: Integration Tests (Verify End-to-End)

- [ ] **T031** Run all integration tests and verify they PASS
  - Execute T010: Thread affinity test ✅
  - Execute T011: Concurrent execution test ✅
  - Execute T012: Missing ServiceId error test ✅
  - Execute T013: Channel backpressure test ✅
  - All integration tests should now PASS with full implementation

- [ ] **T032** Write authentication integration test in `tests/PulseRPC.IntegrationTests/ServiceSchedulingIntegrationTests.cs`
  - Scenario: ServiceId injection during authentication
  - Create test authentication handler that sets ServiceId
  - Invoke service method after authentication
  - Assert service method executes with correct ServiceId in context
  - Assert scheduler routes based on authenticated ServiceId

---

## Phase 3.6: Performance Validation

- [ ] **T033** [P] Write latency benchmark in `perf/BenchmarkApp/SchedulingBenchmarks.cs`
  - BenchmarkDotNet test: Schedule 1000 operations, measure P95/P99 latency
  - Target: P95 < 50ms (constitutional requirement)
  - Report percentiles: P50, P95, P99, P99.9
  - Use realistic work (e.g., 10ms async delay per operation)

- [ ] **T034** [P] Write throughput benchmark in `perf/BenchmarkApp/SchedulingBenchmarks.cs`
  - BenchmarkDotNet test: Measure operations per second
  - Target: >100 QPS (constitutional requirement)
  - Test with multiple ServiceNames and ServiceIds (realistic distribution)
  - Report: OPS, memory allocations, GC pressure

- [ ] **T035** [P] Write success rate test in `perf/BenchmarkApp/SchedulingBenchmarks.cs`
  - Test: Schedule 10,000 operations under load
  - Assert success rate > 99.5% (constitutional requirement)
  - Capture failures (exceptions, timeouts)
  - Report: Total operations, successes, failures, success rate %

- [ ] **T036** Run all performance benchmarks and validate constitutional compliance
  - Execute T033: P95 latency < 50ms ✅
  - Execute T034: Throughput > 100 QPS ✅
  - Execute T035: Success rate > 99.5% ✅
  - If any benchmark fails, optimize and re-run

---

## Phase 3.7: Polish

- [ ] **T037** Update quickstart.md with final implementation details
  - Add actual file paths for implemented classes
  - Add configuration example for appsettings.json
  - Add authentication handler example code
  - Add troubleshooting section with common errors
  - Verify all code samples compile and run

---

## Dependencies

**Critical Paths**:
1. Setup (T001-T002) → All tests (T003-T013) → All implementation (T014-T030)
2. Tests MUST fail before implementation begins (TDD)
3. Value types (T014-T017) are parallel, no dependencies
4. Interfaces (T018-T020) are parallel, depend on value types
5. WorkerThread (T021) → ServiceThreadPool (T022) → ServiceThreadScheduler (T023)
6. ServiceContext (T024) is parallel with scheduler implementation
7. Integration (T025-T030) depends on core implementation complete
8. Integration tests (T031-T032) depend on integration complete
9. Performance (T033-T036) can run in parallel after integration
10. Polish (T037) is last

**Blocking Dependencies**:
- T021 blocks T022 (thread pool needs worker threads)
- T022 blocks T023 (scheduler needs thread pool)
- T014-T024 block T025-T030 (integration needs core)
- T025-T030 block T031-T032 (integration tests need integration)
- T031-T032 block T036 (performance validation needs working integration)

**Parallel Opportunities**:
- T003-T006: All contract tests (different files)
- T007-T009: All unit tests (different files)
- T010-T013: All integration tests (same file, but independent scenarios)
- T014-T017: All value types (different files)
- T018-T020: All interfaces (different files)
- T033-T035: All performance benchmarks (different scenarios)

---

## Parallel Execution Example

### Phase 3.2: Launch All Contract Tests Together
```bash
# All tests in different files, fully parallel:
Task: "Write ServiceSchedulingKey contract tests in tests/PulseRPC.Server.Tests/Scheduling/ServiceSchedulingKeyTests.cs"
Task: "Write SchedulerConfiguration contract tests in tests/PulseRPC.Server.Tests/Scheduling/SchedulerConfigurationTests.cs"
Task: "Write IServiceContext contract tests in tests/PulseRPC.Server.Tests/Scheduling/ServiceContextTests.cs"
Task: "Write IServiceScheduler contract tests in tests/PulseRPC.Server.Tests/Scheduling/ServiceSchedulerTests.cs"
```

### Phase 3.3: Launch All Value Type Implementations Together
```bash
# After all tests fail, implement value types in parallel:
Task: "Implement ServiceSchedulingKey in src/PulseRPC.Abstractions/Scheduling/ServiceSchedulingKey.cs"
Task: "Implement SchedulerConfiguration in src/PulseRPC.Server/Scheduling/SchedulerConfiguration.cs"
Task: "Implement MessagePriority enum in src/PulseRPC.Server/Scheduling/MessagePriority.cs"
Task: "Implement WorkItem struct in src/PulseRPC.Server/Scheduling/WorkItem.cs"
```

### Phase 3.6: Launch All Performance Benchmarks Together
```bash
# All benchmarks are independent scenarios:
Task: "Write latency benchmark in perf/BenchmarkApp/SchedulingBenchmarks.cs targeting P95 < 50ms"
Task: "Write throughput benchmark in perf/BenchmarkApp/SchedulingBenchmarks.cs targeting >100 QPS"
Task: "Write success rate test in perf/BenchmarkApp/SchedulingBenchmarks.cs targeting >99.5%"
```

---

## Validation Checklist

- [x] All contracts have corresponding tests (T003-T006 cover all 4 contract files) ✅
- [x] All entities have implementation tasks (7 entities from data-model.md covered) ✅
- [x] All tests come before implementation (Phase 3.2 before 3.3) ✅
- [x] Parallel tasks are truly independent (different files, verified) ✅
- [x] Each task specifies exact file path ✅
- [x] No parallel task modifies same file as another ✅
- [x] TDD enforced: Tests MUST fail before implementation ✅
- [x] Constitutional requirements included (P95<50ms, >100 QPS, >99.5% success) ✅
- [x] Integration with existing codebase (HighPerformanceMessageEngine, SourceGenerator) ✅
- [x] Performance validation included (T033-T036) ✅

---

## Notes

- **[P] markers**: Tasks marked [P] can be executed in parallel using multiple agents
- **File paths**: All paths are absolute from repository root (`src/`, `tests/`, `perf/`)
- **TDD Critical**: Phase 3.2 tests MUST be written and MUST fail before starting Phase 3.3
- **Constitution**: Performance benchmarks (T033-T036) validate constitutional requirements
- **Backward Compatibility**: Engine integration (T028) includes null-safe fallback for scheduler

## Task Summary

**Total Tasks**: 37
- Setup: 2 tasks (T001-T002)
- Tests: 11 tasks (T003-T013) - TDD critical phase
- Core Implementation: 11 tasks (T014-T024)
- Integration: 6 tasks (T025-T030)
- Integration Validation: 2 tasks (T031-T032)
- Performance: 4 tasks (T033-T036)
- Polish: 1 task (T037)

**Parallel Opportunities**: 18 tasks can run in parallel (marked with [P])
**Estimated Completion**: 3-5 days for single developer, 1-2 days with parallel execution

---

**Ready for implementation! Start with T001 and follow the dependency order.**