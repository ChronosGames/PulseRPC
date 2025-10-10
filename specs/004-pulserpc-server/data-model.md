# Data Model & Entities

## Overview
This document defines all data structures and their relationships in the message dispatch-process-response pipeline.

---

## Core Entities

### 1. Message (RpcMessage)

**Description**: Represents a network message in the request-response pipeline.

**Fields**:
| Field | Type | Description | Validation |
|-------|------|-------------|------------|
| ProtocolVersion | byte | Protocol version for compatibility checking | Must match server version (currently 1) |
| MessageType | MessageType enum | Request, Response, Error, Ping | Valid enum value |
| RequestId | Guid | Unique identifier for correlation | Non-zero GUID |
| ServiceName | string | Target service identifier | Non-null, non-empty, max 200 chars |
| MethodName | string | Target method identifier | Non-null, non-empty, max 200 chars |
| Payload | ReadOnlyMemory<byte> | Serialized parameters or return value | ≤ 10MB (10,485,760 bytes) |
| Metadata | IReadOnlyDictionary<string, string> | Headers, tracing info, priority | Optional, max 50 entries |
| ReceivedAt | long | High-resolution timestamp (ticks) | Auto-populated on reception |

**State Transitions**:
```
Received → Parsed → Dispatched → Processed → Response Created → Sent → Disposed
```

**Relationships**:
- Belongs to **Connection** (N:1)
- Maps to **ServiceRegistration** via ServiceName (N:1)
- Produces **ResponseEnvelope** (1:1)

**Invariants**:
- Once Dispatched, fields are immutable
- RequestId must be unique within connection lifetime
- Payload must be valid according to service contract

---

### 2. Connection (ServerConnection)

**Description**: Represents an active network session with a client.

**Fields**:
| Field | Type | Description | Validation |
|-------|------|-------------|------------|
| ConnectionId | string | Unique connection identifier | Non-null, non-empty, globally unique |
| ClientAddress | IPEndPoint | Client IP and port | Valid IP address |
| TransportProtocol | TransportType enum | TCP or KCP | Valid enum value |
| State | ConnectionState enum | Connecting, Active, Closing, Closed | Valid state transition |
| CreatedAt | DateTime | Connection establishment time (UTC) | ≤ DateTime.UtcNow |
| LastActivityAt | DateTime | Most recent send/receive (UTC) | ≥ CreatedAt, ≤ DateTime.UtcNow |
| MessagesSent | long | Cumulative responses sent | ≥ 0, monotonically increasing |
| MessagesReceived | long | Cumulative requests received | ≥ 0, monotonically increasing |
| ErrorCount | long | Cumulative error count | ≥ 0, monotonically increasing |
| BytesSent | long | Cumulative bytes sent | ≥ 0 |
| BytesReceived | long | Cumulative bytes received | ≥ 0 |

**State Transitions**:
```
Connecting → Active → Closing → Closed
```
(One-way transitions, no backwards movement)

**Relationships**:
- Has many **Messages** (1:N inbound, 1:N outbound)
- Tracked by **ConnectionManager** (N:1)

**Invariants**:
- State must follow one-way transitions
- LastActivityAt ≥ CreatedAt always
- Closed connections never transition to Active

**Lifecycle**:
1. **Connecting**: Socket accepted, handshake in progress
2. **Active**: Handshake complete, can send/receive
3. **Closing**: Shutdown initiated, draining pending messages
4. **Closed**: All resources released, removed from tracking

---

### 3. ServiceRegistration

**Description**: Represents a registered RPC service with compiled method invokers.

**Fields**:
| Field | Type | Description | Validation |
|-------|------|-------------|------------|
| ServiceName | string | Unique service identifier | Non-null, non-empty, unique across registrations |
| ServiceType | Type | CLR type of service implementation | Non-null, must be instantiable |
| Methods | IReadOnlyDictionary<string, CompiledMethodInvoker> | Method name → compiled delegate | All methods must have invokers |
| TimeoutPolicy | TimeSpan | Per-method timeout (default) | 1s ≤ value ≤ 5 minutes |
| Priority | MessagePriority enum | Dispatch priority (Critical, High, Normal, Low) | Valid enum value |
| State | ServiceState enum | Registered, Active, Paused, Unregistered | Valid state transition |
| InvocationCount | long | Cumulative method invocations | ≥ 0 |
| ErrorCount | long | Cumulative invocation errors | ≥ 0 |
| TotalDurationMs | long | Cumulative execution time | ≥ 0 |

