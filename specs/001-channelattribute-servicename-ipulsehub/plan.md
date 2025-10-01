
# Implementation Plan: ServiceName-Based Thread Scheduling

**Branch**: `001-channelattribute-servicename-ipulsehub` | **Date**: 2025-09-30 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/001-channelattribute-servicename-ipulsehub/spec.md`

## Execution Flow (/plan command scope)
```
1. Load feature spec from Input path
   → If not found: ERROR "No feature spec at {path}"
2. Fill Technical Context (scan for NEEDS CLARIFICATION)
   → Detect Project Type from file system structure or context (web=frontend+backend, mobile=app+api)
   → Set Structure Decision based on project type
3. Fill the Constitution Check section based on the content of the constitution document.
4. Evaluate Constitution Check section below
   → If violations exist: Document in Complexity Tracking
   → If no justification possible: ERROR "Simplify approach first"
   → Update Progress Tracking: Initial Constitution Check
5. Execute Phase 0 → research.md
   → If NEEDS CLARIFICATION remain: ERROR "Resolve unknowns"
6. Execute Phase 1 → contracts, data-model.md, quickstart.md, agent-specific template file (e.g., `CLAUDE.md` for Claude Code, `.github/copilot-instructions.md` for GitHub Copilot, `GEMINI.md` for Gemini CLI, `QWEN.md` for Qwen Code or `AGENTS.md` for opencode).
7. Re-evaluate Constitution Check section
   → If new violations: Refactor design, return to Phase 1
   → Update Progress Tracking: Post-Design Constitution Check
