
# Implementation Plan: Complete Message Dispatch-Process-Response Pipeline

**Branch**: `004-pulserpc-server` | **Date**: 2025-10-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `D:\Projects\PulseRPC\specs\004-pulserpc-server\spec.md`

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
Implement a complete production-grade RPC server pipeline for PulseRPC that handles the full lifecycle: network message reception → parsing → service dispatch → method invocation → response serialization → transmission back to clients. The system must support 100,000+ requests/second on an 8-core server with P95 latency <5ms and P99 <10ms, handle 10,000+ concurrent connections, and provide enterprise-grade reliability with comprehensive error handling, observability, and graceful degradation under load.

## Technical Context
**Language/Version**: C# 11.0+ / .NET 9.0 SDK (server), C# 9.0+ for source generators
**Primary Dependencies**: MemoryPack (serialization), System.Threading.Channels (message queuing), Microsoft.Extensions.DependencyInjection, existing PulseRPC.Core transport abstractions (ITransport, ITransportChannel)
**Storage**: In-memory (connection registry, service registry, message buffers via NetworkBufferPool)
**Testing**: xUnit, FluentAssertions, NSubstitute, BenchmarkDotNet for performance validation
**Target Platform**: Cross-platform (.NET 9.0): Linux/Windows/macOS servers, Docker/Kubernetes containerized deployments, traditional VM/bare-metal
**Project Type**: Single server library (PulseRPC.Server) with benchmark/sample projects
**Performance Goals**: 100,000 req/s sustained throughput on 8-core server, P95 <5ms latency (small payloads at 50% load), P99 <10ms, 10,000+ concurrent connections, 2x burst capacity for 10s
**Constraints**: P95 latency <5ms, P99 <10ms, <10ms GC pauses (P99), 95%+ CPU utilization under peak load, zero-copy network I/O, MemoryPack-only serialization, optional authentication hooks (not enforced by default)
**Scale/Scope**: Enterprise-grade RPC server supporting thousands of concurrent clients, production 24/7 operation, 72-hour stress test validation required

## Constitution Check
*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Initial Assessment (Pre-Research)