**State Transitions**:
```
Registered → Active ↔ Paused → Unregistered
```

**Relationships**:
- Receives **Messages** for processing (1:N)
- Managed by **ServiceRegistry** (N:1)

**Invariants**:
- ServiceName must be globally unique
- At least one method must be registered
- Unregistered services cannot return to Active

---

### 4. RequestContext (RpcRequestContext)

**Description**: Contextual information passed to service methods during invocation.

**Fields**:
| Field | Type | Description | Validation |
|-------|------|-------------|------------|
| RequestId | Guid | Correlation identifier | Non-zero, matches originating Message.RequestId |
| ClientId | string | Client identifier (IP:Port) | Non-null, matches Connection.ClientAddress |
| ConnectionId | string | Connection identifier | Non-null, matches Connection.ConnectionId |
| Metadata | IReadOnlyDictionary<string, string> | Request headers and metadata | Immutable copy from Message.Metadata |
| CancellationToken | CancellationToken | Timeout and disconnect cancellation | Not cancelled at creation |
| StartTimestamp | long | High-resolution start time (ticks) | Stopwatch.GetTimestamp() value |
| TraceContext | ActivityContext | Distributed tracing context | Valid W3C Trace Context |

**Lifecycle**:
```
Created (from Message) → Passed to Service Method → Disposed (after response)
```

**Relationships**:
- Created from **Message** (1:1)
- Associated with **Connection** (N:1)

**Invariants**:
- Immutable after creation
- CancellationToken fires on timeout or disconnect
- Disposed after service method returns or throws

---

### 5. ResponseEnvelope

**Description**: Wrapper for service method results or exceptions.

**Fields**:
| Field | Type | Description | Validation |
|-------|------|-------------|------------|
| RequestId | Guid | Correlation identifier | Must match request RequestId |
| IsSuccess | bool | True for success, False for error | - |
| Payload | ReadOnlyMemory<byte>? | Serialized return value (if success) | XOR with ExceptionDetails |
| ExceptionDetails | ExceptionData? | Exception information (if error) | XOR with Payload |
| CompletedAt | DateTime | Completion timestamp (UTC) | ≥ request ReceivedAt |
| DurationMs | double | Execution duration | ≥ 0 |

**Nested Type: ExceptionData**:
| Field | Type | Description |
|-------|------|-------------|
| ExceptionType | string | Full type name (e.g., "System.ArgumentException") |
| Message | string | Exception message (sanitized) |
| StackTrace | string | Stack trace (sanitized, no sensitive paths) |
| InnerException | ExceptionData? | Recursive inner exception |

**Lifecycle**:
```
Created (from service result/exception) → Serialized → Transmitted → Disposed
```

**Relationships**:
- Response to **Message** (1:1)
- Sent over **Connection** (N:1)

**Invariants**:
- Exactly one of Payload or ExceptionDetails must be populated
- RequestId must match originating request
- CompletedAt ≥ request ReceivedAt

---

## Supporting Enums

### MessageType
```csharp
public enum MessageType : byte
{
    Request = 1,        // RPC request from client
    Response = 2,       // Successful response to client
    Error = 3,          // Error response to client
    Ping = 4,           // Keep-alive ping
    Pong = 5            // Keep-alive response
}
```

### ConnectionState
```csharp
public enum ConnectionState : byte
{
    Connecting = 1,     // Handshake in progress
    Active = 2,         // Fully established
    Closing = 3,        // Graceful shutdown initiated
    Closed = 4          // Resources released
}
```

### ServiceState
```csharp
public enum ServiceState : byte
{
    Registered = 1,     // Registered but not active
    Active = 2,         // Accepting requests
    Paused = 3,         // Temporarily disabled
    Unregistered = 4    // Removed from registry
}
```

