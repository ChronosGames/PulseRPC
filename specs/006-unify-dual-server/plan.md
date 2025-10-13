# Implementation Plan: Unified Server Implementation

**Branch**: `006-unify-dual-server` | **Date**: 2025-10-13 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/006-unify-dual-server/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

Consolidate the dual server implementations (PulseServer and ServerHost) into a single unified PulseServer class. The current PulseServer will be completely rewritten to incorporate the best architectural patterns from both implementations, adopting a **transport-focused orchestration model** (managing transports, listeners, channels as primary concern) while ServerHost becomes a deprecated facade delegating to the new unified implementation. This eliminates API confusion, reduces maintenance burden, and provides a single clear entry point for developers while maintaining 100% binary compatibility through the ServerHost facade pattern.

## Technical Context

**Language/Version**: C# 11.0+ / .NET 9.0.203
**Primary Dependencies**:
- Microsoft.Extensions.DependencyInjection (DI container integration)
- Microsoft.Extensions.Hosting (IHostedService pattern support)
- Microsoft.Extensions.Logging (logging abstraction)
- MemoryPack (high-performance serialization)
- PulseRPC.Abstractions (internal abstractions)
- PulseRPC.Transport (TCP/KCP transport implementations)

**Storage**: N/A (in-memory state management only)
**Testing**: xUnit, FluentAssertions, NSubstitute, BenchmarkDotNet
**Target Platform**: .NET 9.0+, cross-platform (Windows, Linux, macOS, Docker)
**Project Type**: Library (RPC framework server component)
**Performance Goals**:
- Message throughput: >100 QPS (queries per second) per connection
- P95 latency: <50ms for local network
- P99 latency: <100ms for local network
- ServerHost facade delegation overhead: <5% measured by message throughput
- Graceful shutdown: complete within 30 seconds (default timeout, configurable)

**Constraints**:
- MUST maintain 100% binary compatibility for existing PulseServer users
- MUST support all existing ServerHost functionality through facade delegation
- MUST NOT break existing DI registration patterns
- MUST NOT regress performance compared to current implementations
- MUST pass all existing integration tests without modification
- C# 11.0 language features available (required for Server.SourceGenerator compatibility)

**Scale/Scope**:
- Single public server class (PulseServer) + deprecated facade (ServerHost)
- ~10-15 internal component classes (lifecycle, transport management, pipeline coordination)
- Existing ~50+ integration tests must pass unchanged
- ~5-10 builder API methods for configuration
- Support for 100+ concurrent connections per server instance

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Status**: ✅ PASS (No project constitution defined - using default .NET best practices)

The project does not have a formal constitution file (`.specify/memory/constitution.md` contains only template placeholders). The implementation will follow standard .NET library development practices:

- **API Design**: Follow .NET Framework Design Guidelines (public API consistency, fluent builders, async/await patterns)
- **Testing**: Maintain existing xUnit test coverage; add tests for new unified implementation
- **Performance**: BenchmarkDotNet validation of facade overhead (<5% threshold from spec)
- **Binary Compatibility**: Strict adherence - no breaking changes to existing public APIs
- **Code Quality**: Enable nullable reference types, XML documentation, PublicAPI analyzers

**Re-evaluation Note**: After Phase 1 design completion, verify that the unified architecture maintains:
1. Transport-focused orchestration aligns with existing PulseServer patterns
2. Facade delegation maintains zero behavioral changes for ServerHost users
3. Builder API extensions preserve existing service registration patterns

## Project Structure

### Documentation (this feature)

