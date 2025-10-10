# Data Model: BenchmarkApp Performance Testing

**Feature**: Complete BenchmarkApp for PulseRPC Performance Testing
**Date**: 2025-10-10

## Overview

This document defines the core data entities for the BenchmarkApp performance testing framework. The model supports comprehensive benchmarking scenarios, accurate metrics collection, multi-format reporting, baseline comparison, and threshold validation.

## Primary Entities

### 1. BenchmarkScenario

Represents a specific performance test pattern with defined parameters and execution configuration.

**Attributes**:
- `Type` (ScenarioType enum): Scenario category
  - PingPong: Latency testing with echo pattern
  - Throughput: Maximum messages/bytes per second
  - ConcurrentConnection: Connection scaling under load
  - Streaming: Continuous data flow performance
  - Stability: Long-duration reliability testing
- `Name` (string): Human-readable scenario name
- `Description` (string): Scenario purpose and methodology
- `Configuration` (TestConfiguration): Execution parameters

**Validation Rules**:
- Type must be valid ScenarioType enum value
- Name required, max 100 characters
- Configuration must be valid (see TestConfiguration)

**Relationships**:
- Has-one TestConfiguration (1:1)
- Produces-many PerformanceMetrics (1:many time series)

---

### 2. TestConfiguration

Complete set of parameters defining benchmark execution.

**Attributes**:
- `ServerAddress` (string): Target server host:port
- `TransportProtocol` (TransportProtocol enum): TCP or KCP
- `Duration` (TimeSpan): Test duration (alternative to IterationCount)
- `IterationCount` (int?): Fixed iteration count (alternative to Duration)
- `ConnectionCount` (int): Number of concurrent connections
- `RequestRate` (int): Requests per second per connection
- `MessageSize` (int): Payload size in bytes
- `RampUpTime` (TimeSpan?): Gradual connection increase period
- `WarmupDuration` (TimeSpan?): Pre-test warmup period
- `CooldownDuration` (TimeSpan?): Post-test cooldown period

**Validation Rules**:
- ServerAddress required, valid host:port format
- Either Duration > 0 OR IterationCount > 0 (mutually exclusive)
- Duration max: 48 hours
- IterationCount range: 1 - 10,000,000
- ConnectionCount range: 1 - 10,000
- RequestRate range: 1 - 100,000
- MessageSize range: 0 - 10MB (10,485,760 bytes)
- RampUpTime < Duration
- WarmupDuration < Duration
- CooldownDuration < Duration

**Relationships**:
- Belongs-to BenchmarkScenario (1:1)

---

### 3. PerformanceMetrics

Quantitative measurements collected during benchmark execution.

**Attributes**:
- `Timestamp` (DateTime): Metric sample time (UTC)
- `ElapsedTime` (TimeSpan): Time since test start
- `Latency` (LatencyMetrics): Latency statistics
- `Throughput` (ThroughputMetrics): Throughput statistics
- `Connections` (ConnectionMetrics): Connection statistics
- `Success` (SuccessMetrics): Success/failure rates
- `Resources` (ResourceMetrics): System resource usage
- `Network` (NetworkMetrics): Network statistics

**Validation Rules**:
- Timestamp required, UTC timezone
- ElapsedTime >= 0
- All sub-metrics must be valid (see below)

**Relationships**:
- Belongs-to BenchmarkScenario (many:1)
- Has-one ResourceMetrics (1:1)
- Aggregated-into BenchmarkReport (many:1)

---

### 4. LatencyMetrics

Latency percentile statistics (milliseconds).

**Attributes**:
- `Min` (double): Minimum latency
- `Max` (double): Maximum latency
- `Mean` (double): Average latency
- `Median` (double): 50th percentile
- `P50` (double): 50th percentile (alias for Median)
- `P75` (double): 75th percentile
- `P90` (double): 90th percentile
- `P95` (double): 95th percentile
- `P99` (double): 99th percentile
- `P999` (double): 99.9th percentile
- `StdDev` (double): Standard deviation