8. Plan Phase 2 → Describe task generation approach (DO NOT create tasks.md)
9. STOP - Ready for /tasks command
```

**IMPORTANT**: The /plan command STOPS at step 7. Phases 2-4 are executed by other commands:
- Phase 2: /tasks command creates tasks.md
- Phase 3-4: Implementation execution (manual or via tools)

## Summary
Implement ServiceName-based thread scheduling for IPulseHub services by extending the ChannelAttribute to specify a ServiceName. The scheduler will ensure all operations for a given ServiceName+ServiceId combination execute sequentially on a dedicated thread, preventing race conditions for stateful services. ServiceId will be injected during authentication and made available in the service context for accurate routing decisions.

## Technical Context
**Language/Version**: C# 11.0+ (.NET 9.0 SDK 9.0.203)
**Primary Dependencies**: System.Threading.Channels, Microsoft.Extensions.DependencyInjection, MemoryPack
**Storage**: N/A (in-memory thread scheduler and connection state)
**Testing**: xUnit, FluentAssertions, NSubstitute, BenchmarkDotNet
**Target Platform**: .NET 9.0+ for server, .NET Standard 2.1 for Unity client compatibility
**Project Type**: Single library project (server-side framework)
**Performance Goals**: P95 < 50ms latency, >100 QPS throughput, >99.5% success rate (per constitution)
**Constraints**: Must use Channel-based message degradation, thread blocking on unavailability, configurable thread pool (initial + max size)
**Scale/Scope**: Support for 100s of ServiceNames, 1000s of ServiceId instances, high-concurrency message dispatch

## Constitution Check
*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Performance-First**: ✅ PASS - Thread-affinity scheduling with System.Threading.Channels designed for high-throughput (>100 QPS). Performance benchmarks will validate P95 < 50ms target.

**Source Generation Over Reflection**: ✅ PASS - ServiceName extraction from ChannelAttribute will be handled by existing PulseRPC.Server.SourceGenerator at compile-time. No runtime reflection in scheduling path.

**Enterprise-Grade Reliability**: ✅ PASS - Design includes Channel-based message degradation for thread overload, blocking behavior with queuing, configurable thread pool sizing, and comprehensive error handling for missing ServiceId.

**Test-Driven Development**: ✅ PASS - Implementation plan includes contract tests, unit tests for scheduler, integration tests for ServiceName+ServiceId routing, and performance benchmarks to validate constitutional requirements.

**Modern .NET Standards**: ✅ PASS - Implementation uses async/await patterns, System.Threading.Channels, nullable reference types, and Microsoft.Extensions.DependencyInjection for scheduler registration.

*Initial assessment: No constitutional violations. Will re-evaluate after Phase 1 design.*

## Project Structure

### Documentation (this feature)
```
specs/[###-feature]/
├── plan.md              # This file (/plan command output)
├── research.md          # Phase 0 output (/plan command)
├── data-model.md        # Phase 1 output (/plan command)
├── quickstart.md        # Phase 1 output (/plan command)
├── contracts/           # Phase 1 output (/plan command)
└── tasks.md             # Phase 2 output (/tasks command - NOT created by /plan)
```

### Source Code (repository root)
```
src/
├── PulseRPC.Abstractions/
│   ├── Attributes.cs                    # Extend ChannelAttribute with ServiceName
│   ├── IPulseHub.cs                     # Base hub interface
│   └── Scheduling/                      # New namespace for scheduling abstractions
│       ├── IServiceScheduler.cs         # Scheduler abstraction
│       ├── IServiceContext.cs           # Context with ServiceId
│       └── ServiceSchedulingKey.cs      # ServiceName+ServiceId composite key
│
├── PulseRPC.Server/
│   ├── Engine/
│   │   └── HighPerformanceMessageEngine.cs  # Integration point for scheduler
│   ├── Scheduling/                      # New namespace for implementation
│   │   ├── ServiceThreadScheduler.cs    # Main scheduler implementation
│   │   ├── ServiceThreadPool.cs         # Thread pool management
│   │   ├── ServiceExecutionContext.cs   # Execution context with ServiceId
│   │   └── SchedulerConfiguration.cs    # Configuration options
│   └── Builder/
│       └── PulseServerBuilder.cs        # DI registration for scheduler
│
└── PulseRPC.Server.SourceGenerator/
    ├── PulseRPCSourceGenerator.cs       # Extract ServiceName from ChannelAttribute
    └── Analyzers/
        └── ServiceAnalyzer.cs           # Validate ServiceName usage

tests/
├── unit/
│   └── PulseRPC.Server.Tests/
│       └── Scheduling/
│           ├── ServiceThreadSchedulerTests.cs
│           ├── ServiceThreadPoolTests.cs
│           └── ServiceSchedulingKeyTests.cs
│
├── integration/
│   └── PulseRPC.IntegrationTests/
│       └── ServiceSchedulingIntegrationTests.cs
│
└── perf/
    └── BenchmarkApp/
        └── SchedulingBenchmarks.cs      # Throughput and latency benchmarks
```

**Structure Decision**: Single project (server framework) with new Scheduling namespace. Changes span PulseRPC.Abstractions (interfaces), PulseRPC.Server (implementation), and PulseRPC.Server.SourceGenerator (compile-time extraction). Follows existing PulseRPC architecture pattern.

## Phase 0: Outline & Research ✅ COMPLETE

**Output**: `research.md` - All technical decisions documented

**Key Research Findings**:
1. **Thread Scheduling**: System.Threading.Channels + dedicated thread pool
2. **ServiceId Injection**: IServiceContext interface with authentication hook
3. **ChannelAttribute Extension**: Add ServiceName property to existing attribute
4. **Thread Pool**: Configurable initial + max size with dynamic scaling
5. **Degradation**: Bounded channels with block/wait + L3 priority-based dropping
6. **Hashing**: Consistent hashing with Murmur3 for thread assignment
7. **Monitoring**: ILogger + performance counters for metrics
8. **Error Handling**: Throw InvalidOperationException for missing ServiceId
9. **Integration**: Inject scheduler after L2 batch processing in HighPerformanceMessageEngine

All unknowns resolved and documented in `research.md`.

## Phase 1: Design & Contracts ✅ COMPLETE

**Output**: `data-model.md`, `contracts/*.cs`, `quickstart.md`, updated `CLAUDE.md`

**Artifacts Generated**:

1. **data-model.md**: Comprehensive entity definitions
   - ServiceSchedulingKey (composite key)
   - IServiceContext (context interface)
   - ServiceThreadScheduler (main scheduler)
   - ServiceThreadPool (thread management)
   - SchedulerConfiguration (options)
   - WorkerThread (execution unit)
   - WorkItem (work encapsulation)
   - Entity relationship diagram and state transitions

2. **contracts/**: Interface contracts (4 files)
   - `IServiceScheduler.cs` - Scheduler abstraction
   - `IServiceContext.cs` - Service context interface
   - `ServiceSchedulingKey.cs` - Composite key value type
   - `SchedulerConfiguration.cs` - Configuration options

3. **quickstart.md**: Developer guide with:
   - Step-by-step usage examples
   - Configuration samples
   - Test scenarios
   - Performance validation
   - Troubleshooting guide

4. **CLAUDE.md**: Updated with feature context
   - Added System.Threading.Channels dependency
   - Added MemoryPack serialization context
   - Preserved existing conventions

**Design Review**: All contracts align with constitutional principles (TDD, performance-first, source generation, modern .NET).

## Phase 2: Task Planning Approach
*This section describes what the /tasks command will do - DO NOT execute during /plan*

**Task Generation Strategy**:

The `/tasks` command will generate implementation tasks following TDD principles:

1. **Contract Test Tasks** (from `contracts/*.cs`):
   - ServiceSchedulingKey equality and hashing tests [P]
   - IServiceContext interface behavior tests [P]
   - SchedulerConfiguration validation tests [P]
   - IServiceScheduler lifecycle and scheduling tests [P]

2. **Entity Implementation Tasks** (from `data-model.md`):
   - Implement ServiceSchedulingKey value type [P]
   - Implement IServiceContext and ServiceExecutionContext [P]
   - Implement SchedulerConfiguration with validation [P]
   - Implement WorkItem and MessagePriority [P]
   - Implement WorkerThread with Channel-based processing [P]
   - Implement ServiceThreadPool with consistent hashing
   - Implement ServiceThreadScheduler with metrics

3. **Integration Tasks** (from `quickstart.md` scenarios):
   - Extend ChannelAttribute with ServiceName property
   - Update PulseRPC.Server.SourceGenerator to extract ServiceName
   - Integrate scheduler into HighPerformanceMessageEngine
   - Add DI registration in PulseServerBuilder
   - Implement authentication hook for ServiceId injection

4. **Test Tasks** (from acceptance scenarios):
   - Test: Same ServiceName+ServiceId executes on same thread
   - Test: Different ServiceIds use different threads
   - Test: Missing ServiceId throws InvalidOperationException
   - Test: Channel full triggers blocking/waiting behavior
   - Test: L3 degradation drops low-priority messages
   - Test: Metrics collection and reporting
   - Integration test: End-to-end scheduling with authentication

5. **Performance Validation Tasks**:
   - BenchmarkDotNet tests for P95 < 50ms latency
   - Throughput benchmarks for >100 QPS
   - Memory allocation benchmarks
   - Success rate validation >99.5%

**Ordering Strategy**:
- TDD: Tests before implementation for each component
- Dependency: Value types → Interfaces → Worker threads → Thread pool → Scheduler
- Parallelization: Mark [P] for independent components (contracts, value types)
- Integration: Scheduler integration after core implementation complete

**Estimated Output**: 30-35 numbered, dependency-ordered tasks in tasks.md

**IMPORTANT**: This phase is executed by the `/tasks` command, NOT by `/plan`

## Phase 3+: Future Implementation
*These phases are beyond the scope of the /plan command*

**Phase 3**: Task execution (/tasks command creates tasks.md)  
**Phase 4**: Implementation (execute tasks.md following constitutional principles)  
**Phase 5**: Validation (run tests, execute quickstart.md, performance validation)

## Complexity Tracking
*Fill ONLY if Constitution Check has violations that must be justified*

No constitutional violations identified. All design decisions align with:
- Performance-First: System.Threading.Channels for high throughput
- Source Generation: Compile-time ServiceName extraction
- Enterprise Reliability: Comprehensive error handling and degradation
- TDD: Contract tests planned before implementation
- Modern .NET: Async/await, nullable types, DI throughout

**No entries required in this section.**


## Progress Tracking
*This checklist is updated during execution flow*

**Phase Status**:
- [x] Phase 0: Research complete (/plan command) ✅
- [x] Phase 1: Design complete (/plan command) ✅
- [x] Phase 2: Task planning complete (/plan command - describe approach only) ✅
- [ ] Phase 3: Tasks generated (/tasks command) - **READY FOR /tasks**
- [ ] Phase 4: Implementation complete
- [ ] Phase 5: Validation passed

**Gate Status**:
- [x] Initial Constitution Check: PASS ✅
- [x] Post-Design Constitution Check: PASS ✅
- [x] All NEEDS CLARIFICATION resolved ✅
- [x] Complexity deviations documented (N/A - no violations) ✅

---
*Based on Constitution v1.0.0 - See `/memory/constitution.md`*