```
specs/006-unify-dual-server/
├── spec.md              # Feature specification (completed)
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (architecture analysis, migration patterns)
├── data-model.md        # Phase 1 output (unified server internal model)
├── quickstart.md        # Phase 1 output (developer migration guide)
├── contracts/           # Phase 1 output (public API contracts)
│   ├── IPulseServer.cs      # Unified server interface
│   ├── ServerConfiguration.cs  # Consolidated configuration
│   └── DeprecationPatterns.md  # Facade implementation patterns
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```
src/PulseRPC.Server/
├── PulseServer.cs                    # ✅ EXISTS - Will be REWRITTEN as unified implementation
├── Core/
│   ├── ServerHost.cs                 # ✅ EXISTS - Will become DEPRECATED FACADE
│   ├── UnifiedServerCore.cs          # 🆕 NEW - Core unified server orchestration logic
│   ├── ServerLifecycleCoordinator.cs # 🆕 NEW - Manages server state transitions (Starting/Running/Stopping/Stopped)
│   └── TransportOrchestrator.cs      # 🆕 NEW - Manages multiple transport lifecycle (TCP/KCP listeners)
├── Configuration/
│   ├── ServerOptions.cs              # ✅ EXISTS - Will be ENHANCED with unified configuration
│   ├── ServerConfiguration.cs        # 🆕 NEW - Consolidated configuration object (merges PulseServer + ServerHost options)
│   └── ShutdownOptions.cs            # 🆕 NEW - Graceful shutdown configuration (30s default timeout)
├── Builder/
│   ├── IPulseServerBuilder.cs        # ✅ EXISTS - Interface remains, implementation enhanced
│   ├── PulseServerBuilder.cs         # ✅ EXISTS - Will target NEW unified PulseServer
│   └── ServerHostBuilderAdapter.cs   # 🆕 NEW - Adapter for ServerHost facade construction
├── Integration/
│   ├── TransportIntegrationManager.cs # ✅ EXISTS - Manages transport providers (TCP/KCP)
│   └── ITransportProvider.cs          # ✅ EXISTS - Transport abstraction
├── Pipeline/
│   ├── MessageReceiver.cs            # ✅ EXISTS - Used by ServerHost, will integrate into unified model
│   ├── MessageDispatcher.cs          # ✅ EXISTS - Message dispatching logic
│   ├── ResponseTransmitter.cs        # ✅ EXISTS - Response transmission
│   └── PipelineCoordinator.cs        # 🆕 NEW - Coordinates pipeline components in unified architecture
├── Channels/
│   └── IServerChannelManager.cs      # ✅ EXISTS - Channel (connection) management abstraction
├── Extensions/
│   ├── ServiceCollectionExtensions.cs # ✅ EXISTS - DI registration, will support unified PulseServer
│   └── HostedServiceAdapter.cs        # 🆕 NEW - IHostedService wrapper for ASP.NET Core integration
└── Observability/
    └── UnifiedServerMetrics.cs        # 🆕 NEW - Performance metrics (connections, throughput, latency)

tests/PulseRPC.Server.Tests/
├── Unit/
│   ├── UnifiedServerLifecycleTests.cs  # 🆕 NEW - Unit tests for lifecycle coordinator
│   ├── TransportOrchestratorTests.cs   # 🆕 NEW - Unit tests for transport management
│   └── ServerHostFacadeTests.cs        # 🆕 NEW - Verify facade delegates correctly
├── Integration/
│   ├── UnifiedServerIntegrationTests.cs # 🆕 NEW - End-to-end unified server tests
│   ├── BinaryCompatibilityTests.cs      # 🆕 NEW - Verify existing tests pass unchanged
│   └── MigrationSmokeTests.cs           # 🆕 NEW - Verify migration scenarios work
└── Performance/
    └── FacadeDelegationBenchmark.cs     # 🆕 NEW - BenchmarkDotNet: measure <5% overhead

perf/BenchmarkApp/
└── PulseRPC.Benchmark.Server/
    └── [NO CHANGES] - Existing benchmarks will be run against unified implementation
