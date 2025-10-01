# Data Model: ServiceName-Based Thread Scheduling

**Feature**: 001-channelattribute-servicename-ipulsehub
**Date**: 2025-09-30

## Overview

This document defines the key entities and their relationships for the ServiceName-based thread scheduling system.

## Core Entities

### 1. ServiceSchedulingKey

**Purpose**: Composite key that uniquely identifies a service instance for scheduling purposes.

**Properties**:
- `ServiceName` (string, required): Logical service name from ChannelAttribute
- `ServiceId` (string, required): Unique instance identifier set during authentication

**Validation Rules**:
- ServiceName: Must not be null or whitespace
- ServiceId: Must not be null or whitespace
- Both values are case-sensitive

**Relationships**:
- Maps to exactly one worker thread in ServiceThreadPool
- Used as dictionary key for thread assignment

**Equality**:
- Two keys are equal if both ServiceName and ServiceId match
- Hash code computed from both components

---

### 2. IServiceContext

**Purpose**: Provides access to service-specific context during request processing, including the ServiceId.

**Properties**:
- `ServiceId` (string?, nullable): Instance identifier, set during authentication
- `ConnectionId` (string, required): Underlying connection identifier
- `IsAuthenticated` (bool): Indicates if ServiceId has been set

**Validation Rules**:
- ServiceId can be null before authentication completes
- Once set, ServiceId should not change for the connection lifetime

**Relationships**:
- Associated with one connection in the HighPerformanceMessageEngine
- Combined with ServiceName to form ServiceSchedulingKey

**Lifecycle**:
- Created when connection established
- ServiceId set during authentication phase
- Disposed when connection terminates

---

### 3. ServiceThreadScheduler

**Purpose**: Main scheduler component that routes service invocations to the correct thread based on ServiceSchedulingKey.

**Properties**:
- `ThreadPool` (ServiceThreadPool): Manages worker threads
- `Configuration` (SchedulerConfiguration): Scheduling behavior configuration
- `IsRunning` (bool): Scheduler operational state

**Methods**:
- `ScheduleAsync(ServiceSchedulingKey, Func<Task>)`: Queue work for execution
- `StartAsync(CancellationToken)`: Initialize and start worker threads
- `StopAsync(CancellationToken)`: Gracefully shut down scheduler

**Validation Rules**:
- Must be started before accepting work
- ServiceSchedulingKey must be valid (non-null ServiceId)

**Relationships**:
- Contains one ServiceThreadPool
- Integrated into HighPerformanceMessageEngine's message processing pipeline

---

### 4. ServiceThreadPool

**Purpose**: Manages the pool of dedicated worker threads and assigns ServiceSchedulingKeys to threads.

**Properties**:
- `ThreadCount` (int): Current number of active worker threads
- `ThreadChannels` (Dictionary<int, Channel>): Per-thread message channels
- `KeyToThreadMapping` (ConcurrentDictionary<ServiceSchedulingKey, int>): Key-to-thread assignment

**Methods**:
- `GetThreadForKey(ServiceSchedulingKey)`: Determine target thread index
- `EnqueueWork(int threadIndex, WorkItem)`: Add work to thread's channel
- `ScaleThreadPool(int newSize)`: Dynamically adjust thread count

**Validation Rules**:
- ThreadCount must be between InitialThreadCount and MaxThreadCount
- All threads must have bounded channels with configured capacity

**Relationships**:
- Owned by ServiceThreadScheduler
- Creates and manages multiple WorkerThread instances

---

### 5. SchedulerConfiguration

**Purpose**: Configuration options for ServiceThreadScheduler behavior.

**Properties**:
- `InitialThreadCount` (int): Starting thread pool size (default: ProcessorCount)
- `MaxThreadCount` (int): Maximum threads allowed (default: ProcessorCount * 2)
- `ThreadIdleTimeout` (TimeSpan): Idle timeout before thread termination (default: 30s)
- `ChannelCapacity` (int): Bounded channel capacity per thread (default: 1024)
- `EnablePriorityDroppingWhenFull` (bool): Enable L3 degradation (default: true)
- `EnableMetrics` (bool): Enable performance metrics collection (default: true)

**Validation Rules**:
- InitialThreadCount > 0
- MaxThreadCount >= InitialThreadCount
- ThreadIdleTimeout > TimeSpan.Zero
- ChannelCapacity > 0

**Relationships**:
- Injected into ServiceThreadScheduler via DI
- Typically loaded from appsettings.json or PulseServerBuilder

---

### 6. WorkerThread

