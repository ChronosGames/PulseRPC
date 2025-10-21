# Feature Specification: Service Thread Scheduling and Disaster Isolation

**Feature Branch**: `007-pulserpc-server-ipulsehub`
**Created**: 2025-10-21
**Status**: Draft
**Input**: User description: "目前 PulseRPC.Server 中的 IPulseHub 是在哪个线程上执行的，期望新使用一套 IPulseService, 通过在 IPulseService 中指定 ServiceName + ServiceId 进行调度执行，同时可以在 IPulseHub 的实现类中也实现 IPulseService 达到调度控制。最终目的是让整个 IPulseService 受线程调度，以达到灾难隔离。"

## Clarifications

### Session 2025-10-21

- Q: ServiceId（服务实例标识符）应该由谁负责生成和管理？ → A: 由服务实现类本身（IPulseService 实现）在初始化时生成 ServiceId
- Q: 当服务实例因连续失败被标记为 Unhealthy/Isolated 后，系统应该如何处理后续对该实例的请求？ → A: 进入冷却期（如 1 分钟），冷却期后允许少量请求重试，成功则恢复健康状态
- Q: 系统如何检测某个服务实例的线程进入了无限循环或死锁状态？ → A: 基于请求超时时间：连续 N 个请求超时则判定为阻塞
- Q: IPulseService 接口中的 ServiceId 属性是否允许在服务实例生命周期内发生变化？ → A: 不可变：ServiceId 在服务实例初始化时确定，之后不能修改
- Q: FR-010 要求暴露服务实例的监控指标（活跃实例数、请求数、健康状态等），这些指标应该通过什么方式对外提供？ → A: 通过诊断 HTTP 端点（如 /metrics 或 /health），返回 JSON 格式指标

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Service Instance Thread Affinity (Priority: P1)

