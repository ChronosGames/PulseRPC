
# Implementation Plan: Complete BenchmarkApp for PulseRPC Performance Testing

**Branch**: `002-perf-benchmarkapp-pulserpc` | **Date**: 2025-10-10 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/002-perf-benchmarkapp-pulserpc/spec.md`

## Execution Flow (/plan command scope)
```
1. Load feature spec from Input path
   → Loaded successfully from spec.md
2. Fill Technical Context (scan for NEEDS CLARIFICATION)
   → Project Type: Performance testing infrastructure (multi-project)
   → Structure Decision: Modular architecture with client/server separation
3. Fill the Constitution Check section
   → Performance-First: ✅ Core requirement of benchmarking
   → Source Generation: N/A (not client SDK, but testing tool)
   → Enterprise-Grade Reliability: ✅ Error handling required
   → Test-Driven Development: ✅ Testing infrastructure must be tested
   → Modern .NET Standards: ✅ .NET 9.0 with modern C# features
4. Evaluate Constitution Check section
   → No violations detected
   → Progress Tracking: Initial Constitution Check PASS
5. Execute Phase 0 → research.md
   → Research existing implementation in perf/BenchmarkApp/
   → Identify gaps against specification requirements
6. Execute Phase 1 → contracts, data-model.md, quickstart.md
   → Define benchmark scenario contracts
   → Model performance metrics and test configurations
7. Re-evaluate Constitution Check section
   → Progress Tracking: Post-Design Constitution Check