**Validation Rules**:
- All values >= 0
- Min <= Mean <= Max
- P50 <= P75 <= P90 <= P95 <= P99 <= P999
- Median == P50

---

### 5. ThroughputMetrics

Throughput rate statistics.

**Attributes**:
- `MessagesPerSecond` (double): Message rate
- `BytesSentPerSecond` (double): Outbound data rate
- `BytesReceivedPerSecond` (double): Inbound data rate
- `TotalMessagesSent` (long): Cumulative messages sent
- `TotalMessagesReceived` (long): Cumulative messages received
- `TotalBytesSent` (long): Cumulative bytes sent
- `TotalBytesReceived` (long): Cumulative bytes received

**Validation Rules**:
- All rates >= 0
- All totals >= 0
- TotalMessagesReceived <= TotalMessagesSent (accounting for in-flight)

---

### 6. ConnectionMetrics

Connection state statistics.

**Attributes**:
- `ActiveConnections` (int): Currently connected
- `TotalConnectionsEstablished` (long): Cumulative connections
- `TotalConnectionsFailed` (long): Failed connection attempts
- `ConnectionEstablishmentTime` (double): Average connection time (ms)
- `ReconnectionCount` (int): Auto-reconnection count

**Validation Rules**:
- ActiveConnections >= 0, <= TestConfiguration.ConnectionCount
- All totals >= 0
- ConnectionEstablishmentTime >= 0

---

### 7. SuccessMetrics

Success/failure rate statistics.

**Attributes**:
- `TotalRequests` (long): Total requests sent
- `SuccessfulRequests` (long): Successful responses
- `FailedRequests` (long): Failed requests
- `TimeoutCount` (long): Timeout errors
- `SuccessRate` (double): Success percentage (0-100)
- `ErrorRate` (double): Error percentage (0-100)

**Validation Rules**:
- TotalRequests == SuccessfulRequests + FailedRequests
- SuccessRate + ErrorRate == 100.0 (within tolerance)
- SuccessRate range: 0 - 100
- All counts >= 0

---

### 8. ResourceMetrics

System resource utilization.

**Attributes**:
- `CpuUsagePercent` (double): CPU utilization (0-100 per core)
- `MemoryUsageMB` (double): Working set memory (MB)
- `ThreadCount` (int): Active thread count
- `GCGen0Collections` (int): Gen 0 GC count
- `GCGen1Collections` (int): Gen 1 GC count
- `GCGen2Collections` (int): Gen 2 GC count

**Validation Rules**:
- CpuUsagePercent >= 0 (can exceed 100 on multi-core)
- MemoryUsageMB > 0
- ThreadCount > 0
- All GC counts >= 0

---

### 9. NetworkMetrics

Network interface statistics.

**Attributes**:
- `BytesSent` (long): Total bytes sent
- `BytesReceived` (long): Total bytes received
- `PacketsSent` (long?): Packets sent (if available)
- `PacketsReceived` (long?): Packets received (if available)
- `PacketLoss` (double?): Packet loss percentage (if detectable)

**Validation Rules**:
- All byte counts >= 0
- Packet counts >= 0 (if present)
- PacketLoss range: 0 - 100 (if present)

---

### 10. BenchmarkReport

Output artifact containing complete test results and analysis.

**Attributes**:
- `ReportId` (Guid): Unique report identifier
- `Scenario` (BenchmarkScenario): Test scenario executed
- `StartTime` (DateTime): Test start time (UTC)
- `EndTime` (DateTime): Test end time (UTC)
- `Duration` (TimeSpan): Actual test duration
- `AggregatedMetrics` (PerformanceMetrics): Summary statistics
- `TimeSeriesMetrics` (List<PerformanceMetrics>): Time-series data
- `Environment` (EnvironmentInfo): Execution environment
- `BaselineComparison` (BaselineComparison?): Optional baseline comparison
- `ThresholdResults` (List<ThresholdResult>): Threshold validation results
- `Errors` (List<ErrorSummary>): Error analysis
- `Status` (ReportStatus enum): Overall test status (Pass, Fail, Warning)