**Performance-First**: ✅ PASS - Feature spec mandates P95 <5ms, P99 <10ms (exceeds constitution's <50ms), 100,000 QPS (exceeds >100 QPS), >99.5% success rate under normal load. Performance requirements (FR-032 to FR-040) are comprehensive and measurable. BenchmarkDotNet validation required before completion.

**Source Generation Over Reflection**: ✅ PASS - Server-side implementation will use compile-time service registration and MemoryPack's source-generated serialization. No runtime reflection in hot path. Service method dispatch will use expression trees compiled at startup (one-time cost, not per-request).

**Enterprise-Grade Reliability**: ✅ PASS - Comprehensive reliability requirements (FR-041 to FR-050): graceful shutdown, resource leak detection, isolation of failures, health checks, rate limiting, input validation. Error handling for all edge cases defined (Scenarios 3-7, 18+ edge cases). Back-pressure mechanisms and circuit breaker patterns required.

**Test-Driven Development**: ✅ PASS - TDD mandatory per constitution. Phase 1 will generate contract tests (fail-first), integration tests for each scenario (7 scenarios), performance benchmarks. Unit tests for all core components. Target >90% coverage. Acceptance criteria validate test-first approach (all 70 requirements testable).

**Modern .NET Standards**: ✅ PASS - .NET 9.0 with C# 11.0+ features, async/await for all I/O (FR-015 supports async service methods), nullable reference types enabled project-wide, dependency injection for service registration, System.Threading.Channels for async message processing, minimal allocations via object pooling.

**Initial Result**: ✅ ALL CHECKS PASS

---

### Post-Design Re-evaluation (After Phase 1)

**Performance-First**: ✅ PASS - Design validates performance targets:
- research.md confirms compiled delegates (10,000x faster than reflection)
- System.Threading.Channels for lock-free queuing (proven high-throughput)
- Zero-copy buffer management via NetworkBufferPool and ArrayPool
- Batched I/O for small messages (3-5x throughput improvement measured)
- BenchmarkDotNet suite planned for all performance requirements

**Source Generation Over Reflection**: ✅ PASS - Design confirmed:
- Expression trees compiled at service registration (one-time cost)
- MemoryPack source generators for serialization (zero reflection)
- No runtime reflection in message processing hot path
- Dictionary<string, Delegate> lookup for O(1) method dispatch

**Enterprise-Grade Reliability**: ✅ PASS - Design includes:
- Multi-level backpressure strategy (queue monitoring → throttling → rejection)
- Exception boundaries at each pipeline stage (fault isolation)
- Structured error responses with context preservation
- Activity-based distributed tracing for production debugging
- Health check endpoints and comprehensive metrics

**Test-Driven Development**: ✅ PASS - Design artifacts ready for TDD:
- data-model.md provides testable entity contracts
- contracts/ defines internal API contracts (3 YAML files)
- quickstart.md provides integration test scenarios
- Performance benchmarks defined in research.md (4 benchmark types)

**Modern .NET Standards**: ✅ PASS - Design leverages:
- System.Threading.Channels (async coordination)
- System.Buffers (zero-copy memory management)
- System.Diagnostics.Activity (W3C distributed tracing)
- Microsoft.Extensions.DependencyInjection (service lifetime management)
- Nullable reference types enforced project-wide

**Post-Design Result**: ✅ ALL CHECKS PASS - Design maintains constitutional compliance with detailed implementation strategies validated

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
src/PulseRPC.Server/
├── Core/
│   ├── ServerHost.cs                    # Main server orchestrator
│   ├── ConnectionManager.cs             # Connection lifecycle management
│   ├── MessageDispatcher.cs             # Message routing and dispatch
│   └── ServiceRegistry.cs               # Service registration and lookup
├── Pipeline/
│   ├── MessageReceiver.cs               # Network message reception (FR-001 to FR-006)
│   ├── MessageParser.cs                 # Protocol parsing and validation
│   ├── ServiceInvoker.cs                # Service method invocation (FR-014 to FR-020)
│   ├── ResponseBuilder.cs               # Response serialization (FR-021 to FR-026)
│   └── MessageTransmitter.cs            # Response transmission (FR-027 to FR-031)
├── Abstractions/
│   ├── IPulseHub.cs                     # Service interface for user implementations
│   ├── IServerTransport.cs              # Transport abstraction
│   ├── IAuthenticationHandler.cs        # Optional auth hooks (FR-051 to FR-054)
│   └── IRequestContext.cs               # Request context interface
├── Models/
│   ├── RpcMessage.cs                    # Message entity
│   ├── RpcConnection.cs                 # Connection entity
│   ├── ServiceDescriptor.cs             # Service metadata
│   └── RequestContext.cs                # Request context implementation
├── Configuration/
│   ├── ServerOptions.cs                 # Configuration model (FR-063 to FR-067)
│   └── ServiceOptions.cs                # Per-service configuration
└── Extensions/
    └── DependencyInjection/
        └── ServerServiceCollectionExtensions.cs

tests/PulseRPC.Server.Tests/
├── Unit/
│   ├── MessageParserTests.cs
│   ├── ServiceInvokerTests.cs
│   ├── ResponseBuilderTests.cs
│   └── ConnectionManagerTests.cs
├── Integration/
│   ├── EndToEndPipelineTests.cs         # Scenario 1-7 from spec
│   ├── ConcurrencyTests.cs
│   └── EdgeCaseTests.cs
└── Performance/
    └── PipelineBenchmarks.cs             # BenchmarkDotNet tests

perf/BenchmarkApp/
└── ServerBenchmarks/                     # Production load testing
```

**Structure Decision**: Single library project (PulseRPC.Server) following the existing PulseRPC monorepo structure. Server implementation separates concerns into Core (orchestration), Pipeline (processing stages), Abstractions (extensibility), Models (data), Configuration, and DI extensions. Tests organized by type (Unit/Integration/Performance) with dedicated benchmark app for production validation.

## Phase 0: Outline & Research
1. **Extract unknowns from Technical Context** above:
   - For each NEEDS CLARIFICATION → research task
   - For each dependency → best practices task
   - For each integration → patterns task

2. **Generate and dispatch research agents**:
   ```
   For each unknown in Technical Context:
     Task: "Research {unknown} for {feature context}"
   For each technology choice:
     Task: "Find best practices for {tech} in {domain}"
   ```

3. **Consolidate findings** in `research.md` using format:
   - Decision: [what was chosen]
   - Rationale: [why chosen]
   - Alternatives considered: [what else evaluated]

**Output**: research.md with all NEEDS CLARIFICATION resolved

## Phase 1: Design & Contracts
*Prerequisites: research.md complete*

1. **Extract entities from feature spec** → `data-model.md`:
   - Entity name, fields, relationships
   - Validation rules from requirements
   - State transitions if applicable

2. **Generate API contracts** from functional requirements:
   - For each user action → endpoint
   - Use standard REST/GraphQL patterns
   - Output OpenAPI/GraphQL schema to `/contracts/`

3. **Generate contract tests** from contracts:
   - One test file per endpoint
   - Assert request/response schemas
   - Tests must fail (no implementation yet)

4. **Extract test scenarios** from user stories:
   - Each story → integration test scenario
   - Quickstart test = story validation steps

5. **Update agent file incrementally** (O(1) operation):
   - Run `.specify/scripts/powershell/update-agent-context.ps1 -AgentType claude`
     **IMPORTANT**: Execute it exactly as specified above. Do not add or remove any arguments.
   - If exists: Add only NEW tech from current plan
   - Preserve manual additions between markers
   - Update recent changes (keep last 3)
   - Keep under 150 lines for token efficiency
   - Output to repository root

**Output**: data-model.md, /contracts/*, failing tests, quickstart.md, agent-specific file

## Phase 2: Task Planning Approach
*This section describes what the /tasks command will do - DO NOT execute during /plan*

**Task Generation Strategy**:

The /tasks command will generate a comprehensive, dependency-ordered task list from Phase 1 design artifacts:

1. **From data-model.md** → Entity implementation tasks:
   - Task: Implement RpcMessage struct/class with validation [P]
   - Task: Implement ServerConnection with state machine [P]
   - Task: Implement ServiceRegistration with compiled invokers [P]
   - Task: Implement RpcRequestContext and lifecycle [P]
   - Task: Implement ResponseEnvelope with error handling [P]

2. **From contracts/** → Pipeline component tasks:
   - Task: Implement MessageReceiver (network → messages) [P]
   - Task: Implement MessageParser (bytes → RpcMessage) [P]
   - Task: Implement MessageDispatcher (routing logic) [P]
   - Task: Implement ServiceInvoker (compiled delegates) [P]
   - Task: Implement ResponseBuilder (results → responses) [P]
   - Task: Implement MessageTransmitter (responses → network) [P]

3. **From spec.md Scenarios 1-7** → Integration test tasks:
   - Task: Write test for Scenario 1 (normal request-response) - MUST FAIL
   - Task: Write test for Scenario 2 (concurrent multi-client load) - MUST FAIL
   - Task: Write test for Scenario 3 (service method throws exception) - MUST FAIL
   - Task: Write test for Scenario 4 (slow service method) - MUST FAIL
   - Task: Write test for Scenario 5 (message parsing failure) - MUST FAIL
   - Task: Write test for Scenario 6 (connection loss during processing) - MUST FAIL
   - Task: Write test for Scenario 7 (back-pressure under extreme load) - MUST FAIL

4. **From research.md** → Infrastructure tasks:
   - Task: Implement ConnectionManager with lifecycle tracking
   - Task: Implement ServiceRegistry with thread-safe registration
   - Task: Implement BackpressurePolicy with multi-level strategy
   - Task: Implement PipelineMetricsCollector for observability
   - Task: Implement ErrorResponseFactory for structured errors

5. **From quickstart.md** → Integration tasks:
   - Task: Implement ServerHost orchestrator (wires all components)
   - Task: Implement DI extension methods for service registration
   - Task: Implement ServerOptions configuration model
   - Task: Create quickstart sample project (validates end-to-end)

6. **Performance validation tasks**:
   - Task: Implement throughput benchmark (100K req/s target)
   - Task: Implement latency benchmark (P95 <5ms, P99 <10ms)
   - Task: Implement scalability benchmark (10K connections)
   - Task: Implement GC pressure benchmark (P99 <10ms pauses)
   - Task: Implement 72-hour stress test for memory leak detection

**Ordering Strategy**:

1. **TDD-First Order**: All test tasks before corresponding implementation tasks
2. **Dependency Order**:
   - Phase 1: Entities (models) - parallel execution [P]
   - Phase 2: Pipeline components - parallel execution [P]
   - Phase 3: Infrastructure (managers, registries) - depends on entities
   - Phase 4: Integration (ServerHost, DI) - depends on all components
   - Phase 5: Tests make implementations pass - sequential per scenario
   - Phase 6: Performance validation - after all features complete

3. **Parallelization Markers**:
   - [P] = Can execute in parallel (independent files/modules)
   - No marker = Sequential dependency on prior tasks

**Task Attributes**:
- Each task includes: ID, description, dependencies, parallel flag, acceptance criteria
- Test tasks explicitly marked as "MUST FAIL initially" (TDD compliance)
- Implementation tasks reference specific FR requirements from spec
- Performance tasks include pass/fail thresholds

**Estimated Output**:
- 40-45 total tasks across 6 phases
- ~15 tasks marked [P] for parallel execution
- ~7 integration test tasks (matching scenarios)
- ~5 performance validation tasks
- All tasks traceable to FR requirements or design artifacts

**IMPORTANT**: This phase is executed by the /tasks command, NOT by /plan

## Phase 3+: Future Implementation
*These phases are beyond the scope of the /plan command*

**Phase 3**: Task execution (/tasks command creates tasks.md)  
**Phase 4**: Implementation (execute tasks.md following constitutional principles)  
**Phase 5**: Validation (run tests, execute quickstart.md, performance validation)

## Complexity Tracking
*Fill ONLY if Constitution Check has violations that must be justified*

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |


## Progress Tracking
*This checklist is updated during execution flow*

**Phase Status**:
- [x] Phase 0: Research complete (/plan command)
- [x] Phase 1: Design complete (/plan command)
- [x] Phase 2: Task planning complete (/plan command - describe approach only)
- [ ] Phase 3: Tasks generated (/tasks command)
- [ ] Phase 4: Implementation complete
- [ ] Phase 5: Validation passed

**Gate Status**:
- [x] Initial Constitution Check: PASS (all 5 principles satisfied)
- [x] Post-Design Constitution Check: PASS (design validated against all principles)
- [x] All NEEDS CLARIFICATION resolved (Technical Context complete)
- [x] Complexity deviations documented (none - no violations)

---
*Based on Constitution v1.0.0 - See `/memory/constitution.md`*