8. Plan Phase 2 → Describe task generation approach
9. STOP - Ready for /tasks command
```

**IMPORTANT**: The /plan command STOPS at step 8. Phases 2-4 are executed by other commands:
- Phase 2: /tasks command creates tasks.md
- Phase 3-4: Implementation execution (manual or via tools)

## Summary

Complete the implementation of BenchmarkApp, a comprehensive performance testing framework for PulseRPC. The application provides systematic benchmarking across multiple scenarios (latency, throughput, concurrent connections, streaming, stability) with real-time monitoring, multi-format reporting (HTML, JSON, CSV), baseline comparison, threshold validation, and protocol comparison (TCP vs KCP).

**Current State**: Basic infrastructure exists with partial implementation of scenarios, metrics collection, and reporting. Missing features include baseline comparison, threshold validation, protocol comparison, HTML report generation, and comprehensive error analysis.

**Technical Approach**: Enhance existing modular architecture with additional scenarios, implement missing reporting formats, add threshold validation system, create baseline comparison engine, and improve real-time monitoring capabilities.

## Technical Context

**Language/Version**: C# 12 / .NET 9.0
**Primary Dependencies**:
- System.CommandLine (CLI framework)
- Spectre.Console (real-time UI)
- System.Text.Json (JSON serialization)
- Microsoft.Extensions.Hosting (dependency injection)
- PulseRPC.Client / PulseRPC.Server (tested framework)
- System.Threading.Channels (high-performance async operations)

**Storage**:
- JSON files for baseline data persistence
- JSON/CSV/HTML for report generation
- Configuration files for test scenarios

**Testing**: xUnit, FluentAssertions, NSubstitute
**Target Platform**: Cross-platform (.NET 9.0) - Windows, Linux, macOS
**Project Type**: Multi-project solution with clear separation of concerns

**Performance Goals**:
- Measure P95 latency < 50ms, P99 < 100ms
- Support >100 QPS throughput testing
- Handle 1000+ concurrent connections
- Real-time metric updates < 100ms latency
- Report generation < 5 seconds for standard runs

**Constraints**:
- Must not impact server performance during measurement
- Accurate timing measurement (high-resolution timers)
- Minimal memory allocation during benchmark runs
- Thread-safe metric collection
- Platform-independent resource monitoring

**Scale/Scope**:
- 5 primary benchmark scenarios
- 20+ configurable parameters
- Support for 10,000+ connection tests
- Multi-hour stability testing support
- Comprehensive metric collection (15+ metric types)

## Constitution Check
*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Performance-First**: ✅ YES
- Core purpose is performance testing and validation
- Measures exact targets: P95 < 50ms, >100 QPS, >99.5% success rate
- Performance regression detection is primary feature
- Rationale: Benchmarking tool must meet same standards it validates

**Source Generation Over Reflection**: ⚠️ NOT APPLICABLE
- This is a testing tool, not a client SDK
- Limited runtime reflection acceptable for test configuration
- Rationale: Testing infrastructure has different requirements than production client code

**Enterprise-Grade Reliability**: ✅ YES
- Comprehensive error handling and categorization (FR-013)
- Graceful degradation under resource constraints
- Timeout detection and failure recovery
- Detailed error reporting and analysis
- Rationale: Unreliable benchmarking produces invalid results

**Test-Driven Development**: ✅ YES
- Unit tests for metric collection logic
- Integration tests for scenario execution
- Contract tests for benchmark service endpoints
- Validation tests for report generation
- Target: >85% coverage for core components
- Rationale: Testing infrastructure must itself be tested to ensure accuracy

**Modern .NET Standards**: ✅ YES
- async/await throughout (System.Threading.Channels)
- Nullable reference types enabled
- Dependency injection via Microsoft.Extensions.Hosting
- Records for immutable data models
- Top-level statements in Program.cs
- Rationale: Modern C# ensures maintainability and performance

*No violations requiring justification*

## Project Structure

### Documentation (this feature)
```
specs/002-perf-benchmarkapp-pulserpc/
├── plan.md              # This file (/plan command output)
├── research.md          # Phase 0 output (/plan command)
├── data-model.md        # Phase 1 output (/plan command)
├── quickstart.md        # Phase 1 output (/plan command)
├── contracts/           # Phase 1 output (/plan command)
│   ├── benchmark-service.yaml    # OpenAPI spec for benchmark endpoints
│   └── metric-schemas.json       # JSON schemas for metrics
└── tasks.md             # Phase 2 output (/tasks command - NOT created by /plan)
```

### Source Code (repository root)

```
perf/BenchmarkApp/
├── PulseRPC.Benchmark.Core/           # Core abstractions and interfaces
│   ├── Abstractions/
│   │   ├── IBenchmarkScenario.cs      # Scenario execution interface
│   │   ├── IMetricsCollector.cs       # Metrics collection interface
│   │   └── IReportGenerator.cs        # Report generation interface
│   ├── Models/
│   │   ├── BenchmarkResult.cs         # Test execution results
│   │   ├── PerformanceMetrics.cs      # Collected metrics
│   │   └── TestConfiguration.cs       # Test parameters
│   └── Enums/
│       ├── ScenarioType.cs
│       └── TransportProtocol.cs
│
├── PulseRPC.Benchmark.Scenarios/      # Scenario implementations
│   ├── LatencyScenario.cs             # Ping-pong latency testing
│   ├── ThroughputScenario.cs          # Maximum throughput testing
│   ├── ConcurrentConnectionScenario.cs # Connection scaling
│   ├── StreamingScenario.cs           # Streaming performance
│   └── StabilityScenario.cs           # Long-duration reliability
│
├── PulseRPC.Benchmark.Metrics/        # Metrics collection and analysis
│   ├── Collectors/
│   │   ├── LatencyCollector.cs        # Latency percentile calculations
│   │   ├── ThroughputCollector.cs     # Messages/bytes per second
│   │   └── ResourceCollector.cs       # CPU, memory monitoring
│   ├── Analysis/
│   │   ├── StatisticalAnalyzer.cs     # P50, P95, P99 calculations
│   │   ├── ThresholdValidator.cs      # Pass/fail validation
│   │   └── BaselineComparator.cs      # Performance regression detection
│   └── Storage/
│       └── BaselineRepository.cs      # Baseline persistence
│
├── PulseRPC.Benchmark.Configuration/  # Configuration management
│   ├── Models/
│   │   ├── BenchmarkConfig.cs         # Test configuration
│   │   ├── ThresholdConfig.cs         # Performance thresholds
│   │   └── ReportingConfig.cs         # Report format options
│   └── Loaders/
│       └── ConfigurationLoader.cs     # JSON config parsing
│
├── PulseRPC.Benchmark.Shared/         # Shared utilities
│   ├── Timing/
│   │   └── HighResolutionTimer.cs     # Accurate timing
│   ├── Formatting/
│   │   └── MetricFormatter.cs         # Human-readable formatting
│   └── Environment/
│       └── EnvironmentDetector.cs     # OS, CPU, memory detection
│
├── PulseRPC.Benchmark.Client/         # CLI client application
│   ├── Program.cs                     # Entry point
│   ├── Commands/
│   │   ├── RunCommand.cs              # Execute benchmarks
│   │   ├── ReportCommand.cs           # Generate reports
│   │   └── BaselineCommand.cs         # [NEW] Baseline management
│   ├── Engine/
│   │   └── TestExecutionEngine.cs     # Orchestrates test execution
│   ├── UI/
│   │   ├── RealtimeDisplayManager.cs  # Spectre.Console live display
│   │   └── DisplayComponents/         # UI components
│   └── Transport/
│       └── ClientConnectionManager.cs # Connection lifecycle
│
├── PulseRPC.Benchmark.Server/         # Benchmark server
│   ├── Program.cs                     # Server entry point
│   ├── Services/
│   │   └── BenchmarkHubImpl.cs        # Test service endpoints
│   ├── Configuration/
│   │   └── ServerConfiguration.cs     # Server settings
│   └── Monitoring/
│       └── ServerMetricsIntegration.cs # Server-side metrics
│
└── PulseRPC.Benchmark.Reporting/      # [NEW] Report generation
    ├── Generators/
    │   ├── HtmlReportGenerator.cs     # HTML with charts
    │   ├── JsonReportGenerator.cs     # JSON data export
    │   ├── CsvReportGenerator.cs      # CSV tabular export
    │   └── ConsoleReportGenerator.cs  # Terminal output
    ├── Templates/
    │   └── report-template.html       # HTML template
    └── Visualization/
        ├── ChartGenerator.cs          # Chart data generation
        └── Models/
            └── ChartData.cs           # Chart data models

