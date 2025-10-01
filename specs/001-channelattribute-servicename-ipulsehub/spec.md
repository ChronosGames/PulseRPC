# Feature Specification: ServiceName-Based Thread Scheduling for IPulseHub

**Feature Branch**: `001-channelattribute-servicename-ipulsehub`
**Created**: 2025-09-30
**Status**: Draft
**Input**: User description: "实现基于 ChannelAttribute 进行 ServiceName 的指定，之后，所有 IPulseHub 基于 ServiceName 为服务线程调度执行，确保相同名称 Service 在同一个线程内执行，同时，要提供认证时把 ServiceId 放入 Service内的接口，以便 ServiceName + ServiceId 进行准确调度"

## Execution Flow (main)
```
1. Parse user description from Input
   → Key concepts: ChannelAttribute, ServiceName, thread scheduling, ServiceId injection
2. Extract key concepts from description
   → Actors: IPulseHub services, authentication system
   → Actions: service name specification, thread scheduling, ServiceId injection
   → Data: ServiceName, ServiceId
   → Constraints: same ServiceName must execute in same thread
3. For each unclear aspect:
   → [Performance characteristics - thread pool size, scheduling strategy]
   → [Error handling - 当线程不可用时，阻塞等待，内部自行使用基于Channel的消息降级，只影响当前连接，不卡顿整个系统]
   → [ServiceId lifecycle - ServiceId 由逻辑服务生成，如 PlayerManagerService，在每个玩家登录时生成唯一的 ServiceId，通过 PlayerService + ServiceId 注册至服务发现，这样调度器就以 PlayerService + ServiceId 进行调度]
4. Fill User Scenarios & Testing section
   → Defined service registration, scheduling, and authentication scenarios
5. Generate Functional Requirements
   → 8 testable requirements identified
6. Identify Key Entities
   → ServiceName, ServiceId, ChannelAttribute, Scheduler
7. Run Review Checklist
   → WARN "Spec has uncertainties - clarification needed"
8. Return: SUCCESS (spec ready for planning with clarifications)
```

---

## ⚡ Quick Guidelines
- ✅ Focus on WHAT users need and WHY
- ❌ Avoid HOW to implement (no tech stack, APIs, code structure)
- 👥 Written for business stakeholders, not developers

### Section Requirements
- **Mandatory sections**: Must be completed for every feature
- **Optional sections**: Include only when relevant to the feature
- When a section doesn't apply, remove it entirely (don't leave as "N/A")

### For AI Generation
When creating this spec from a user prompt:
1. **Mark all ambiguities**: Use [NEEDS CLARIFICATION: specific question] for any assumption you'd need to make
2. **Don't guess**: If the prompt doesn't specify something, mark it
3. **Think like a tester**: Every vague requirement should fail the "testable and unambiguous" checklist item
4. **Common underspecified areas**: Marked below with [NEEDS CLARIFICATION]

---

## User Scenarios & Testing *(mandatory)*

### Primary User Story
As a PulseRPC service developer, I need to ensure that all hub operations for a specific service are executed sequentially within a dedicated thread context. This prevents race conditions and ensures thread-safe execution for services that manage stateful operations. I specify the service name via ChannelAttribute, and during authentication, the ServiceId is injected into the service context, allowing the scheduler to route all operations for that ServiceName+ServiceId combination to the same thread.

### Acceptance Scenarios
1. **Given** two IPulseHub services with the same ServiceName, **When** both services receive concurrent requests, **Then** all operations for each service instance are executed in their respective dedicated threads
2. **Given** a service is authenticated, **When** the authentication completes, **Then** the ServiceId is available within the service context for scheduling decisions
3. **Given** multiple services with different ServiceNames, **When** they receive concurrent requests, **Then** operations are distributed across different threads based on ServiceName
4. **Given** a service with ServiceName+ServiceId already executing, **When** a new request arrives for the same combination, **Then** the request is queued and executed sequentially in the same thread
5. **Given** a ChannelAttribute specifies a ServiceName, **When** the service is registered, **Then** the scheduler creates or assigns a dedicated thread for that ServiceName

### Edge Cases
- What happens when a thread for a ServiceName is blocked or takes too long?
  [批量处理，若执行时间过长，直至L3的降级处理，丢弃后续的低优先级请求]
- How does the system handle ServiceName conflicts or duplicate names?
  [允许重复的 ServiceName, 使用 同一个线程 调度执行]
- What occurs if ServiceId is not set during authentication?
  [未设置 ServiceId 则无法进行调度，抛出异常]
- How are threads allocated when many ServiceNames are registered?
  [Thread pool limits and resource management]

## Requirements *(mandatory)*

### Functional Requirements
- **FR-001**: System MUST allow developers to specify a ServiceName via ChannelAttribute
- **FR-002**: System MUST ensure that all operations for IPulseHub services with the same ServiceName execute within a single dedicated thread
- **FR-003**: System MUST provide an interface to inject ServiceId into the service context during authentication
- **FR-004**: System MUST route operations based on the combination of ServiceName + ServiceId to ensure accurate scheduling
- **FR-005**: System MUST maintain execution order for requests to the same ServiceName+ServiceId combination (sequential execution)
- **FR-006**: System MUST support concurrent execution of services with different ServiceNames
- **FR-007**: System MUST handle thread lifecycle [使用配置的线程池，初始大小 + 最大值，线程调试策略随意，若线程阻塞，则等待，内部自行使用基于Channel的消息降级，只影响当前连接，不卡顿整个系统]
- **FR-008**: System MUST provide observability/diagnostics capabilities - logging, metrics

### Key Entities *(include if feature involves data)*
- **ServiceName**: A logical identifier specified via ChannelAttribute that groups related hub operations for thread-affinity scheduling
- **ServiceId**: A unique identifier assigned during authentication that distinguishes individual service instances with the same ServiceName
- **ChannelAttribute**: Configuration metadata that specifies the ServiceName for a hub service
- **Thread Scheduler**: The component responsible for routing operations to the correct thread based on ServiceName+ServiceId combination
- **Authentication Context**: The mechanism through which ServiceId is captured and made available to the service

---

## Review & Acceptance Checklist
*GATE: Automated checks run during main() execution*

### Content Quality
- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

### Requirement Completeness
- [ ] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous (except for clarified items)
- [x] Success criteria are measurable
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

---

## Execution Status
*Updated by main() during processing*

- [x] User description parsed
- [x] Key concepts extracted
- [x] Ambiguities marked
- [x] User scenarios defined
- [x] Requirements generated
- [x] Entities identified
- [ ] Review checklist passed (pending clarifications)

---