**Validation Rules**:
- ReportId required (unique)
- Scenario required
- StartTime < EndTime
- Duration == EndTime - StartTime (within tolerance)
- AggregatedMetrics required
- TimeSeriesMetrics count > 0

**Relationships**:
- Has-one BenchmarkScenario (1:1)
- Has-many PerformanceMetrics (1:many time series)
- Has-one EnvironmentInfo (1:1)
- Has-one-optional BaselineComparison (1:0..1)
- Has-many ThresholdResult (1:many)
- Has-many ErrorSummary (1:many)

---

### 11. BaselineData

Historical benchmark results for comparison.

**Attributes**:
- `BaselineId` (Guid): Unique baseline identifier
- `Name` (string): Baseline name/version
- `Description` (string): Baseline description
- `CreatedAt` (DateTime): Baseline creation time (UTC)
- `Scenario` (BenchmarkScenario): Original test scenario
- `Metrics` (PerformanceMetrics): Baseline metrics
- `Environment` (EnvironmentInfo): Baseline execution environment

**Validation Rules**:
- BaselineId required (unique)
- Name required, max 100 characters
- CreatedAt required
- Scenario required (must match comparison scenario)
- Metrics required

**Relationships**:
- Referenced-by BaselineComparison (many:1)

---

### 12. BaselineComparison

Performance regression analysis comparing current run to baseline.

**Attributes**:
- `Baseline` (BaselineData): Reference baseline
- `CurrentMetrics` (PerformanceMetrics): Current run metrics
- `LatencyDelta` (LatencyDelta): Latency differences
- `ThroughputDelta` (ThroughputDelta): Throughput differences
- `SuccessRateDelta` (double): Success rate difference (percentage points)
- `OverallRegression` (bool): True if performance degraded
- `RegressionDetails` (List<string>): Specific regression descriptions

**Validation Rules**:
- Baseline required
- CurrentMetrics required
- All deltas must be computed

**Relationships**:
- Belongs-to BenchmarkReport (1:1)
- References BaselineData (1:1)

---

### 13. PerformanceThreshold

Acceptable performance limit for a specific metric.

**Attributes**:
- `MetricName` (string): Metric identifier (e.g., "Latency.P95")
- `Operator` (ThresholdOperator enum): Comparison operator (LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual, Between)
- `TargetValue` (double): Expected value
- `MaxValue` (double?): Upper bound (for Between operator)
- `Severity` (ThresholdSeverity enum): Error, Warning, Info

**Validation Rules**:
- MetricName required, must match known metric path
- Operator required
- TargetValue required
- MaxValue required if Operator == Between
- If Between: TargetValue < MaxValue

---

### 14. ThresholdResult

Threshold validation outcome.

**Attributes**:
- `Threshold` (PerformanceThreshold): Evaluated threshold
- `ActualValue` (double): Measured value
- `Passed` (bool): True if threshold met
- `Message` (string): Result description

**Validation Rules**:
- Threshold required
- ActualValue required
- Message required

**Relationships**:
- Belongs-to BenchmarkReport (many:1)
- References PerformanceThreshold (1:1)

---

### 15. EnvironmentInfo

Execution environment details for reproducibility.

**Attributes**:
- `OperatingSystem` (string): OS name and version
- `ProcessorName` (string): CPU model
- `ProcessorCount` (int): Logical processor count
- `TotalMemoryMB` (double): Available RAM (MB)
- `DotNetVersion` (string): .NET runtime version
- `Architecture` (string): x86, x64, ARM, ARM64
- `MachineName` (string): Host machine name
- `UserName` (string?): Executing user (optional)

**Validation Rules**:
- All fields except UserName required
- ProcessorCount > 0
- TotalMemoryMB > 0

---

### 16. ErrorSummary

Categorized error analysis.