```

**Structure Decision**:

This is a **library refactoring project** within an existing multi-project solution. The structure follows the existing PulseRPC.Server layout with minimal new files:

- **Unified Core**: New internal classes (`UnifiedServerCore`, `ServerLifecycleCoordinator`, `TransportOrchestrator`) encapsulate the consolidated orchestration logic
- **Facade Pattern**: `ServerHost` becomes a thin delegation wrapper to the unified `PulseServer`
- **Configuration Consolidation**: New `ServerConfiguration` class merges options from both implementations
- **Pipeline Integration**: Existing pipeline components (`MessageReceiver`, `MessageDispatcher`, `ResponseTransmitter`) are integrated into the unified model via new `PipelineCoordinator`
- **Binary Compatibility**: Existing public APIs (builders, extensions) maintained, implementations redirected to unified core
- **Testing Strategy**: New tests validate unified behavior + facade correctness; existing integration tests run unchanged to verify compatibility

The rewritten `PulseServer` will adopt the **transport-focused orchestration** approach (as clarified in spec), making it the authoritative server implementation while ServerHost provides backward compatibility through delegation.

## Complexity Tracking

*Fill ONLY if Constitution Check has violations that must be justified*

**Not Applicable** - No constitution violations. This is a refactoring/consolidation project that reduces overall complexity by eliminating dual implementations while maintaining API compatibility through standard facade pattern.

---

## Phase 0: Research & Analysis (Prerequisites Resolution)

**Objective**: Analyze both existing implementations, identify integration patterns, and resolve architectural decisions.

### Research Tasks

1. **Architecture Comparison**
   - **Task**: Deep-dive analysis of PulseServer vs ServerHost architectures
   - **Questions to Answer**:
     - What are the core differences in orchestration models? (transport-focused vs pipeline-focused)
     - Which pipeline components are shared vs duplicated?
     - How does each handle lifecycle management (start/stop/shutdown)?
     - What are the dependency injection integration patterns?
   - **Deliverable**: Comparative architecture diagram + decision matrix

2. **Functionality Inventory**
   - **Task**: Catalog all public API methods from both PulseServer and ServerHost
   - **Questions to Answer**:
     - What functionality exists in PulseServer but not ServerHost (and vice versa)?
     - Which methods are semantically equivalent but differently named?
     - Are there any ServerHost-exclusive features that must be preserved?
   - **Deliverable**: API compatibility matrix + feature gap analysis

3. **Integration Points Analysis**
   - **Task**: Identify all external integration points (builders, DI, hosted services)
   - **Questions to Answer**:
     - How are services registered in DI containers for each implementation?
     - How does builder API differ between PulseServerBuilder and any ServerHost builder?
     - What are the IHostedService integration patterns?
   - **Deliverable**: Integration pattern document + migration mapping

4. **Test Coverage Analysis**
   - **Task**: Survey existing test suites for both implementations
   - **Questions to Answer**:
     - Which integration tests target PulseServer specifically?
     - Which tests target ServerHost specifically?
     - What test scenarios must pass unchanged to validate binary compatibility?
   - **Deliverable**: Test inventory + compatibility test selection criteria

5. **Performance Baseline Establishment**
   - **Task**: Run existing benchmarks to establish baseline metrics
   - **Questions to Answer**:
     - What is current PulseServer message throughput (msgs/sec)?
     - What is current ServerHost message throughput?
     - What overhead is acceptable for facade delegation (spec: <5%)?
   - **Deliverable**: Performance baseline report (BenchmarkDotNet results)

6. **Facade Delegation Pattern Research**
   - **Task**: Research best practices for zero-overhead delegation in .NET
   - **Questions to Answer**:
     - How to implement minimal-overhead method delegation in C#?
     - Should ServerHost be a thin wrapper or adapter pattern?
     - How to deprecate APIs gracefully (ObsoleteAttribute patterns)?
   - **Deliverable**: Delegation pattern recommendations + deprecation strategy

7. **Graceful Shutdown Patterns**
   - **Task**: Research .NET best practices for graceful server shutdown
   - **Questions to Answer**:
     - How to implement timeout-based shutdown (30s default from clarification)?
     - How to coordinate shutdown across multiple listeners/transports?
     - How to handle in-flight requests during shutdown?
   - **Deliverable**: Shutdown workflow design + cancellation token patterns

8. **Builder API Consolidation Strategy**
   - **Task**: Design unified builder API that supports all configuration scenarios
   - **Questions to Answer**:
     - How to merge PulseServerBuilder functionality with ServerHost options?
     - How to maintain fluent API ergonomics?
     - How to support both direct instantiation and DI-based construction?
   - **Deliverable**: Unified builder API design document

### Research Output

**File**: `research.md`

**Structure**:
```markdown
# Unified Server Implementation Research

## Architecture Analysis
[Comparative diagrams, orchestration model comparison]

## API Compatibility Matrix
[Full method inventory, compatibility mappings]

## Integration Patterns
[DI registration, builder patterns, hosted service integration]

## Test Compatibility Plan
[Test selection, expected pass rates, compatibility validation approach]

## Performance Baseline
[Current metrics, facade overhead budget, benchmark methodology]

## Design Decisions
### Decision: Transport-Focused Orchestration
- Rationale: [Why chosen from clarification]
- Implementation approach: [How to refactor current PulseServer]
- Pipeline integration: [How MessageReceiver/Dispatcher/Transmitter fit]

### Decision: Facade Delegation Pattern
- Rationale: [Zero breaking changes, gradual migration]
- Implementation approach: [Thin wrapper vs adapter]
- Performance considerations: [Overhead mitigation]

### Decision: Graceful Shutdown Strategy
- Rationale: [30s default timeout, Kubernetes-aligned]
- Implementation approach: [Cancellation token coordination]
- Timeout behavior: [Force shutdown after timeout]

## Migration Patterns
[Common migration scenarios, before/after code examples]

