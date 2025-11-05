# Feature Specification: PulseRPC - Distributed Game Server Framework

**Feature Branch**: `008-distributed-game-server-framework`
**Created**: 2025-11-05
**Status**: Draft
**Input**: User description: "依据 @docs\PulseRPC分布式游戏服务器框架.md 的描述进行项目整体目标的调整"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Distributed Service Deployment (Priority: P1)

A game server operator deploys a distributed cluster where each node can run multiple services (IPulseService instances). Each service is uniquely identified by ServiceType and ServiceId, and is addressable via a PID (Process Identifier). Services are distributed across nodes using consistent hashing for automatic load distribution and fault tolerance.

**Why this priority**: Core infrastructure requirement - without distributed service management, the entire framework cannot function. This is the foundation for all other features.

**Independent Test**: Deploy a cluster with 3 nodes, create 10 services with different ServiceTypes, verify each service has a unique PID and can be located via consistent hashing. System delivers a working distributed service mesh.

**Acceptance Scenarios**:

1. **Given** a cluster with multiple nodes, **When** a new service is created with ServiceType and ServiceId, **Then** the system assigns a unique 64-bit PID containing cluster ID (16-bit), node ID (8-bit), ServiceShortType (8-bit), and node-local sequence number (32-bit)
2. **Given** a service with a specific ServiceType and ServiceId, **When** querying for the service location, **Then** consistent hashing determines the correct node hosting the service
3. **Given** a node leaves the cluster, **When** the hash ring is updated, **Then** only affected services are migrated to other nodes while unaffected services remain stable

---

### User Story 2 - Client-Server RPC Communication (Priority: P1)

A Unity game client connects to a game server node and invokes server methods (IPulseHub) using protocol numbers. The server can push events to the client via IPulseReceiver. All communication uses efficient binary serialization with MemoryPack and supports multiple transport protocols (TCP/KCP).

**Why this priority**: Essential for client-server interaction - this is the primary use case for game developers using the framework. Without this, the framework cannot serve its core purpose.

**Independent Test**: Connect a Unity client to a server, invoke a Hub method with parameters, receive a response, and handle a server-pushed event. System delivers bidirectional RPC communication that game developers can use immediately.

**Acceptance Scenarios**:

1. **Given** a Unity client connected to a server node, **When** the client invokes a Hub method with protocol number and serialized parameters, **Then** the server executes the method and returns a response with status code and serialized result
2. **Given** a server needs to push an event to a connected client, **When** the server triggers a Receiver event with protocol number and data, **Then** the client receives the event and invokes the corresponding callback handler
3. **Given** a client connection is lost, **When** the server attempts to push an event, **Then** the system detects the disconnection and handles it gracefully without blocking

---

### User Story 3 - Inter-Node RPC Communication (Priority: P1)

Services on different nodes communicate with each other via internal RPC. A service on Node A can invoke methods on a service on Node B by addressing it via PID. The system handles request routing, response matching, and network failures automatically.

**Why this priority**: Critical for distributed game server architectures where services need to collaborate (e.g., game service calling inventory service, matchmaking service calling room service). This enables the "distributed" aspect of the framework.

**Independent Test**: Deploy two services on different nodes, have Service A invoke a method on Service B using its PID, verify the request is routed correctly and the response is returned. System delivers transparent cross-node service communication.

**Acceptance Scenarios**:

1. **Given** Service A on Node 1 and Service B on Node 2, **When** Service A invokes a method on Service B using its PID, **Then** the request is routed through internal TCP connection with request ID, target PID, method ID, and serialized parameters
2. **Given** a request sent from Service A to Service B, **When** Service B processes the request and returns a result, **Then** the response contains the matching request ID, status code, and serialized result data
3. **Given** a service invocation to a target PID, **When** the target service is not found or the node is unreachable, **Then** the system returns an error response with appropriate status code within a timeout period

---

### User Story 4 - Service Discovery and Registration (Priority: P2)

When a node starts up, it registers itself with Etcd, providing node ID, internal communication address/port, external communication address/port, and load information (CPU, memory). Other nodes can discover available nodes and monitor cluster membership changes in real-time.