tests/BenchmarkApp.Tests/
├── Unit/
│   ├── Scenarios/
│   │   ├── LatencyScenarioTests.cs
│   │   └── ThroughputScenarioTests.cs
│   ├── Metrics/
│   │   ├── StatisticalAnalyzerTests.cs
│   │   ├── ThresholdValidatorTests.cs
│   │   └── BaselineComparatorTests.cs
│   └── Reporting/
│       ├── HtmlGeneratorTests.cs
│       └── JsonGeneratorTests.cs
├── Integration/
│   ├── EndToEndScenarioTests.cs
│   └── ReportGenerationTests.cs
└── Contract/
    └── BenchmarkServiceContractTests.cs
```

**Structure Decision**: Modular architecture with clear separation:
1. **Core** - Abstractions and shared models
2. **Scenarios** - Individual test scenario implementations
3. **Metrics** - Collection, analysis, and storage
4. **Configuration** - Test configuration management
5. **Shared** - Cross-cutting utilities
6. **Client** - CLI application for running tests
7. **Server** - Test server implementation
8. **Reporting** - [NEW] Multi-format report generation
9. **Tests** - Comprehensive test suite

This structure supports parallel development, clear dependencies, and maintainability.

## Phase 0: Outline & Research

### Research Tasks

1. **Analyze existing BenchmarkApp implementation**:
   - Task: "Review current implementation in perf/BenchmarkApp/ and identify implemented vs missing features from spec"
   - Deliverable: Gap analysis document

2. **Research high-performance metrics collection patterns**:
   - Task: "Research best practices for accurate latency measurement in .NET (System.Diagnostics.Stopwatch, DateTime.UtcNow.Ticks, etc.)"
   - Task: "Investigate concurrent metrics collection without impacting test accuracy"
   - Deliverable: Timing and metrics collection patterns

3. **Research report generation libraries**:
   - Task: "Evaluate HTML template engines (Razor, Scriban, RazorLight) for report generation"
   - Task: "Research charting libraries compatible with static HTML (Chart.js, Plotly)"
   - Deliverable: Report generation technology decisions

4. **Research baseline comparison algorithms**:
   - Task: "Research statistical methods for performance regression detection (t-test, confidence intervals)"
   - Task: "Investigate industry-standard benchmark comparison practices"
   - Deliverable: Baseline comparison algorithm design

5. **Research resource monitoring across platforms**:
   - Task: "Research cross-platform CPU and memory monitoring (.NET APIs vs platform-specific)"
   - Task: "Investigate network interface statistics collection on Windows/Linux"
   - Deliverable: Resource monitoring implementation plan

6. **Review existing benchmark implementations**:
   - Task: "Analyze BenchmarkDotNet architecture for best practices"
   - Task: "Review k6, Apache JMeter, wrk for feature inspiration"
   - Deliverable: Benchmark framework design patterns

### Consolidate Findings

**Output**: `research.md` containing:
- Current implementation status (what exists, what's missing)
- Timing/metrics collection approach (chosen timers, collection patterns)
- Report generation technology stack (HTML template engine, charting library)
- Baseline comparison algorithm (statistical methods)
- Resource monitoring approach (APIs and platform support)
- Architecture decisions informed by industry best practices

## Phase 1: Design & Contracts

### 1. Data Model (`data-model.md`)

Extract entities from feature spec and current implementation:

**Primary Entities**:
- `BenchmarkScenario`: Type, parameters, duration configuration
- `PerformanceMetrics`: Latency percentiles, throughput, success rates
- `TestConfiguration`: Server connection, scenario selection, concurrency
- `BenchmarkReport`: Results, analysis, visualizations, threshold evaluation
- `BaselineData`: Historical metrics, configuration, execution environment
- `PerformanceThreshold`: Metric name, acceptable range, pass/fail criteria
- `ResourceMetrics`: CPU percentage, memory usage, network stats
- `EnvironmentInfo`: OS, CPU model, memory, .NET version

**Relationships**:
- BenchmarkScenario → TestConfiguration (1:1)
- BenchmarkScenario → PerformanceMetrics (1:many - time series)
- BenchmarkReport → PerformanceMetrics (1:1 aggregated)
- BenchmarkReport → BaselineData (1:1 optional comparison)
- BenchmarkReport → PerformanceThreshold[] (1:many validations)
- PerformanceMetrics → ResourceMetrics (1:1 per sample)

**Validation Rules**:
- Connection count > 0, < 10000
- Request rate > 0, < 100000
- Message size >= 0, < 10MB
- Duration > 0, < 48 hours
- Percentile values 0-100
- Threshold ranges must be valid (min < max)

### 2. API Contracts (`/contracts/`)

**Benchmark Service Contract** (`benchmark-service.yaml`):
```yaml
# OpenAPI 3.0 specification
paths:
  /benchmark/echo:
    post:
      summary: Echo benchmark endpoint
      parameters:
        - name: payload
          schema:
            type: string
            format: byte
      responses:
        200:
          description: Echo response
          content:
            application/octet-stream:
              schema:
                type: string
                format: byte

  /benchmark/throughput:
    post:
      summary: Throughput test endpoint

  /benchmark/stream:
    post:
      summary: Streaming test endpoint