## Alternatives Considered
[Other architectural approaches evaluated and rejected]
```

---

## Phase 1: Design & Contracts

**Prerequisites**: `research.md` complete, all architectural decisions finalized

### Design Tasks

1. **Unified Server Internal Model**
   - **Task**: Design the internal architecture of the rewritten PulseServer
   - **Components to Define**:
     - `UnifiedServerCore`: Central orchestration logic
     - `ServerLifecycleCoordinator`: State machine (Stopped → Starting → Running → Stopping → Stopped)
     - `TransportOrchestrator`: Multi-transport management (TCP/KCP listeners)
     - `PipelineCoordinator`: Integrates MessageReceiver/Dispatcher/Transmitter
   - **Deliverable**: `data-model.md` with class diagrams, state machines, component interactions

2. **ServerHost Facade Specification**
   - **Task**: Define how ServerHost delegates to unified PulseServer
   - **Decisions**:
     - Which ServerHost methods map directly to PulseServer methods?
     - How to handle ServerHost-specific options (conversion to ServerConfiguration)?
     - Deprecation message wording (ObsoleteAttribute text)
   - **Deliverable**: `contracts/ServerHostFacade.md` with delegation mappings

3. **Configuration Consolidation**
   - **Task**: Design unified `ServerConfiguration` class
   - **Merge**: `ServerOptions` (PulseServer) + `ServerHostOptions` (ServerHost)
   - **Fields**: All transport configs, pipeline options, shutdown timeout, DI settings
   - **Deliverable**: `contracts/ServerConfiguration.cs` (C# interface contract)

4. **Public API Contracts**
   - **Task**: Define the final public API surface of unified PulseServer
   - **Methods**:
     - `StartAsync(CancellationToken)`: Start server (30s grace period on cancellation)
     - `StopAsync(CancellationToken)`: Graceful stop (30s default timeout)
     - `AddTransport(TransportChannelConfiguration)`: Register transport
     - `GetTransports()`: Query active transports
     - `GetActiveConnections()`: Query connection state
     - `GetPerformanceMetrics()`: Retrieve metrics
   - **Deliverable**: `contracts/IPulseServer.cs` (C# interface contract)

5. **Builder API Design**
   - **Task**: Finalize unified `PulseServerBuilder` API
   - **Methods**:
     - `.AddTcpTransport(port, options)`: Configure TCP listener
     - `.AddKcpTransport(port, options)`: Configure KCP listener
     - `.WithShutdownTimeout(TimeSpan)`: Override default 30s timeout
     - `.WithServiceDiscovery(...)`: Service discovery integration (if applicable)
     - `.Build()`: Construct unified PulseServer
   - **Deliverable**: `contracts/PulseServerBuilder.cs` (C# interface contract)

6. **Migration Guide (Quickstart)**
   - **Task**: Write step-by-step migration guide for existing users
   - **Scenarios**:
     - PulseServer user (minimal changes - API remains mostly compatible)
     - ServerHost user (update to PulseServer, handle deprecation warnings)
     - Custom extension method users (guidance from clarification: must rewrite)
     - DI registration users (verify ServiceCollectionExtensions still work)
   - **Deliverable**: `quickstart.md` with before/after code samples

7. **Deprecation Strategy Document**
   - **Task**: Specify deprecation messages and migration timeline
   - **Details**:
     - `[Obsolete("ServerHost is deprecated. Use PulseServer instead. See quickstart.md for migration guide.", false)]`
     - Guidance message in ObsoleteAttribute pointing to documentation
     - No removal timeline (retain facade indefinitely for maximum compatibility)
   - **Deliverable**: `contracts/DeprecationPatterns.md`

### Design Output

**Files**:
- `data-model.md`: Internal architecture, class diagrams, state machines
- `quickstart.md`: Developer-facing migration guide (before/after code)
- `contracts/IPulseServer.cs`: Public unified server interface
- `contracts/ServerConfiguration.cs`: Consolidated configuration class
- `contracts/PulseServerBuilder.cs`: Builder API specification
- `contracts/ServerHostFacade.md`: Facade delegation mappings
- `contracts/DeprecationPatterns.md`: Deprecation message templates

**Data Model Structure** (`data-model.md`):
```markdown
# Unified Server Internal Model

## Architecture Overview
[High-level component diagram showing UnifiedServerCore, Lifecycle, Transport, Pipeline coordinators]

## Core Components

### UnifiedServerCore
- **Responsibility**: Central orchestration, owns all coordinators
- **State**: Delegates to ServerLifecycleCoordinator
- **Transports**: Delegates to TransportOrchestrator
- **Pipeline**: Delegates to PipelineCoordinator
- **Public Methods**: Thin wrappers exposing coordinator functionality

### ServerLifecycleCoordinator
- **State Machine**:
  - States: Stopped → Starting → Running → Stopping → Stopped
  - Transitions: Start request → validate → coordinate → Running
  - Shutdown: Running → Stopping → wait (30s timeout) → Stopped