**Purpose**: Represents a single dedicated thread that processes work items sequentially.

**Properties**:
- `ThreadId` (int): Unique thread identifier within pool
- `MessageChannel` (Channel<WorkItem>): Bounded channel for queuing work
- `IsRunning` (bool): Thread operational state
- `ProcessedCount` (long): Total messages processed (metrics)
- `CurrentQueueDepth` (int): Current channel depth (metrics)

**Methods**:
- `StartAsync(CancellationToken)`: Begin processing loop
- `StopAsync(CancellationToken)`: Graceful shutdown with channel completion
- `EnqueueAsync(WorkItem)`: Add work to channel (blocks if full)

**Validation Rules**:
- Must be started before accepting work
- Channel must be bounded to enable backpressure

**Relationships**:
- Managed by ServiceThreadPool
- Processes work items for multiple ServiceSchedulingKeys (based on hash distribution)

---

### 7. WorkItem

**Purpose**: Encapsulates a unit of work to be executed on a worker thread.

**Properties**:
- `Key` (ServiceSchedulingKey): The service instance this work belongs to
- `Work` (Func<Task>): The async work to execute
- `EnqueuedTime` (DateTimeOffset): When work was queued (for latency metrics)
- `Priority` (MessagePriority): Priority level (for L3 degradation)

**Validation Rules**:
- Key must be valid (non-null ServiceId)
- Work delegate must not be null

**Relationships**:
- Queued in WorkerThread's message channel
- Created by ServiceThreadScheduler.ScheduleAsync

**Lifecycle**:
- Created when service method invoked
- Queued in appropriate worker thread channel
- Executed by worker thread
- Metrics recorded after completion

---

## Entity Relationship Diagram

```
┌─────────────────────────────────────────┐
│  HighPerformanceMessageEngine           │
│  (existing)                              │
└────────────────┬────────────────────────┘
                 │
                 │ integrates
                 ▼
┌─────────────────────────────────────────┐
│  ServiceThreadScheduler                  │
│  - ScheduleAsync()                       │
│  - StartAsync()                          │
└────────────────┬────────────────────────┘
                 │ owns
                 │
                 ▼
┌─────────────────────────────────────────┐
│  ServiceThreadPool                       │
│  - GetThreadForKey()                     │
│  - EnqueueWork()                         │
└────────────────┬────────────────────────┘
                 │ manages
                 │
                 ▼
         ┌───────────────┐
         │  WorkerThread │ (1..N)
         │  - StartAsync()│
         │  - Channel     │
         └───────┬───────┘
                 │ processes
                 │
                 ▼
         ┌───────────────┐
         │   WorkItem    │
         │  - Key        │
         │  - Work       │
         └───────┬───────┘
                 │
                 │ identified by
                 ▼
    ┌────────────────────────────┐
    │  ServiceSchedulingKey       │
    │  - ServiceName              │
    │  - ServiceId (from context) │
    └────────────────────────────┘
                 ▲
                 │ provides
                 │
         ┌───────┴───────┐
         │ IServiceContext│
         │  - ServiceId   │
         └────────────────┘
```

## State Transitions

### ServiceThreadScheduler Lifecycle
```
[Not Started] ──StartAsync()──> [Running] ──StopAsync()──> [Stopped]
                                    │
                                    │ ScheduleAsync()
                                    ▼
                            [Enqueueing Work Items]
```

### WorkerThread Lifecycle
```
[Created] ──StartAsync()──> [Processing] ──Channel Empty──> [Idle]
                                │                              │
                                │                              │ timeout
                                │                              ▼
                                │                          [Terminating]
                                │                              │
                                └──StopAsync()────────────────┘
                                                │
                                                ▼
                                            [Stopped]
```

### WorkItem Processing Flow
```
[Created] ──EnqueueAsync()──> [Queued] ──Dequeue()──> [Executing] ──Complete()──> [Done]
                                │                                                    │
                                │ channel full                                       │
                                ▼                                                    │
                        [Blocking/Waiting]                                           │
                                │                                                    │
                                │ L3 degradation enabled                             │
                                ▼                                                    │
                        [Dropped (low priority)]──────────────────────────────────>┘
```

## Validation Summary

All entities have clear validation rules ensuring:
- Non-null required fields (ServiceName, ServiceId, Work delegates)
- Positive numeric constraints (thread counts, capacities, timeouts)
- State consistency (scheduler must be started before scheduling work)
- Resource limits (bounded channels, max thread counts)

---

**Next**: Contract definitions (Phase 1 continued)