```

**Metric Schemas** (`metric-schemas.json`):
```json
{
  "PerformanceMetrics": {
    "type": "object",
    "properties": {
      "latency": {
        "type": "object",
        "properties": {
          "min": { "type": "number" },
          "max": { "type": "number" },
          "mean": { "type": "number" },
          "median": { "type": "number" },
          "p50": { "type": "number" },
          "p95": { "type": "number" },
          "p99": { "type": "number" },
          "p999": { "type": "number" }
        },
        "required": ["min", "max", "mean", "p95", "p99"]
      },
      "throughput": {
        "type": "object",
        "properties": {
          "messagesPerSecond": { "type": "number" },
          "bytesSentPerSecond": { "type": "number" },
          "bytesReceivedPerSecond": { "type": "number" }
        }
      }
    }
  }
}
```

### 3. Contract Tests

Generate failing tests for each contract:

**`tests/BenchmarkApp.Tests/Contract/BenchmarkServiceContractTests.cs`**:
```csharp
public class BenchmarkServiceContractTests
{
    [Fact]
    public async Task EchoEndpoint_ShouldReturnSamePayload()
    {
        // Arrange
        var client = CreateBenchmarkClient();
        var payload = GenerateTestPayload(1024);

        // Act
        var response = await client.EchoAsync(payload);

        // Assert
        response.Should().Equal(payload);
    }

