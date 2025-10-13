# Feature Specification: Unified Server Implementation

**Feature Branch**: `006-unify-dual-server`
**Created**: 2025-10-13
**Status**: Draft
**Input**: User description: "双核心服务器类冗余 (高严重性)，期望采用单一的服务器实现"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Clear API Surface (Priority: P1)

As a developer integrating PulseRPC into my application, I need a single, clear entry point for creating and configuring servers, so that I can quickly understand the API without confusion about which server class to use.

**Why this priority**: This is the core value proposition - eliminating architectural confusion that currently exists with PulseServer and ServerHost. Developers should have one obvious way to create servers, following the "principle of least surprise". This directly impacts developer experience and adoption.

**Independent Test**: Can be fully tested by examining the public API surface - there should be exactly one public server class. A new developer should be able to create a working server in under 5 minutes following documentation. Delivers immediate value by reducing onboarding time and API complexity.

**Acceptance Scenarios**:

1. **Given** API documentation, **When** searching for how to create a server, **Then** exactly one server class is documented as the public entry point
2. **Given** a new project, **When** adding PulseRPC package reference, **Then** IntelliSense shows one primary server class for instantiation
3. **Given** existing code examples, **When** reviewing sample code, **Then** all examples use the same unified server class

---

### User Story 2 - Consistent Behavior (Priority: P1)

As a PulseRPC user, I need the server to behave consistently regardless of how it's configured, so that I don't encounter unexpected behavior differences between different initialization paths.

**Why this priority**: The current dual implementation may lead to subtle behavioral differences depending on which server class is used. This can cause hard-to-debug production issues. Unified implementation ensures consistent behavior across all use cases.

**Independent Test**: Can be fully tested by running the same integration test suite against the unified implementation that currently runs against both server variants. All tests should pass with identical behavior. Delivers value by preventing production surprises.

**Acceptance Scenarios**:

1. **Given** a server configured via DI container, **When** processing messages, **Then** behavior matches server configured via builder API
2. **Given** identical server configuration, **When** measuring performance metrics, **Then** results are consistent across multiple runs
3. **Given** existing integration tests, **When** running against unified implementation, **Then** 100% of tests pass without modification

---

### User Story 3 - Simplified Maintenance (Priority: P2)

As a PulseRPC maintainer, I need to maintain only one server implementation, so that bug fixes and features don't need to be implemented twice, reducing maintenance burden and preventing implementation drift.

**Why this priority**: This is primarily a maintainer concern rather than end-user value. However, it indirectly benefits users through faster bug fixes and feature delivery. Secondary to getting the API right for users.

**Independent Test**: Can be fully tested by measuring code coverage - there should be no duplicated server lifecycle logic. Code review should show clear separation of concerns with no redundant implementations. Delivers value through reduced technical debt.

**Acceptance Scenarios**:

1. **Given** a bug fix for server lifecycle, **When** implementing the fix, **Then** code change is required in only one location
2. **Given** the unified codebase, **When** running code coverage analysis, **Then** no duplicate server orchestration logic exists
3. **Given** a new feature request, **When** estimating implementation time, **Then** estimates decrease by at least 30% compared to dual implementation

---

### User Story 4 - Migration Path (Priority: P2)

As a developer with existing PulseRPC applications, I need a clear migration path to the unified implementation, so that I can upgrade to the new version without breaking my application.

**Why this priority**: Breaking existing applications would prevent adoption of the improvement. While important, this is secondary to getting the unified design right. A well-designed API may justify breaking changes in a major version.

**Independent Test**: Can be fully tested by following migration documentation with a sample v1 application. Migration should be completable in under 30 minutes with clear error messages for deprecated APIs. Delivers value by enabling safe upgrades.

**Acceptance Scenarios**:

1. **Given** an application using the previous server class, **When** upgrading to the unified version, **Then** clear deprecation warnings or compiler errors guide migration
2. **Given** migration documentation, **When** following step-by-step instructions, **Then** migration completes in under 30 minutes
3. **Given** a migrated application, **When** running tests, **Then** all functionality works as before

---

### Edge Cases