**Attributes**:
- `ErrorType` (string): Error category (Timeout, ConnectionFailure, ProtocolError, etc.)
- `Count` (int): Error occurrence count
- `FirstOccurrence` (DateTime): First error timestamp
- `LastOccurrence` (DateTime): Last error timestamp
- `SampleMessages` (List<string>): Example error messages (max 5)

**Validation Rules**:
- ErrorType required
- Count > 0
- FirstOccurrence <= LastOccurrence
- SampleMessages count <= 5

---

## Enumerations

### ScenarioType
- PingPong
- Throughput
- ConcurrentConnection
- Streaming
- Stability

### TransportProtocol
- TCP
- KCP

### ReportStatus
- Pass
- Fail
- Warning

### ThresholdOperator
- LessThan
- LessThanOrEqual
- GreaterThan
- GreaterThanOrEqual
- Between

### ThresholdSeverity
- Error
- Warning
- Info

---

## Entity Relationship Diagram

```
BenchmarkScenario (1) ─── (1) TestConfiguration
       │
       │ (1)
       │
       ▼ (many)
PerformanceMetrics ─── (1) ResourceMetrics
       │             ─── (1) LatencyMetrics
       │             ─── (1) ThroughputMetrics
       │             ─── (1) ConnectionMetrics
       │             ─── (1) SuccessMetrics
       │             ─── (1) NetworkMetrics
       │
       │ (many)
       │
       ▼ (1)
BenchmarkReport ─── (1) EnvironmentInfo
       │         ─── (0..1) BaselineComparison ─── (1) BaselineData
       │         ─── (many) ThresholdResult ─── (1) PerformanceThreshold
       │         ─── (many) ErrorSummary
```

---

## State Transitions

### BenchmarkScenario Execution States
1. **Created**: Configuration defined
2. **Validating**: Parameters validated
3. **Warming Up**: Warmup period (optional)
4. **Running**: Active benchmarking
5. **Cooling Down**: Cooldown period (optional)
6. **Analyzing**: Metrics aggregation
7. **Completed**: Execution finished
8. **Failed**: Execution failed

### BaselineData Lifecycle
1. **Created**: Initial baseline saved
2. **Active**: Used for comparisons
3. **Archived**: Superseded by newer baseline
4. **Deleted**: Permanently removed

---

## Persistence Format

### Baseline Storage (JSON)
```json
{
  "baselineId": "guid",
  "name": "v1.0-production",
  "description": "Production baseline from release v1.0",
  "createdAt": "2025-10-10T10:00:00Z",
  "scenario": { /* BenchmarkScenario */ },
  "metrics": { /* PerformanceMetrics */ },
  "environment": { /* EnvironmentInfo */ }
}
```

### Report Storage (JSON)
```json
{
  "reportId": "guid",
  "scenario": { /* BenchmarkScenario */ },
  "startTime": "2025-10-10T10:00:00Z",
  "endTime": "2025-10-10T10:30:00Z",
  "duration": "00:30:00",
  "aggregatedMetrics": { /* PerformanceMetrics */ },
  "timeSeriesMetrics": [ /* Array of PerformanceMetrics */ ],
  "environment": { /* EnvironmentInfo */ },
  "baselineComparison": { /* BaselineComparison */ },
  "thresholdResults": [ /* Array of ThresholdResult */ ],
  "errors": [ /* Array of ErrorSummary */ ],
  "status": "Pass"
}
```

---

## Implementation Notes

1. **Immutability**: All metric entities should be immutable (records in C#) for thread safety
2. **Time Precision**: Use high-resolution timers (Stopwatch) for latency measurements
3. **Memory Efficiency**: Use structs for small metric types to reduce allocations
4. **Percentile Calculation**: Use efficient algorithms (quickselect or histogram-based)
5. **Time Series Storage**: Consider downsampling for long-duration tests to manage memory
6. **Serialization**: Use System.Text.Json with source generators for performance
7. **Validation**: Implement validation attributes and FluentValidation for complex rules

---

**Version**: 1.0
**Last Updated**: 2025-10-10