    [Fact]
    public async Task ThroughputEndpoint_ShouldAcceptMultipleRequests()
    {
        // Tests concurrent request handling
    }
}
```

**`tests/BenchmarkApp.Tests/Unit/Metrics/StatisticalAnalyzerTests.cs`**:
```csharp
public class StatisticalAnalyzerTests
{
    [Theory]
    [InlineData(new[] { 1.0, 2.0, 3.0, 4.0, 5.0 }, 3.0, 5.0)]
    public void CalculatePercentiles_ShouldReturnCorrectP95AndP99(
        double[] values, double expectedP95, double expectedP99)
    {
        // Arrange
        var analyzer = new StatisticalAnalyzer();

        // Act
        var result = analyzer.CalculatePercentiles(values);

        // Assert
        result.P95.Should().BeApproximately(expectedP95, 0.01);
        result.P99.Should().BeApproximately(expectedP99, 0.01);
    }
}
```

### 4. Test Scenarios from User Stories

Extract acceptance scenarios from spec:

**Scenario 1: Latency Benchmark** → `tests/Integration/LatencyBenchmarkTests.cs`
**Scenario 2: Throughput Benchmark** → `tests/Integration/ThroughputBenchmarkTests.cs`
**Scenario 3: Multi-format Reports** → `tests/Integration/ReportGenerationTests.cs`
**Scenario 4: Baseline Comparison** → `tests/Integration/BaselineComparisonTests.cs`
**Scenario 5: Threshold Validation** → `tests/Integration/ThresholdValidationTests.cs`

### 5. Quickstart (`quickstart.md`)

**Title**: "Running Your First PulseRPC Benchmark"

**Prerequisites**:
- .NET 9.0 SDK installed
- PulseRPC.Benchmark.Server running
- Basic understanding of performance testing

**Steps**:
1. Start benchmark server: `dotnet run --project PulseRPC.Benchmark.Server`
2. Run basic latency test: `dotnet run --project PulseRPC.Benchmark.Client -- run --server localhost:8080 --scenario ping-pong --duration 30`
3. View results in console output
4. Generate HTML report: `dotnet run --project PulseRPC.Benchmark.Client -- report --input results/latest.json --format html --output report.html`
5. Open `report.html` in browser to view detailed charts and analysis

**Validation Steps**:
- Verify server shows "Listening on port 8080"
- Verify client shows real-time progress during test
- Verify report contains latency percentiles, throughput metrics
- Verify HTML report displays charts correctly

### 6. Update Agent Context

Execute the update script:
```powershell
.specify/scripts/powershell/update-agent-context.ps1 -AgentType claude
```

This will update `CLAUDE.md` with:
- New technology: Spectre.Console for real-time UI
- New patterns: Statistical analysis, baseline comparison
- Recent changes: BenchmarkApp completion phase
- Keep existing manual content between markers

**Output**:
- `data-model.md` with complete entity definitions
- `/contracts/benchmark-service.yaml` and `/contracts/metric-schemas.json`
- Failing contract tests in `tests/BenchmarkApp.Tests/Contract/`
- Failing unit tests in `tests/BenchmarkApp.Tests/Unit/`
- Integration test scenarios in `tests/BenchmarkApp.Tests/Integration/`
- `quickstart.md` with step-by-step validation
- Updated `D:\Projects\PulseRPC\CLAUDE.md`

## Phase 2: Task Planning Approach
*This section describes what the /tasks command will do - DO NOT execute during /plan*

**Task Generation Strategy**:

1. **Load existing implementation status** from `research.md`
2. **Generate test tasks first** (TDD approach):
   - Contract test tasks for benchmark service endpoints [P]
   - Unit test tasks for new components (BaselineComparator, ThresholdValidator, HtmlGenerator) [P]
   - Integration test tasks for end-to-end scenarios
3. **Generate implementation tasks**:
   - Implement missing scenarios (if any gaps identified)
   - Implement baseline comparison engine
   - Implement threshold validation system
   - Implement HTML report generator with charts
   - Implement CSV report generator
   - Enhance JSON report with baseline comparison
   - Implement baseline management CLI commands
   - Add protocol comparison mode
4. **Generate documentation tasks**:
   - Update README with full usage examples
   - Add configuration documentation
   - Add threshold configuration guide

**Ordering Strategy**:
1. **Phase 2A**: Contract and unit tests [P] (parallel execution)
2. **Phase 2B**: Core implementations (BaselineComparator → ThresholdValidator → ReportGenerators)
3. **Phase 2C**: Integration tests (after implementations)
4. **Phase 2D**: CLI enhancements (baseline commands, protocol comparison)
5. **Phase 2E**: Documentation updates

**Estimated Output**: 30-35 numbered, dependency-ordered tasks in tasks.md

**Task Format**:
```
## Task 1: [P] Write contract tests for benchmark service
**Type**: Test
**Dependencies**: None
**Files**: tests/BenchmarkApp.Tests/Contract/BenchmarkServiceContractTests.cs
**Description**: Implement contract tests validating echo, throughput, and streaming endpoints