- What happens when users have custom middleware or interceptors registered with the old implementation?
- How are dependency injection configurations migrated when the service registration changes?
- What occurs when hosted service patterns differ between old and new implementations?
- How are error messages and logging maintained during migration to avoid breaking monitoring?
- Custom extension methods targeting specific implementation types will break and require rewriting to target the new unified PulseServer implementation
- How do we handle configuration objects that may have been specific to one implementation?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide exactly one public server class as the primary API entry point
- **FR-002**: Unified server MUST support all functionality currently provided by both existing implementations
- **FR-003**: Server MUST support lifecycle management (start, stop, restart, status monitoring)
- **FR-004**: Server MUST support multiple transport configurations (TCP, KCP, custom transports)
- **FR-005**: Server MUST integrate with dependency injection containers
- **FR-006**: Server MUST provide fluent builder API for configuration
- **FR-007**: Server MUST support hosted service pattern for ASP.NET Core integration
- **FR-008**: Server MUST maintain backward compatibility for service registration patterns
- **FR-009**: Server MUST provide consistent error handling and logging across all operations
- **FR-010**: Server MUST support graceful shutdown with configurable timeout (default: 30 seconds, aligning with Kubernetes pod termination grace period)
- **FR-011**: Unified implementation MUST pass all existing integration tests without modification
- **FR-012**: Server MUST provide clear separation between high-level orchestration and low-level pipeline management
- **FR-013**: Server MUST support observable metrics and health checks
- **FR-014**: Migration path MUST be documented with clear before/after code examples
- **FR-015**: ServerHost MUST remain as deprecated facade with ObsoleteAttribute warnings; PulseServer becomes the unified implementation
- **FR-016**: ServerHost facade MUST delegate all operations to unified PulseServer implementation with zero behavior changes
- **FR-017**: Unified server MUST use transport-focused architecture (managing transports, listeners, channels as primary concern)
- **FR-018**: All existing public APIs MUST maintain 100% binary compatibility (no breaking changes)
- **FR-019**: Deprecation warnings MUST include clear migration guidance pointing to unified API
- **FR-020**: Migration documentation MUST explicitly note that custom extension methods targeting old implementation types need rewriting for the unified PulseServer

### Key Entities

- **PulseServer**: The single public server class that orchestrates all server functionality (unified implementation)
- **ServerHost**: Deprecated facade class that delegates to PulseServer (marked with ObsoleteAttribute)
- **ServerConfiguration**: Consolidated configuration object containing all server settings
- **TransportManager**: Internal component managing transport layer integrations
- **PipelineOrchestrator**: Internal component coordinating message processing pipeline
- **LifecycleCoordinator**: Internal component managing server state transitions

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Public API surface reduces to exactly one primary server class (measured by public type count in API documentation)
- **SC-002**: Developer onboarding time decreases by at least 40% (measured by time-to-first-working-server in user studies)
- **SC-003**: Code duplication in server orchestration logic reduces to zero (measured by code coverage analysis)
- **SC-004**: All existing integration tests pass without modification (100% test compatibility)
- **SC-005**: Migration documentation is validated by at least 3 external developers completing migration in under 30 minutes
- **SC-006**: GitHub issues and Stack Overflow questions about "which server class to use" decrease to zero
- **SC-007**: Code maintainability score improves by at least 25% (measured by static analysis tools like NDepend or SonarQube)
- **SC-008**: Server initialization code reduces by at least 30% in common use cases (lines of code comparison)
- **SC-009**: ServerHost facade delegation overhead is under 5% compared to direct PulseServer usage, measured by message throughput (messages/second) across representative workload using BenchmarkDotNet
- **SC-010**: Existing applications can upgrade without any code changes (100% binary compatibility validated)

## Scope & Boundaries *(mandatory)*

### In Scope

- Designing unified server architecture that consolidates PulseServer and ServerHost functionality
- Creating single public API entry point with clear, consistent behavior
- Implementing internal separation of concerns (orchestration vs pipeline management)
- Maintaining backward compatibility for service registration and configuration
- Migrating existing integration tests to unified implementation
- Providing comprehensive migration documentation
- Deprecating old server classes with clear migration path
- Ensuring DI container integration works identically
- Maintaining hosted service pattern support

### Out of Scope

- Changes to transport layer implementations (TCP/KCP listeners remain unchanged)
- Modifications to message dispatching logic (dispatcher implementations unchanged)
- Changes to serialization layer (MemoryPack usage unchanged)
- Client-side API changes
- Performance optimizations beyond what consolidation naturally provides
- Changes to authentication or authorization mechanisms
- Modifications to monitoring and observability infrastructure

### Success Indicators

- Single, clear API that developers can find immediately
- No confusion in documentation or examples about which class to use
- Faster issue resolution due to simpler codebase
- Improved code coverage due to elimination of duplicate code paths
- Positive developer feedback on API clarity

### Failure Indicators

- Developers still confused about how to initialize servers
- Test failures indicating behavioral inconsistencies
- Migration requires more than 30 minutes for typical applications
- Performance regression in common scenarios
- Increased bug reports after consolidation