Developers define services that must always execute on the same dedicated thread based on a unique service instance identifier. This ensures that all requests for a specific service instance (e.g., a specific user's session, a specific game room, a specific order) are processed sequentially on a dedicated thread, eliminating race conditions and simplifying state management.

**Why this priority**: Core functionality that enables the entire feature. Without thread affinity based on service identifiers, disaster isolation cannot be achieved.

**Independent Test**: Can be fully tested by deploying a service with multiple instances, sending concurrent requests with different service IDs, and verifying that requests with the same service ID are processed by the same thread while different service IDs use different threads. Delivers guaranteed sequential execution per service instance.

**Acceptance Scenarios**:

1. **Given** a service is registered with ServiceName="ChatRoom" and ServiceId="room-123", **When** 100 concurrent requests arrive for ServiceId="room-123", **Then** all requests are processed sequentially on the same dedicated thread
2. **Given** two service instances with ServiceId="room-123" and ServiceId="room-456", **When** requests arrive for both instances, **Then** each instance's requests are processed on different dedicated threads
3. **Given** a service instance has completed all pending work, **When** no new requests arrive for 5 minutes, **Then** the dedicated thread resources are released and can be reassigned to other service instances
4. **Given** 1000 concurrent service instances are active, **When** the system has only 16 worker threads, **Then** service instances are distributed across available threads using consistent hashing to maintain affinity

---

### User Story 2 - Disaster Isolation per Service Instance (Priority: P1)

When a service instance encounters a fatal error (infinite loop, deadlock, unhandled exception, memory leak), only that specific service instance is affected. Other service instances and the overall server continue operating normally without disruption.

**Why this priority**: Critical for production reliability and availability. Prevents cascading failures where one bad service instance brings down the entire server.

**Independent Test**: Can be fully tested by injecting a fault (infinite loop) into one service instance and verifying that: (1) the affected service instance stops responding, (2) other service instances continue processing requests normally, (3) the system detects and isolates the failed instance. Delivers fault tolerance and resilience.

**Acceptance Scenarios**:

1. **Given** ServiceId="room-123" enters an infinite loop, **When** other service instances receive requests, **Then** they continue processing without delays or errors
2. **Given** ServiceId="room-123" throws an unhandled exception, **When** the scheduler detects the failure, **Then** the thread is recovered and ServiceId="room-123" is marked as unhealthy
3. **Given** ServiceId="room-123" has exceeded timeout threshold for 3 consecutive requests, **When** the scheduler detects this pattern, **Then** the instance enters Isolated state and future requests are rejected with "Service Unavailable"
4. **Given** a service instance is in Isolated state, **When** the cooling period (default: 1 minute) expires, **Then** the instance transitions to ProbeAllowed state and limited probe requests are permitted to test recovery
5. **Given** a service instance in ProbeAllowed state successfully processes probe requests, **When** the success threshold is met, **Then** the instance transitions back to Healthy state and resumes normal operation
6. **Given** a service instance is isolated due to failures, **When** an administrator manually resets the instance health status via diagnostic endpoint, **Then** the instance immediately transitions to Healthy state

---

### User Story 3 - Backward Compatibility with IPulseHub (Priority: P2)

Existing services implementing IPulseHub continue to work without modification while optionally adopting the new IPulseService interface for thread scheduling control. Developers can incrementally migrate services without breaking existing functionality.

**Why this priority**: Enables smooth migration path and protects existing investments. Not having this would force all services to migrate simultaneously, which is risky.

**Independent Test**: Can be fully tested by deploying legacy IPulseHub services alongside new IPulseService services and verifying both work correctly. Delivers zero-downtime migration capability.

**Acceptance Scenarios**:

1. **Given** a legacy service implements only IPulseHub, **When** requests arrive, **Then** the service executes using default thread pool behavior (current behavior preserved)
2. **Given** a service implements both IPulseHub and IPulseService, **When** requests arrive, **Then** the service executes on the dedicated thread specified by IPulseService's ServiceName and ServiceId
3. **Given** a system has 50 legacy IPulseHub services and 10 new IPulseService services, **When** the server starts, **Then** all services are registered and operational without errors
4. **Given** a developer wants to migrate a service, **When** they add IPulseService interface to existing IPulseHub implementation, **Then** no changes are required to service method signatures or business logic

---

### User Story 4 - Dynamic Thread Pool Management (Priority: P2)

The system automatically manages thread allocation and deallocation based on active service instance demand. When many service instances are active, threads are efficiently shared. When service instances become idle, threads are reclaimed to minimize resource overhead.

**Why this priority**: Important for resource efficiency and scalability, but the system can function with static thread allocation initially.

**Independent Test**: Can be fully tested by monitoring thread allocation metrics while varying the number of active service instances from 10 to 10,000 and verifying efficient resource utilization. Delivers cost-effective scaling.

**Acceptance Scenarios**:

1. **Given** 10,000 service instances are active, **When** the system has 16 worker threads, **Then** instances are evenly distributed across threads with load balancing
2. **Given** a service instance has been idle for configurable threshold (default: 5 minutes), **When** the scheduler checks for idle instances, **Then** the instance's thread affinity is released and resources are freed
3. **Given** a previously idle service instance receives a new request, **When** the scheduler processes the request, **Then** a new thread affinity is established automatically
4. **Given** the system is under high load with 50,000 active instances, **When** the scheduler reaches maximum capacity, **Then** new service instance requests are queued with backpressure signaling

---

### User Story 5 - Service Instance Monitoring and Observability (Priority: P3)

Administrators and operators can monitor the health, performance, and thread allocation of individual service instances in real-time. This enables proactive problem detection and troubleshooting.

**Why this priority**: Valuable for operations but not required for core functionality. Can be added after basic scheduling works.

**Independent Test**: Can be fully tested by deploying instrumented services and verifying metrics collection, dashboard display, and alerting on failure conditions. Delivers operational visibility.

**Acceptance Scenarios**:

1. **Given** an administrator accesses the diagnostic HTTP endpoint (/metrics), **When** viewing service instances, **Then** they receive JSON-formatted real-time metrics including: active instance count, request rate per instance, average processing time per instance, thread assignment per instance
2. **Given** ServiceId="room-123" has processed 1000 requests, **When** querying the diagnostic endpoint with instance filter, **Then** the system returns JSON data: total requests, success rate, average latency, current health status (Healthy/Isolated/CoolingDown/ProbeAllowed), assigned thread ID
3. **Given** ServiceId="room-123" health status changes from Healthy to Isolated, **When** the monitoring system detects the change, **Then** an alert is generated, logged, and reflected in the /health endpoint within 5 seconds
4. **Given** an operator wants to debug a slow service instance, **When** they query the /metrics endpoint for thread scheduling data, **Then** they receive JSON containing: thread queue depth, thread utilization percentage, thread starvation indicators

---

### Edge Cases

- What happens when a service instance identifier (ServiceId) is extremely long (>1000 characters)? System must validate and reject or truncate to prevent hash collision issues.
- How does the system handle hash collisions when mapping ServiceId to threads using consistent hashing? System must use high-quality hash function and verify no two different ServiceIds map to identical thread affinity.
- What happens when a service implements IPulseService but returns null or empty string for ServiceId? System must fall back to default thread pool behavior and log a warning.
- How does the system handle rapid service instance creation/destruction (e.g., 10,000 instances created and destroyed in 10 seconds)? System must use efficient data structures to avoid memory pressure and thread pool exhaustion.
- What happens when all worker threads are blocked by slow service instances and new requests arrive? System must implement backpressure, queue requests, and optionally reject requests with "Service Unavailable" after queue threshold.
- How does the system recover when a dedicated thread crashes or becomes unresponsive? System must detect thread failure, restart the thread, and replay or reject pending requests for affected service instances.
- What happens when a service is upgraded from IPulseHub-only to IPulseHub+IPulseService during runtime? System must handle hot reload scenarios gracefully or require server restart.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide an IPulseService interface that includes ServiceName and ServiceId properties for identifying unique service instances. ServiceId MUST be immutable after service instance initialization.
- **FR-002**: System MUST route all requests for a given ServiceName+ServiceId combination to the same dedicated worker thread for sequential execution. ServiceId is determined by the service implementation class at initialization time.
- **FR-003**: System MUST support services implementing both IPulseHub and IPulseService interfaces simultaneously for incremental migration
- **FR-004**: System MUST maintain backward compatibility with existing IPulseHub-only services, executing them using current thread pool behavior
- **FR-005**: System MUST isolate service instance failures such that errors in one ServiceId do not affect other ServiceIds or the overall server
- **FR-006**: System MUST detect when a service instance becomes unhealthy by monitoring consecutive request timeouts (configurable threshold, default: 3 consecutive timeouts) and mark the instance as isolated
- **FR-006a**: System MUST implement a circuit-breaker pattern for isolated service instances: after entering Isolated state, the instance enters a cooling period (configurable, default: 1 minute), after which a limited number of probe requests are allowed to test recovery. Successful probe requests transition the instance back to Healthy state.
- **FR-007**: System MUST provide configuration options for: number of worker threads, idle instance timeout threshold, health check failure thresholds, cooling period duration, probe request limits
- **FR-008**: System MUST use consistent hashing or similar algorithm to distribute service instances across available worker threads when instance count exceeds thread count
- **FR-009**: System MUST release thread affinity for service instances that have been idle beyond the configured threshold to reclaim resources
- **FR-010**: System MUST expose metrics for observability including: active service instance count, per-instance request count, per-instance health status, thread allocation mapping. Metrics MUST be accessible via diagnostic HTTP endpoints (e.g., /metrics, /health) returning JSON-formatted data.
- **FR-011**: System MUST allow administrators to manually reset health status of isolated service instances to enable recovery via diagnostic HTTP endpoints
- **FR-012**: System MUST validate ServiceId values to prevent injection attacks, excessive length, or hash collision vulnerabilities
- **FR-013**: System MUST implement backpressure when all worker threads are saturated, queueing or rejecting new requests based on configured strategy
- **FR-014**: System MUST provide clear migration documentation showing how to upgrade existing IPulseHub services to IPulseService
- **FR-015**: System MUST log service instance lifecycle events including: instance creation, thread assignment, health status changes, instance destruction

### Key Entities

- **IPulseService**: Interface representing a service instance with scheduling control. Contains immutable ServiceName (string) identifying the service type and immutable ServiceId (string) identifying the unique instance, both determined at service initialization. Can be implemented alongside IPulseHub.
- **ServiceSchedulingKey**: Composite key combining ServiceName and ServiceId used by the scheduler to determine thread affinity. Implements consistent hashing for efficient thread assignment.
- **ServiceInstanceHealth**: Health status record for a service instance. Tracks request count, success rate, consecutive timeout count, last activity timestamp, current health state (Healthy, Isolated, CoolingDown, ProbeAllowed), cooling period expiration time, probe request allowance counter.
- **ThreadAffinity**: Mapping between ServiceSchedulingKey and assigned worker thread. Includes creation timestamp, last access timestamp, idle duration for resource management.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Service instances with the same ServiceId process all requests sequentially on a single thread with zero concurrent execution
- **SC-002**: When one service instance fails (infinite loop or crash), other service instances continue processing requests with less than 1% latency impact
- **SC-003**: System can handle at least 10,000 concurrent active service instances distributed across 16 worker threads without performance degradation
- **SC-004**: Existing IPulseHub-only services continue operating without modification or recompilation, maintaining 100% backward compatibility
- **SC-005**: Service instance health monitoring detects failures within 3 consecutive timeout events or unhandled exceptions
- **SC-006**: System reclaims idle service instance resources within 1 minute of the configured idle threshold being reached
- **SC-007**: Thread assignment for service instances remains stable (same thread) across requests with 99.99% consistency
- **SC-008**: Migration from IPulseHub to IPulseHub+IPulseService requires adding only interface implementation and property definitions without changing service method logic
- **SC-009**: System prevents cascading failures with 99.9% isolation rate (1 in 1000 service instance failures may affect others)
- **SC-010**: Observability metrics provide real-time visibility into service instance health with less than 5 second latency from event to metric availability

## Assumptions

- The existing MessageDispatcher and ServiceThreadScheduler infrastructure will be extended rather than replaced entirely
- Service instance identifiers (ServiceId) are generated by the service implementation class itself at initialization time and remain immutable throughout the service instance lifecycle
- Service developers understand the threading model implications and design their service instance state to be thread-safe within a single instance (no multi-threading within an instance)
- The default thread pool behavior for IPulseHub-only services matches current production behavior (ThreadPool.QueueUserWorkItem or Task.Run)
- Consistent hashing algorithm provides sufficient distribution quality to avoid significant load imbalances across worker threads
- Service instance idle timeout threshold is configurable with a reasonable default of 5 minutes
- Maximum number of worker threads is bounded by Environment.ProcessorCount or a configured maximum (e.g., 64 threads)
- Health check failure threshold is configurable with a reasonable default of 3 consecutive failures
- The system runs on .NET 8.0 or higher with access to modern threading primitives (System.Threading.Channels, etc.)

## Dependencies

- Existing PulseRPC.Server.Pipeline.MessageDispatcher for message routing infrastructure
- Existing PulseRPC.Server.Scheduling.ServiceThreadScheduler for thread pool management
- Existing PulseRPC.Server.Abstractions.IPulseHub interface must remain unchanged for backward compatibility
- Existing source generator (PulseRPC.Server.SourceGenerator) may need updates to recognize IPulseService interface
- Monitoring infrastructure (ILogger, metrics collectors) for observability features

## Out of Scope

- Distributed scheduling across multiple server instances (only single-server thread scheduling)
- Automatic service instance state persistence or recovery after server restart
- Dynamic thread pool sizing that adjusts worker thread count based on load (initial version uses fixed thread pool size)
- Service instance migration between threads for load rebalancing (thread affinity is sticky once assigned)
- Priority-based scheduling between different service instances (all instances treated equally within thread queue)
- Custom thread scheduling policies per service type (global scheduling policy applies to all services)