## Task 2: [P] Write unit tests for StatisticalAnalyzer
**Type**: Test
**Dependencies**: None
**Files**: tests/BenchmarkApp.Tests/Unit/Metrics/StatisticalAnalyzerTests.cs
...
```

**IMPORTANT**: This phase is executed by the /tasks command, NOT by /plan

## Phase 3+: Future Implementation
*These phases are beyond the scope of the /plan command*

**Phase 3**: Task execution (/tasks command creates tasks.md with 30-35 ordered tasks)
**Phase 4**: Implementation (execute tasks.md following TDD and constitutional principles)
**Phase 5**: Validation
- Run complete test suite (contract, unit, integration)
- Execute quickstart.md validation steps
- Performance validation: Run benchmark against reference server, verify metrics accuracy
- Report validation: Generate all report formats, verify charts and data
- Baseline validation: Create baseline, run comparison, verify regression detection
- Threshold validation: Configure thresholds, verify pass/fail logic

## Complexity Tracking
*Fill ONLY if Constitution Check has violations that must be justified*

No violations detected. Project aligns with all constitutional principles.

## Progress Tracking
*This checklist is updated during execution flow*

**Phase Status**:
- [x] Phase 0: Research complete (/plan command) ✅
- [x] Phase 1: Design complete (/plan command) ✅
- [x] Phase 2: Task planning complete (/plan command - describe approach only) ✅
- [ ] Phase 3: Tasks generated (/tasks command) - Ready for execution
- [ ] Phase 4: Implementation complete
- [ ] Phase 5: Validation passed

**Gate Status**:
- [x] Initial Constitution Check: PASS ✅
- [x] Post-Design Constitution Check: PASS ✅
- [x] All NEEDS CLARIFICATION resolved (none in spec) ✅
- [x] Complexity deviations documented (none) ✅

**Artifacts Generated**:
- [x] research.md - Gap analysis and technology decisions
- [x] data-model.md - Complete entity definitions with 16 entities
- [x] contracts/benchmark-service.yaml - OpenAPI 3.0 service specification
- [x] contracts/metric-schemas.json - JSON Schema for all metrics
- [x] quickstart.md - Step-by-step validation guide
- [x] CLAUDE.md updated - Agent context with new technologies

---
*Based on Constitution v1.0.0 - See `.specify/memory/constitution.md`*