**Why this priority**: Essential for cluster formation and dynamic scaling, but the framework can function with static node configuration for initial testing. This enables production-ready deployment scenarios.

**Independent Test**: Start a new node, verify it registers with Etcd with correct information, start another node and verify it discovers the first node. Stop the first node and verify the second node detects the departure. System delivers automatic cluster membership management.

**Acceptance Scenarios**:

1. **Given** a new node starting up, **When** the node initializes, **Then** it registers with Etcd including node ID (8-bit), internal address/port, external address/port, and current load metrics
2. **Given** a node registered in Etcd, **When** another node queries for cluster members, **Then** it receives a list of all active nodes with their connection details and load information
3. **Given** a node goes offline, **When** Etcd detects the node failure (via heartbeat timeout), **Then** other nodes are notified of the node departure and update their hash ring accordingly

---

### User Story 5 - Component-Based Service Architecture (Priority: P2)

A service (IPulseService) is composed of multiple components (IPulseComponent) that each handle specific functionality. Components provide modular, reusable functionality that can be composed to build complex services without tight coupling.

**Why this priority**: Important for code organization and reusability, but services can function without component decomposition initially. This enables clean architecture and maintainability for complex services.

**Independent Test**: Create a service with 3 components (e.g., InventoryComponent, AchievementComponent, StatisticsComponent), verify each component can access shared service context and communicate with each other. System delivers modular service architecture.

**Acceptance Scenarios**:

1. **Given** a service with multiple components, **When** the service initializes, **Then** all components are initialized with access to the parent service context
2. **Given** a component needs to interact with another component in the same service, **When** the component requests access to another component by type, **Then** the service provides the requested component reference
3. **Given** a Hub method invocation targets functionality implemented in a component, **When** the request is processed, **Then** the appropriate component method is invoked with request parameters

---

### User Story 6 - Unity Client Code Generation (Priority: P2)