## Assumptions & Constraints *(mandatory)*

### Assumptions

- Both current implementations (PulseServer and ServerHost) provide equivalent functionality with different orchestration approaches
- Most users interact through builder API or DI container, not directly constructing server classes
- Existing tests adequately cover all server functionality
- Facade pattern can provide zero-overhead delegation to unified implementation
- Users will gradually migrate to unified API when they see deprecation warnings in build output
- Transport-focused architecture can accommodate all current ServerHost pipeline coordination needs
- No critical functionality is locked into ServerHost's pipeline-first approach that can't be replicated

### Technical Constraints

- Must maintain .NET 8+ compatibility (current project target)
- Must support dependency injection patterns (Microsoft.Extensions.DependencyInjection)
- Must integrate with hosted service infrastructure (IHostedService)
- Cannot break existing message processing semantics
- Must maintain thread-safety guarantees
- Must preserve graceful shutdown behavior

### Business Constraints

- Changes MUST NOT require users to modify any existing code (100% binary compatibility)
- Migration documentation must be ready before release to encourage adoption of new API
- Deprecation warnings guide users toward best practices without forcing immediate changes
- This is a minor version update (no breaking changes per semantic versioning)
- All functionality from both PulseServer and ServerHost must remain accessible through facades
- Performance must not regress due to facade delegation overhead

## Dependencies *(mandatory)*

### Internal Dependencies

- PulseRPC.Server.Builder: Builder API may need updates to construct unified server
- PulseRPC.Server.Extensions: DI extension methods need to register unified server
- PulseRPC.Server.Integration: Transport integration must work with unified implementation
- PulseRPC.Server.Channels: Channel management integration remains unchanged
- PulseRPC.Server.Dispatch: Message dispatching integration remains unchanged

### External Dependencies

- Microsoft.Extensions.DependencyInjection: For DI container integration
- Microsoft.Extensions.Hosting: For hosted service pattern support
- Microsoft.Extensions.Logging: For consistent logging across unified implementation

### Risk Assessment

- **High Risk (Mitigated)**: Breaking existing applications - MITIGATED by maintaining 100% binary compatibility through facades
- **Medium Risk**: Subtle behavioral differences in facade delegation causing production issues - requires thorough integration testing
- **Medium Risk**: Performance overhead from facade pattern delegation - needs benchmarking to ensure zero or minimal impact
- **Low Risk**: Users may ignore deprecation warnings and never migrate to unified API
- **Low Risk**: Documentation may not cover all migration scenarios initially
- **Low Risk**: Third-party extensions depending on internal APIs may need guidance for migration

## Clarifications

### Session 2025-10-13

- Q: What should the public unified server class be named? → A: Keep PulseServer name for the unified implementation
- Q: ServerHost Facade Implementation Strategy - what happens to the current PulseServer implementation? → A: Delete current PulseServer entirely, rewrite as unified implementation from scratch
- Q: What should the default graceful shutdown timeout be? → A: 30 seconds
- Q: How should custom extension methods built on top of the old API be handled? → A: Break compatibility, require users to rewrite all custom extensions
- Q: What performance metric should be used to measure facade delegation overhead? → A: Message throughput (messages/second) across representative workload

## Design Decisions *(clarified)*

### Deprecation Strategy

**Decision**: PulseServer becomes the unified implementation (rewritten from scratch); ServerHost becomes a deprecated facade that delegates to PulseServer.

**Rationale**: This provides a clean break for the implementation while maintaining 100% binary compatibility. Users of PulseServer continue using the same class name but get the unified implementation. Users of ServerHost receive deprecation warnings but their code continues to work without modifications through facade delegation. This approach minimizes API confusion (only one recommended class: PulseServer) while protecting existing ServerHost users during transition.

### Internal Architecture

**Decision**: Transport-focused orchestration (PulseServer model) - unified server primarily manages transports, listeners, and channels.

**Rationale**: This approach is simpler for scenarios with multiple transports (TCP + KCP + WebSocket) and provides better separation between network concerns and message processing. The transport-focused model aligns well with the current PulseServer implementation, making the consolidation more straightforward. Pipeline components integrate as managed dependencies of the transport layer rather than being the primary orchestration concern.

### Binary Compatibility

**Decision**: Maintain 100% binary compatibility using facades and adapters - zero breaking changes.

**Rationale**: This is the safest approach for users, enabling instant upgrades without any code changes required. While it may carry forward some suboptimal API design decisions and create slightly more complex internal implementation, it prioritizes user experience and adoption. The unified server becomes the recommended path forward through documentation and examples, while existing code continues to work seamlessly.