- **Responsibilities**: State management, event emission (StateChanged events)

### TransportOrchestrator
- **Transport Management**:
  - Owns `ConcurrentDictionary<string, IServerListener>` (TCP/KCP listeners)
  - Parallel start/stop of multiple transports
  - Connection acceptance routing to channel manager
- **Lifecycle Integration**: Start/stop coordinated by LifecycleCoordinator

### PipelineCoordinator
- **Pipeline Components**:
  - MessageReceiver (from ServerHost pattern)
  - MessageDispatcher (from ServerHost pattern)
  - ResponseTransmitter (from ServerHost pattern)
- **Coordination**: Wires message flow events, backpressure integration
- **Integration**: Connects transport layer to service dispatch layer

## ServerHost Facade Model
- **Pattern**: Thin wrapper, direct delegation
- **Constructor**: Takes same options, converts to ServerConfiguration, constructs PulseServer
- **Methods**: All public methods delegate to internal `_unifiedServer` field
- **Deprecation**: `[Obsolete]` attribute on class declaration

## Configuration Model
- **ServerConfiguration**: Merged properties from ServerOptions + ServerHostOptions
- **ShutdownOptions**: Timeout (default 30s), force shutdown behavior
- **TransportConfigurations**: List<TransportChannelConfiguration>

## Lifecycle Workflow
[Sequence diagrams for Start, Stop, Shutdown with timeout]

## Performance Considerations
- Zero-allocation delegation paths where possible
- Async/await throughout (no blocking calls)
- Concurrent transport start/stop for fast startup
- Graceful shutdown respects timeout (force after 30s default)
```

**Agent Context Update**:
After generating design artifacts, run:
```powershell
.specify/scripts/powershell/update-agent-context.ps1 -AgentType claude
```
This will update `.claude/claude_project.json` (or equivalent) with new unified server architecture context.

---

## Phase 2: Task Generation

**Prerequisites**: Phase 1 complete, all design artifacts generated and reviewed

**Note**: Phase 2 is executed by the `/speckit.tasks` command, **NOT** by `/speckit.plan`.

This planning document ends here. After reviewing `plan.md`, `research.md`, `data-model.md`, `quickstart.md`, and `contracts/`, the user should run:

```bash
/speckit.tasks
```

The `tasks.md` file will contain dependency-ordered implementation tasks generated from the design artifacts above, including:

- Task breakdown (e.g., "Implement UnifiedServerCore", "Write ServerHost facade", "Migrate tests")
- Dependencies (e.g., "UnifiedServerCore must exist before ServerHost facade")
- Acceptance criteria (e.g., "All existing integration tests pass unchanged")
- Test-first approach (write tests before implementation per each task)

---

## Constitution Check Re-evaluation (Post-Design)

**Status**: ✅ PASS

**Verification**:
1. ✅ **Transport-focused orchestration**: Design confirms unified PulseServer adopts transport-first model (TransportOrchestrator as primary concern), aligning with existing PulseServer patterns and clarification decision
2. ✅ **Facade delegation maintains zero behavioral changes**: ServerHost facade design shows direct method delegation with no logic changes, only type conversion (ServerHostOptions → ServerConfiguration)
3. ✅ **Builder API preserves existing patterns**: Unified PulseServerBuilder maintains fluent API style, existing service registration patterns unchanged in ServiceCollectionExtensions

**Design Alignment**: The unified architecture successfully consolidates both implementations while respecting:
- PulseServer's transport-focused philosophy
- ServerHost's pipeline coordination patterns (integrated via PipelineCoordinator)
- Binary compatibility requirements (facade pattern with zero breaking changes)
- Performance constraints (delegation overhead measured via BenchmarkDotNet, <5% threshold)

**Recommendation**: Proceed to `/speckit.tasks` for implementation task generation.

---

## Next Steps

1. **Review This Plan**: Ensure all stakeholders agree with the architecture and migration strategy
2. **Execute `/speckit.tasks`**: Generate implementation tasks from design artifacts
3. **Begin Implementation**: Follow test-first approach (write tests → implement → validate)
4. **Performance Validation**: Run BenchmarkDotNet suite to verify <5% facade overhead
5. **Migration Testing**: Validate existing integration tests pass 100% unchanged

**Estimated Effort**:
- Research Phase (Phase 0): 2-3 days (architecture analysis, test inventory)
- Design Phase (Phase 1): 2-3 days (contracts, data model, migration guide)
- Implementation (Phase 2, post-`/speckit.tasks`): 5-7 days (unified core, facade, tests)
- **Total**: ~10-15 days for complete implementation and validation