Game developers define server Hub and Receiver interfaces with protocol numbers. The PulseRPC.Client.SourceGenerator automatically generates Unity-compatible client code (C# 9.0 compatible) that provides strongly-typed RPC calls and event handlers, eliminating manual protocol handling.

**Why this priority**: Significantly improves developer experience and reduces errors, but clients can be implemented manually for initial development. This enables rapid client development with compile-time safety.

**Independent Test**: Define a Hub interface with 5 methods and a Receiver interface with 3 events, run the source generator, compile the Unity client, verify all methods and events are available with correct signatures. System delivers automatic client code generation.

**Acceptance Scenarios**:

1. **Given** a server Hub interface with protocol-numbered methods, **When** the source generator runs, **Then** it produces Unity-compatible client code with strongly-typed method proxies
2. **Given** a server Receiver interface with protocol-numbered events, **When** the source generator runs, **Then** it produces Unity-compatible event handler registration code
3. **Given** generated client code, **When** compiled in Unity with .NET Standard 2.1 and C# 9.0, **Then** the code compiles without errors and provides IntelliSense support

---

### User Story 7 - Persistent Data and Caching (Priority: P3)

Services can persist data to MongoDB and cache frequently accessed data in Redis. The framework provides abstractions for data access that handle connection management, serialization, and caching strategies automatically.

**Why this priority**: Important for production games requiring persistent player data, but services can function with in-memory state for testing and development. This enables production-grade data persistence.

**Independent Test**: Configure a service to use MongoDB and Redis, perform CRUD operations on game entities, verify data is persisted and cached correctly. System delivers transparent data layer integration.

**Acceptance Scenarios**:

1. **Given** a service configured with MongoDB connection, **When** the service saves an entity, **Then** the entity is persisted to MongoDB with proper serialization
2. **Given** an entity cached in Redis, **When** the service queries for the entity, **Then** the cache is checked first before querying MongoDB
3. **Given** cached data in Redis, **When** the underlying MongoDB data is updated, **Then** the cache is invalidated or updated to maintain consistency

---

### User Story 8 - Monitoring and Health Checks (Priority: P3)

Each node exposes HTTP health check endpoints that report node status, service health, and metrics. Operators can integrate with Sentry for error logging and extend monitoring with Prometheus/Grafana for observability.

**Why this priority**: Critical for production operations but not required for development and testing. This enables operational excellence in production environments.

**Independent Test**: Deploy a node, query its health endpoint to verify node status and service list, trigger an error and verify it's logged to Sentry. System delivers basic operational monitoring.

**Acceptance Scenarios**:

1. **Given** a running node, **When** an HTTP request is made to the health check endpoint, **Then** the response includes node status, running services, CPU/memory metrics, and overall health status
2. **Given** a service throws an exception, **When** the error occurs, **Then** the error details are logged to Sentry with context (service PID, method name, parameters)
3. **Given** Prometheus monitoring is configured, **When** the monitoring system scrapes metrics, **Then** the node exposes RPC call counts, latency histograms, active connections, and service health metrics

---

### Edge Cases

- What happens when a service's target node fails during a request? The request should timeout and return an error, allowing the caller to retry or handle the failure
- How does the system handle PID collisions when a node restarts with the same node ID? The node-local sequence number (32-bit) should continue from a persisted value or use timestamp-based generation
- What happens when consistent hashing places a service on a node that is at capacity? The system should support configurable capacity limits and rebalancing policies
- How does the client handle partial responses when the server crashes mid-processing? The client should implement timeout-based failure detection and provide retry mechanisms
- What happens when Etcd becomes unavailable? Nodes should continue operating with their last known cluster state but prevent new node joins or service migrations until Etcd recovers
- How are Hub method protocol numbers managed to avoid conflicts? The framework should provide tools for centralized protocol number registry or validation during compilation
- What happens when a client connects but the target service has not started yet? The connection should be held or rejected with a clear error indicating service unavailability

## Requirements *(mandatory)*

### Functional Requirements

#### Core Service Infrastructure

- **FR-001**: System MUST support creating distributed services (IPulseService) identified by ServiceType and ServiceId
- **FR-002**: System MUST generate unique 64-bit PIDs with structure: cluster ID (16-bit) + node ID (8-bit) + ServiceShortType (8-bit) + sequence number (32-bit)
- **FR-003**: System MUST distribute services across nodes using consistent hashing based on ServiceType and ServiceId
- **FR-004**: System MUST support composing services from multiple components (IPulseComponent) for modular functionality
- **FR-005**: System MUST provide lifecycle management for services (initialize, start, stop, dispose)

#### RPC Communication

- **FR-006**: System MUST support client-server RPC via IPulseHub interfaces with protocol-numbered methods
- **FR-007**: System MUST support server-client event push via IPulseReceiver interfaces with protocol-numbered events
- **FR-008**: System MUST support inter-node RPC using internal protocol with request ID, target PID, method ID, and serialized parameters
- **FR-009**: System MUST serialize messages using MemoryPack for efficient binary encoding
- **FR-010**: External client communication MUST support both TCP and KCP transport protocols
- **FR-011**: Internal node communication MUST use TCP with the defined RPC protocol format

#### Client-Server Protocol

- **FR-012**: Client requests MUST include protocol number (2 bytes) and serialized parameters
- **FR-013**: Server responses MUST include protocol number (2 bytes), status code (1 byte), and serialized response data
- **FR-014**: Internal requests MUST include request ID (4 bytes), target PID (8 bytes), method ID (2 bytes), and serialized parameters
- **FR-015**: Internal responses MUST include request ID (4 bytes), status code (1 byte), and serialized response/error data
- **FR-016**: System MUST match responses to requests using request ID for both client and internal RPC

#### Service Discovery

- **FR-017**: Nodes MUST register with Etcd on startup with node ID, internal address/port, external address/port, and load metrics
- **FR-018**: Nodes MUST maintain heartbeat with Etcd to indicate availability
- **FR-019**: System MUST detect node failures via Etcd and update consistent hashing ring
- **FR-020**: System MUST support dynamic cluster membership changes (nodes joining/leaving)

#### Code Generation

- **FR-021**: PulseRPC.Client.SourceGenerator MUST generate Unity-compatible client code from Hub and Receiver interfaces
- **FR-022**: Generated client code MUST be compatible with .NET Standard 2.1 and C# 9.0 syntax for Unity support
- **FR-023**: PulseRPC.Server.SourceGenerator MUST generate server-side RPC dispatch code compatible with C# 11.0+
- **FR-024**: Generated code MUST provide strongly-typed method signatures matching interface definitions

#### Data Persistence and Caching

- **FR-025**: System MUST support MongoDB integration for persistent data storage
- **FR-026**: System MUST support Redis integration for data caching
- **FR-027**: System MUST provide abstractions for data access that hide storage implementation details

#### Monitoring and Operations

- **FR-028**: Nodes MUST expose HTTP health check endpoints reporting node and service status
- **FR-029**: System MUST integrate with Sentry for error logging and exception tracking
- **FR-030**: System MUST support Prometheus metrics export for monitoring (optional integration)

#### Fault Tolerance

- **FR-031**: System MUST handle node failures gracefully with service migration to available nodes
- **FR-032**: RPC requests to unavailable services MUST return error responses within configurable timeout period
- **FR-033**: System MUST continue operating when Etcd is temporarily unavailable, using last known cluster state

### Key Entities

- **IPulseService**: Represents a distributed service instance running on a node, identified by unique PID, can contain multiple components
- **PID (Process Identifier)**: 64-bit unique identifier with cluster ID, node ID, ServiceShortType, and sequence number components
- **IPulseHub**: Server-side interface defining RPC methods that clients can invoke, each method has a unique protocol number
- **IPulseReceiver**: Server-side interface defining events that servers can push to clients, each event has a unique protocol number
- **IPulseComponent**: Modular functional unit within a service, provides composable functionality for building services
- **ServiceType**: Type identifier for a service category (e.g., GameService, InventoryService), used in consistent hashing
- **ServiceId**: Instance identifier for a specific service within a ServiceType, used in consistent hashing
- **Node**: Physical or virtual machine running multiple services, registered in Etcd with connection and load information
- **Consistent Hash Ring**: Virtual ring structure that maps services to nodes for distributed placement
- **Protocol Number**: 16-bit identifier assigned to each Hub method and Receiver event for wire protocol identification

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A single node can host at least 1000 active services without performance degradation
- **SC-002**: RPC call latency between services on the same node is under 1 millisecond for 95% of calls
- **SC-003**: RPC call latency between services on different nodes is under 10 milliseconds for 95% of calls
- **SC-004**: Client-server RPC latency is under 50 milliseconds for 95% of calls in local network conditions
- **SC-005**: Cluster supports at least 100 nodes without consistent hashing performance impact
- **SC-006**: Node failure is detected and hash ring updated within 10 seconds
- **SC-007**: Service migration during node failure affects less than 20% of services in a balanced cluster
- **SC-008**: Unity client code generation completes in under 5 seconds for projects with 100 Hub methods
- **SC-009**: Generated client code has zero manual modifications required for 95% of use cases
- **SC-010**: System supports at least 10,000 concurrent client connections per node
- **SC-011**: Memory allocation per RPC call is under 1KB using MemoryPack serialization
- **SC-012**: Health check endpoint responds within 100 milliseconds
- **SC-013**: Developer can create a new distributed service and test it end-to-end within 30 minutes
- **SC-014**: Framework documentation enables a game developer to integrate a Unity client within 2 hours

## Assumptions

- **Assumption 1**: Game developers are familiar with Unity and C# but may not have distributed systems experience
- **Assumption 2**: Cluster nodes have reliable network connectivity with RTT under 100ms between nodes
- **Assumption 3**: Etcd cluster is properly deployed and maintained separately from game server nodes
- **Assumption 4**: MongoDB and Redis are deployed and accessible from service nodes
- **Assumption 5**: Protocol numbers for Hub methods and Receiver events are managed and assigned without conflicts
- **Assumption 6**: Services are designed to be stateless or have state that can be migrated during node failures
- **Assumption 7**: Unity projects target .NET Standard 2.1 with C# 9.0 or compatible language version
- **Assumption 8**: Error logging to Sentry does not impact game server performance during high load
- **Assumption 9**: Consistent hashing provides reasonable load distribution for typical game service patterns
- **Assumption 10**: Transport layer (TCP/KCP) selection is configured per deployment based on game requirements
