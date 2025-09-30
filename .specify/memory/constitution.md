<!--
Sync Impact Report
==================
Version change: NEW → 1.0.0
Modified principles: Initial adoption
Added sections: All core principles and governance
Removed sections: None
Templates requiring updates:
  ✅ plan-template.md - Updated constitution version reference and Constitution Check section
  ✅ spec-template.md - No changes needed, no constitution dependencies
  ✅ tasks-template.md - No changes needed, no constitution dependencies
  ✅ agent-file-template.md - No changes needed, no constitution dependencies
Follow-up TODOs: None
-->

# PulseRPC Constitution

## Core Principles

### I. Performance-First
Every feature MUST meet or exceed documented performance targets. Components MUST be designed for high-throughput, low-latency scenarios with specific measurable goals: <50ms P95 latency, >100 QPS throughput, >99.5% success rate. Performance regressions are blocking issues that require immediate resolution.

Rationale: PulseRPC is designed for enterprise gaming and microservice scenarios where performance is critical to user experience and system reliability.

### II. Source Generation Over Reflection
Client implementations MUST avoid reflection and use Source Generator-based code generation for type safety and performance. All client proxies, serialization, and service discovery MUST be compile-time generated. Runtime reflection is forbidden in client code paths.

Rationale: Zero-reflection design ensures predictable performance, reduces memory allocation, and enables AOT compilation for Unity and enterprise deployments.

### III. Enterprise-Grade Reliability
All network operations MUST implement comprehensive error handling, retry policies, circuit breakers, and health checks. Connection management MUST support automatic reconnection, load balancing, and graceful degradation. System MUST be resilient to network failures and service disruptions.

Rationale: Enterprise and gaming environments require 24/7 availability with automatic recovery from transient failures.

### IV. Test-Driven Development (NON-NEGOTIABLE)
TDD is mandatory: Tests written → Implementation → Tests pass. Every feature MUST have unit tests, integration tests, and contract tests before implementation. Test coverage MUST be >90% for core components. Performance benchmarks MUST be automated and verified.

Rationale: High-reliability systems require comprehensive testing to prevent regressions and ensure consistent behavior across diverse deployment scenarios.

### V. Modern .NET Standards
All code MUST use modern C# language features: async/await patterns, nullable reference types, records, minimal APIs, and dependency injection. Code MUST target latest LTS .NET versions with backward compatibility for Unity. Source generators MUST support C# 9.0+ syntax.

Rationale: Modern language features improve code safety, maintainability, and developer productivity while ensuring compatibility with contemporary .NET ecosystems.

## Technical Standards

### Technology Stack Requirements
- **.NET 9.0+ SDK** for development, .NET Standard 2.1+ for Unity compatibility
- **MemoryPack** for high-performance serialization
- **xUnit, FluentAssertions, NSubstitute** for testing framework
- **BenchmarkDotNet** for performance measurement
- **Source Generators** for compile-time code generation
- **Microsoft.Extensions.*** for dependency injection and configuration

### Performance Standards
All components MUST meet baseline performance requirements verified through automated benchmarks:
- RPC latency: P95 < 50ms, P99 < 100ms
- Throughput: >100 QPS sustained load
- Memory allocation: Minimal per-request allocations via object pooling
- Success rate: >99.5% under normal conditions

### Code Quality Requirements
- Nullable reference types enabled project-wide
- PublicAPI.*.txt files maintained for breaking change detection
- XML documentation for all public APIs
- EditorConfig and analyzer rules enforced in CI/CD

## Development Workflow

### Test-First Implementation
1. Write failing tests that capture requirements
2. Implement minimal code to pass tests
3. Refactor with tests remaining green
4. Performance benchmarks validate non-functional requirements
5. Code review ensures constitutional compliance

### Review Process
All changes MUST pass:
- Automated test suite (unit, integration, performance)
- Code review focusing on constitutional compliance
- Static analysis and security scanning
- Documentation updates for breaking changes

### Quality Gates
- No code committed without passing tests
- Performance regressions block deployment
- Breaking changes require explicit approval and migration guide
- Security vulnerabilities treated as critical priority

## Governance

Constitution supersedes all other development practices and guidelines. All feature development, architectural decisions, and code review processes MUST verify compliance with constitutional principles.

Complexity that violates principles MUST be justified with specific technical rationale. When constitutional principles conflict, prioritize in order: Test-Driven Development, Performance-First, Enterprise-Grade Reliability, Source Generation, Modern .NET Standards.

Amendment procedure requires documentation of proposed changes, technical justification, impact assessment, and approval from project maintainers. All amendments MUST include migration plan for existing code.

Runtime development guidance is provided in `CLAUDE.md` and project-specific documentation. Constitutional violations in legacy code MUST be addressed during refactoring but do not block new feature development.

**Version**: 1.0.0 | **Ratified**: 2025-09-30 | **Last Amended**: 2025-09-30