### MessagePriority
```csharp
public enum MessagePriority : byte
{
    Critical = 0,       // Process immediately (health checks, admin)
    High = 1,           // High priority (latency-sensitive operations)
    Normal = 2,         // Default priority
    Low = 3             // Low priority (background tasks, analytics)
}
```

### TransportType
```csharp
public enum TransportType : byte
{
    TCP = 1,            // Reliable stream-based transport
    KCP = 2             // Low-latency UDP-based transport
}
```

---

## Entity Relationships Diagram

```
┌──────────────┐
│  Connection  │
│              │
│ - Id         │
│ - State      │───────┐
│ - Statistics │       │
└──────────────┘       │ 1:N
        │              │
        │ 1:N          ▼
        │        ┌─────────────┐
        │        │   Message   │
        │        │             │
        │        │ - RequestId │
        │        │ - Payload   │
        │        │ - Metadata  │
        │        └─────────────┘
        │              │
        │              │ N:1
        │              ▼
        │        ┌──────────────────────┐
        │        │ ServiceRegistration  │
        │        │                      │
        │        │ - ServiceName        │
        │        │ - Methods            │
        │        │ - TimeoutPolicy      │
        │        └──────────────────────┘
        │              │
        │              │ 1:1
        │              ▼
        │        ┌──────────────────┐
        │        │ RequestContext   │
        │        │                  │
        │        │ - RequestId      │
        │        │ - Metadata       │
        │        │ - Cancellation   │
        │        └──────────────────┘
        │              │
        │              │ Creates
        │              ▼
        │        ┌──────────────────┐
        └───────>│ ResponseEnvelope │
                 │                  │
                 │ - RequestId      │
                 │ - Payload/Error  │
                 └──────────────────┘
```

---

## Data Flow Through Pipeline

```
1. Network → Message (Received)
   ↓
2. Message → Parsed and Validated
   ↓
3. Message → Dispatched to ServiceRegistration
   ↓
4. ServiceRegistration + Message → Create RequestContext
   ↓
5. RequestContext → Invoke Service Method
   ↓
6. Method Result/Exception → Create ResponseEnvelope
   ↓
7. ResponseEnvelope → Serialize to bytes
   ↓
8. Serialized Response → Transmit over Connection
   ↓
9. Response Sent → Dispose all contexts
```

---

## Memory Management

### Zero-Copy Principles
- **Message.Payload**: `ReadOnlyMemory<byte>` references network buffer directly (no copy until serialization)
- **Response.Payload**: Allocated from `ArrayPool<byte>` for reuse
- **Metadata dictionaries**: Pooled `Dictionary<TKey, TValue>` instances

### Disposal Chain
```
Message → RequestContext → ResponseEnvelope
   ↓            ↓                 ↓
Dispose()   Dispose()         Dispose()
   ↓            ↓                 ↓
Return      Return            Return
buffers     contexts          buffers
to pool     to pool           to pool
```

### Lifetime Scopes
- **Connection**: Long-lived (minutes to hours)
- **Message**: Short-lived (milliseconds)
- **RequestContext**: Scoped to method invocation (microseconds to seconds)
- **ResponseEnvelope**: Short-lived (milliseconds)

---

## Validation Rules Summary

| Entity | Critical Validations |
|--------|---------------------|
| Message | ServiceName non-empty, Payload ≤ 10MB, ProtocolVersion matches |
| Connection | State transitions valid, statistics monotonically increasing |
| ServiceRegistration | ServiceName unique, Methods non-empty, TimeoutPolicy in range |
| RequestContext | RequestId matches Message, CancellationToken not cancelled |
| ResponseEnvelope | RequestId matches request, Payload XOR ExceptionDetails |

---

## Performance Considerations

### Hot Path Structures
- **Message**: Struct where possible for stack allocation
- **RequestContext**: Pooled to avoid GC pressure
- **ResponseEnvelope**: Pooled with ArrayPool for payloads

### Cold Path Structures
- **Connection**: Class (long-lived, heap allocation acceptable)
- **ServiceRegistration**: Class (created once at startup)

### Allocation Targets
- **Per-request allocation**: < 500 bytes (message + context)
- **Response allocation**: Proportional to payload size (ArrayPool)
- **Connection allocation**: Amortized over lifetime (negligible)
