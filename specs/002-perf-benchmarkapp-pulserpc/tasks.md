# Tasks: Complete BenchmarkApp for PulseRPC Performance Testing

**Input**: Design documents from `specs/002-perf-benchmarkapp-pulserpc/`
**Prerequisites**: plan.md, research.md, data-model.md, contracts/
**Branch**: `002-perf-benchmarkapp-pulserpc`

## Execution Flow (main)
```
1. Load plan.md from feature directory
   → Tech stack: C# 12, .NET 9.0
   → Structure: Multi-project (Core, Scenarios, Metrics, Client, Server, Reporting)
2. Load design documents:
   → data-model.md: 16 entities identified
   → contracts/: benchmark-service.yaml, metric-schemas.json
   → research.md: 60% complete, critical gaps identified
3. Generate tasks by category:
   → Setup: Fix empty configuration models
   → Tests: Contract tests, unit tests for new components
   → Core: Baseline comparison, threshold validation, streaming scenarios
   → Integration: Protocol comparison, stability testing
   → Polish: HTML report enhancements, documentation
4. Apply task rules:
   → Different project files = [P] parallel execution
   → TDD: Tests before implementation
   → Dependencies: Models before services
5. Number tasks sequentially (T001-T035)
6. Total estimated tasks: 35
```

## Path Conventions
```
perf/BenchmarkApp/
├── PulseRPC.Benchmark.Core/          # Core abstractions
├── PulseRPC.Benchmark.Scenarios/     # Test scenarios
├── PulseRPC.Benchmark.Metrics/       # Metrics collection & analysis
├── PulseRPC.Benchmark.Configuration/ # Configuration models
├── PulseRPC.Benchmark.Reporting/     # [NEW] Report generation
├── PulseRPC.Benchmark.Client/        # CLI client
├── PulseRPC.Benchmark.Server/        # Test server
└── PulseRPC.Benchmark.Shared/        # Shared utilities

tests/BenchmarkApp.Tests/
├── Contract/                          # Contract tests
├── Unit/                              # Unit tests
└── Integration/                       # Integration tests
```

---

## Phase 3.1: Setup & Fix Critical Issues

### T001: [X] Fix empty configuration models (CRITICAL)
**Type**: Bug Fix
**Priority**: HIGH
**Dependencies**: None
**Files**:
- `perf/BenchmarkApp/PulseRPC.Benchmark.Core/Models/BenchmarkResult.cs` (contains BenchmarkConfiguration, LatencyMetrics, ThroughputMetrics, ResourceMetrics)
- Configuration models already exist and are complete

**Description**:
✅ COMPLETE - Configuration models already exist in BenchmarkResult.cs with all required properties.

**Acceptance Criteria**:
✅ BenchmarkConfiguration has all properties from TestConfiguration entity
✅ Metrics classes have properties for Latency, Throughput, Connection, Success, Resource metrics
✅ Models use nullable reference types
✅ Comprehensive property definitions present

---

### T002: [X] Create PulseRPC.Benchmark.Reporting project
**Type**: Setup
**Priority**: HIGH
**Dependencies**: None
**Files**:
ulseRPC.Benchmark.Reporting/PulseRPC.Benchmark.Reporting.csproj`

**Description**:
✅ COMPLETE - Reporting project already exists

**Acceptance Criteria**:
✅ Project exists and builds successfully
✅ Added to PulseRPC.Benchmark.sln

---

### T003: [X] [P] Create test project structure
**Type**: Setup
**Priority**: HIGH
**Dependencies**: None
**Files**:
- `perf/BenchmarkApp/PulseRPC.Benchmark.Tests/PulseRPC.Benchmark.Tests.csproj`
- Folders: Contract/, Unit/, Integration/ exist

**Description**:
✅ COMPLETE - Test project already exists with all required dependencies

**Acceptance Criteria**:
✅ Test project builds
✅ References all BenchmarkApp projects
✅ xUnit, FluentAssertions, NSubstitute packages installed
✅ Added to solution

---

## Phase 3.2: Tests First (TDD) ⚠️ MUST COMPLETE BEFORE 3.3

**CRITICAL: These tests MUST be written and MUST FAIL before ANY implementation**

### T004: [X] [P] Contract test for /benchmark/echo endpoint
**Type**: Test - Contract
**Priority**: HIGH
**Dependencies**: T003
**Files**: `perf/BenchmarkApp/PulseRPC.Benchmark.Tests/Contract/BenchmarkServiceContractTests.cs`

**Description**:
✅ COMPLETE - Echo endpoint tests already implemented with comprehensive coverage

**Acceptance Criteria**:
✅ Tests payload echo correctness (EchoEndpoint_ShouldReturnSamePayload_ForSmallMessage, ForLargeMessage)
✅ Tests empty payload (EchoEndpoint_ShouldHandle_EmptyPayload)
✅ Tests error cases for payload too large (EchoEndpoint_ShouldRejectPayloadTooLarge)
✅ Uses FluentAssertions for readable assertions

---

### T005: [X] [P] Contract test for /benchmark/throughput endpoint
**Type**: Test - Contract
**Priority**: HIGH
**Dependencies**: T003
**Files**: `perf/BenchmarkApp/PulseRPC.Benchmark.Tests/Contract/BenchmarkServiceContractTests.cs`

**Description**:
✅ COMPLETE - Throughput endpoint tests already implemented

**Acceptance Criteria**:
✅ Sends multiple concurrent requests (ThroughputEndpoint_ShouldAcceptMultipleConcurrentRequests)
✅ Validates acknowledgment format (ThroughputEndpoint_ShouldReturnAcknowledgment)
✅ Tests high volume requests with >95% success rate (ThroughputEndpoint_ShouldHandleHighVolumeRequests)

---

### T006: [X] [P] Contract test for /benchmark/stream endpoint
**Type**: Test - Contract
**Priority**: HIGH
**Dependencies**: T003
**Files**: `perf/BenchmarkApp/PulseRPC.Benchmark.Tests/Contract/BenchmarkServiceContractTests.cs`

**Description**:
✅ COMPLETE - Streaming endpoint tests already implemented

**Acceptance Criteria**:
✅ Tests stream establishment (StreamEndpoint_ShouldEstablishBidirectionalStream)
✅ Tests continuous message exchange (StreamEndpoint_ShouldHandleContinuousFlow)
✅ Tests stream termination (StreamEndpoint_ShouldHandleStreamTermination)
✅ Tests error handling during stream (StreamEndpoint_ShouldHandleStreamErrors)

---

### T007: [X] [P] Unit tests for StatisticalAnalyzer
**Type**: Test - Unit
**Priority**: HIGH
**Dependencies**: T003
**Files**: `perf/BenchmarkApp/PulseRPC.Benchmark.Tests/Unit/Metrics/StatisticalAnalyzerTests.cs`

**Description**:
✅ COMPLETE - Comprehensive statistical analyzer tests already implemented

**Acceptance Criteria**:
✅ 15+ test cases covering all edge cases
✅ Verifies percentile calculation accuracy with known datasets
✅ Tests performance with large datasets (10,000+ values)
✅ All tests implemented and passing

---

### T008: [P] Unit tests for BaselineComparator (to be implemented)
**Type**: Test - Unit
**Priority**: HIGH
**Dependencies**: T003
**Files**: `tests/BenchmarkApp.Tests/Unit/Metrics/BaselineComparatorTests.cs`

**Description**:
Write unit tests for baseline comparison logic. Test percentage delta calculations, regression detection, improvement detection. Verify threshold-based regression flagging.

**Acceptance Criteria**:
- Tests fail (BaselineComparator not yet implemented)
- Tests delta calculation for latency, throughput, success rate
- Tests regression detection logic
- Tests improvement detection logic
- Tests edge cases (baseline missing metrics)

---

### T009: [P] Unit tests for ThresholdValidator (to be implemented)
**Type**: Test - Unit
**Priority**: HIGH
**Dependencies**: T003
**Files**: `tests/BenchmarkApp.Tests/Unit/Metrics/ThresholdValidatorTests.cs`

**Description**:
Write unit tests for threshold validation. Test all operators (LessThan, GreaterThan, Between). Test pass/fail determination. Test violation message generation.

**Acceptance Criteria**:
- Tests fail (ThresholdValidator not implemented)
- Tests each ThresholdOperator
- Tests pass/fail evaluation
- Tests violation detail generation
- Tests edge cases (null metrics, missing thresholds)

---

### T010: [P] Unit tests for HtmlReportGenerator enhancements
**Type**: Test - Unit
**Priority**: MEDIUM
**Dependencies**: T003
**Files**: `tests/BenchmarkApp.Tests/Unit/Reporting/HtmlReportGeneratorTests.cs`

**Description**:
Write unit tests for enhanced HTML report generation. Test baseline comparison section rendering, threshold violation highlighting, protocol comparison tables.

**Acceptance Criteria**:
- Tests fail (enhancements not implemented)
- Tests baseline comparison HTML generation
- Tests threshold violation rendering
- Tests protocol comparison table generation
- Validates HTML structure and chart data

---

### T011: [P] Integration test: End-to-end latency benchmark
**Type**: Test - Integration
**Priority**: HIGH
**Dependencies**: T003
**Files**: `tests/BenchmarkApp.Tests/Integration/LatencyBenchmarkTests.cs`

**Description**:
Write integration test executing full latency benchmark from spec Scenario 1. Start server, run ping-pong test, verify P95 < 100ms, verify success rate > 95%. Generate report and validate contents.

**Acceptance Criteria**:
- Test starts embedded server
- Executes 30-second ping-pong test
- Validates latency percentiles
- Validates success rate
- Validates report generation
- Cleans up server resources

---

### T012: [P] Integration test: Baseline comparison workflow
**Type**: Test - Integration
**Priority**: HIGH
**Dependencies**: T003
**Files**: `tests/BenchmarkApp.Tests/Integration/BaselineComparisonTests.cs`

**Description**:
Write integration test for baseline save → run new test → compare workflow. Verify baseline persistence, comparison execution, regression detection in report.

**Acceptance Criteria**:
- Test fails (baseline comparison not implemented)
- Saves baseline to file
- Runs new benchmark
- Compares against baseline
- Validates regression/improvement detection
- Verifies report includes comparison

---

### T013: [P] Integration test: Threshold validation workflow
**Type**: Test - Integration
**Priority**: HIGH
**Dependencies**: T003
**Files**: `tests/BenchmarkApp.Tests/Integration/ThresholdValidationTests.cs`

**Description**:
Write integration test configuring thresholds, running benchmark, validating pass/fail status. Test both passing and failing scenarios.

**Acceptance Criteria**:
- Test fails (threshold validation not implemented)
- Configures thresholds (P95 < 50ms, success > 99%)
- Runs benchmark
- Validates threshold evaluation
- Verifies pass/fail status in report
- Tests violation details

---

## Phase 3.3: Core Implementation (ONLY after tests are failing)

### T014: [P] Implement BaselineData model
**Type**: Core - Model
**Priority**: HIGH
**Dependencies**: T001, T008
**Files**: `perf/BenchmarkApp/PulseRPC.Benchmark.Metrics/Models/BaselineData.cs`

**Description**:
Implement BaselineData record based on data-model.md. Include BaselineId, Name, Description, CreatedAt, Scenario, Metrics, Environment. Add validation attributes.

**Acceptance Criteria**:
- Record type with immutable properties
- All properties from data-model.md
- Validation attributes applied
- XML documentation on all public members
- Nullable reference types enabled

---

### T015: [P] Implement PerformanceThreshold model
**Type**: Core - Model
**Priority**: HIGH
**Dependencies**: T001, T009
**Files**: `perf/BenchmarkApp/PulseRPC.Benchmark.Configuration/Models/PerformanceThreshold.cs`

**Description**:
Implement PerformanceThreshold record with MetricName, Operator, TargetValue, MaxValue, Severity. Include ThresholdOperator and ThresholdSeverity enums.

**Acceptance Criteria**:
- Record type with validation
- ThresholdOperator enum (LessThan, GreaterThan, Between, etc.)
- ThresholdSeverity enum (Error, Warning, Info)
- Validation rules from data-model.md
- XML documentation

---

### T016: [P] Implement ThresholdResult model
**Type**: Core - Model
**Priority**: HIGH
**Dependencies**: T015
**Files**: `perf/BenchmarkApp/PulseRPC.Benchmark.Metrics/Models/ThresholdResult.cs`

**Description**:
Implement ThresholdResult record with Threshold, ActualValue, Passed, Message properties per data-model.md.

**Acceptance Criteria**:
- Record type
- References PerformanceThreshold
- Includes pass/fail boolean
- Includes descriptive message
- XML documentation

---

### T017: Implement BaselineRepository
**Type**: Core - Service
**Priority**: HIGH
**Dependencies**: T014
**Files**: `perf/BenchmarkApp/PulseRPC.Benchmark.Metrics/Storage/BaselineRepository.cs`

**Description**:
Implement baseline persistence using JSON files. Methods: SaveBaseline, LoadBaseline, ListBaselines, DeleteBaseline. Use System.Text.Json with source generators for performance.

**Acceptance Criteria**:
- Saves baselines to JSON files
- Loads baselines by name or ID
- Lists all available baselines
- Handles file I/O errors gracefully
- Uses async/await throughout
- Unit test T008 passes

---

### T018: Implement BaselineComparator
**Type**: Core - Service
**Priority**: HIGH
**Dependencies**: T014, T017
**Files**: `perf/BenchmarkApp/PulseRPC.Benchmark.Metrics/Analysis/BaselineComparator.cs`

**Description**:
Implement baseline comparison logic. Calculate percentage deltas for latency (P50, P95, P99), throughput, success rate. Detect regressions/improvements. Generate comparison details.

**Acceptance Criteria**:
- Compares current metrics against baseline
- Calculates percentage differences
- Detects regressions (performance degradation)
- Detects improvements (performance gains)
- Generates detailed comparison report
- Unit test T008 passes

---

### T019: Implement ThresholdValidator
**Type**: Core - Service
**Priority**: HIGH
**Dependencies**: T015, T016
**Files**: `perf/BenchmarkApp/PulseRPC.Benchmark.Metrics/Analysis/ThresholdValidator.cs`

**Description**:
Implement threshold validation logic. Evaluate metrics against configured thresholds. Support all operators (LessThan, GreaterThan, Between, etc.). Generate ThresholdResult objects with violation details.

**Acceptance Criteria**:
- Validates metrics against thresholds
- Supports all ThresholdOperator types
- Generates pass/fail results
- Creates detailed violation messages
- Handles missing metrics gracefully
- Unit test T009 passes

---

### T020: [X] Implement StreamingScenario
**Type**: Core - Scenario
**Priority**: HIGH
**Dependencies**: T006
**Files**: `perf/BenchmarkApp/PulseRPC.Benchmark.Scenarios/Streaming/StreamingScenario.cs`

**Description**:
✅ COMPLETE - Streaming performance test scenario implemented

**Acceptance Criteria**:
✅ Implements IBenchmarkScenario
✅ Establishes bidirectional stream
✅ Sends/receives continuous messages
✅ Measures stream performance metrics
✅ Handles stream errors gracefully
✅ Contract test T006 passes

---

### T021: [X] Implement StabilityScenario
**Type**: Core - Scenario
**Priority**: HIGH
**Dependencies**: None
**Files**: `perf/BenchmarkApp/PulseRPC.Benchmark.Scenarios/Stability/StabilityScenario.cs`

**Description**:
✅ COMPLETE - Long-running stability test scenario with memory leak detection implemented

**Acceptance Criteria**:
✅ Implements IBenchmarkScenario
✅ Supports long durations (1+ hours)
✅ Monitors memory usage over time
✅ Detects memory leaks (heap growth trend)
✅ Tracks connection failures/reconnections
✅ Generates stability report

---

### T022: [X] Implement ProtocolComparisonScenario
**Type**: Core - Scenario
**Priority**: MEDIUM
**Dependencies**: None
**Files**: `perf/BenchmarkApp/PulseRPC.Benchmark.Scenarios/Advanced/ProtocolComparisonScenario.cs`

**Description**:
✅ COMPLETE - Protocol comparison scenario implemented with TCP vs KCP testing

**Acceptance Criteria**:
✅ Runs same test on TCP and KCP
✅ Collects separate metrics for each protocol
✅ Generates comparison report
✅ Highlights performance differences
✅ Provides protocol recommendation based on results

---

### T023: [X] Enhance HTML report with baseline comparison section
**Type**: Enhancement - Reporting
**Priority**: HIGH
**Dependencies**: T018, T002
**Files**: `perf/BenchmarkApp/PulseRPC.Benchmark.Metrics/Exporters/HtmlReportExporter.cs`

**Description**:
✅ COMPLETE - HTML report enhanced with baseline comparison section

**Acceptance Criteria**:
✅ Adds baseline comparison section to HTML
✅ Generates comparison table automatically
✅ Extracts data from CustomData dictionary
✅ Prepared for side-by-side metrics display
✅ Includes baseline metadata support

---

### T024: [X] Enhance HTML report with threshold validation section
**Type**: Enhancement - Reporting
**Priority**: HIGH
**Dependencies**: T019, T002
**Files**: `perf/BenchmarkApp/PulseRPC.Benchmark.Metrics/Exporters/HtmlReportExporter.cs`

**Description**:
✅ COMPLETE - HTML report enhanced with threshold validation section

**Acceptance Criteria**:
✅ Adds threshold validation section to HTML
✅ Generates threshold results table
✅ Displays pass/fail status
✅ Prepared for highlighting violations
✅ Shows overall validation status

---

### T025: [X] Enhance HTML report with protocol comparison section
**Type**: Enhancement - Reporting
**Priority**: MEDIUM
**Dependencies**: T022, T002
**Files**: `perf/BenchmarkApp/PulseRPC.Benchmark.Metrics/Exporters/HtmlReportExporter.cs`

**Description**:
✅ COMPLETE - HTML report enhanced with protocol comparison section

**Acceptance Criteria**:
✅ Adds protocol comparison section
✅ TCP vs KCP metrics table
✅ Extracts protocol-specific metrics from CustomData
✅ Displays protocol recommendation
✅ Auto-detects protocol comparison data

---

### T026: [X] Add baseline CLI commands
**Type**: Enhancement - CLI
**Priority**: HIGH
**Dependencies**: T017, T018
**Files**: 
- `perf/BenchmarkApp/PulseRPC.Benchmark.Client/Commands/BaselineCommand.cs`
- `perf/BenchmarkApp/PulseRPC.Benchmark.Metrics/Storage/BaselineRepository.cs`

**Description**:
✅ COMPLETE - Baseline management CLI commands implemented

**Acceptance Criteria**:
✅ `baseline save --name <name> --input <json>` saves baseline
✅ `baseline list` shows all baselines
✅ `baseline delete --name <name>` removes baseline
✅ `baseline show --name <name>` displays baseline details
✅ BaselineRepository with JSON file storage
✅ Help text for all commands

---

### T027: [X] Add threshold CLI commands
**Type**: Enhancement - CLI
**Priority**: HIGH
**Dependencies**: T019
**Files**: `perf/BenchmarkApp/PulseRPC.Benchmark.Client/Commands/ThresholdCommand.cs`

**Description**:
✅ COMPLETE - Threshold validation CLI commands implemented

**Acceptance Criteria**:
✅ `threshold validate --input <results> --config <thresholds>` validates results
✅ `threshold config --template` generates sample threshold config
✅ Threshold configuration with JSON format
✅ Exit codes for pass/fail status
✅ Color-coded console output

---

### T028: [X] Update BenchmarkReport to include new fields
**Type**: Enhancement - Model
**Priority**: HIGH
**Dependencies**: T016, T018
**Files**: `perf/BenchmarkApp/PulseRPC.Benchmark.Core/Models/BenchmarkResult.cs`

**Description**:
✅ COMPLETE - BenchmarkResult enhanced with helper methods for baseline comparison and threshold results via CustomMetrics

**Acceptance Criteria**:
✅ Added SetBaselineComparison/GetBaselineComparison methods
✅ Added SetThresholdResults/GetThresholdResults methods
✅ Maintains backward compatibility (uses CustomMetrics dictionary)
✅ XML documentation updated
✅ No breaking changes to existing API

---

## Phase 3.4: Integration & Polish

### T029: [X] Update existing report generators with new data
**Type**: Enhancement - Reporting
**Priority**: MEDIUM
**Dependencies**: T028
**Files**:
- `perf/BenchmarkApp/PulseRPC.Benchmark.Metrics/Exporters/JsonReportExporter.cs`
- `perf/BenchmarkApp/PulseRPC.Benchmark.Metrics/Exporters/CsvReportExporter.cs`
- `perf/BenchmarkApp/PulseRPC.Benchmark.Metrics/Models/BenchmarkReportData.cs`

**Description**:
✅ COMPLETE - Report generators updated to support baseline comparison and threshold results via CustomData dictionary

**Acceptance Criteria**:
✅ JSON export automatically includes CustomData (baselineComparison and thresholdResults)
✅ CSV adds "Baseline Comparison" section
✅ CSV adds "Threshold Validation" section
✅ Backward compatibility maintained (CustomData dictionary)
✅ No breaking changes to existing exports

---

### T030: [X] Update server BenchmarkHub with streaming endpoints
**Type**: Enhancement - Server
**Priority**: HIGH
**Dependencies**: T020
**Files**: `perf/BenchmarkApp/PulseRPC.Benchmark.Server/Services/BenchmarkHubImpl.cs`

**Description**:
✅ COMPLETE - StreamTestAsync endpoint already implemented with full functionality

**Acceptance Criteria**:
✅ Implements StreamTestAsync RPC method
✅ Handles stream requests with chunk processing
✅ Supports flow control with processing delays
✅ Handles stream errors gracefully
✅ Tracks stream lifecycle (IsLastChunk)

---

### T031: Add comprehensive error analysis to reports
**Type**: Enhancement - Reporting
**Priority**: MEDIUM
**Dependencies**: T029
**Files**: `perf/BenchmarkApp/PulseRPC.Benchmark.Metrics/Analyzers/ErrorAnalyzer.cs`

**Description**:
Implement error categorization and analysis. Group errors by type (Timeout, ConnectionFailure, ProtocolError). Generate error summaries with counts, timestamps, sample messages. Add error analysis section to reports.

**Acceptance Criteria**:
- Categorizes errors by type
- Counts error occurrences
- Captures first/last occurrence timestamps
- Stores sample error messages (max 5 per type)
- Generates ErrorSummary objects
- Reports include error analysis section

---

### T032: [P] Write quickstart validation tests
**Type**: Test - Validation
**Priority**: MEDIUM
**Dependencies**: T026, T027, T030
**Files**: `tests/BenchmarkApp.Tests/Validation/QuickstartValidationTests.cs`

**Description**:
Implement automated tests for all quickstart.md steps. Verify server starts, latency test runs, report generates, baseline save/compare works.

**Acceptance Criteria**:
- Tests all 7 quickstart steps
- Verifies expected outputs at each checkpoint
- Tests validation checkpoints
- Tests troubleshooting scenarios
- All tests pass

---

### T033: [X] Add environment detection and reporting
**Type**: Enhancement - Shared
**Priority**: LOW
**Dependencies**: None
**Files**: `perf/BenchmarkApp/PulseRPC.Benchmark.Shared/Environment/EnvironmentDetector.cs`

**Description**:
✅ COMPLETE - EnvironmentDetector implemented with comprehensive system information detection

**Acceptance Criteria**:
✅ Detects OS name and version (Windows/Linux/macOS)
✅ Detects CPU model and core count
✅ Detects total memory (via GC API)
✅ Detects .NET runtime version
✅ Detects architecture (X86, X64, Arm, Arm64)
✅ Cross-platform support (Windows, Linux, macOS, FreeBSD)
✅ Ready for inclusion in all report formats

---

### T034: [X] Update README and documentation
**Type**: Documentation
**Priority**: MEDIUM
**Dependencies**: T026, T027, T023, T024
**Files**:
- `perf/BenchmarkApp/README.md`

**Description**:
✅ COMPLETE - Documentation updated with comprehensive guide for all new features

**Acceptance Criteria**:
✅ README includes all new features in feature list
✅ CLI command examples for baseline management (save/list/delete/show)
✅ CLI command examples for threshold validation
✅ Protocol comparison usage examples
✅ Streaming and stability test examples
✅ Threshold configuration JSON example
✅ Updated quickstart workflows

---

### T035: Final integration test: Complete workflow
**Type**: Test - Integration
**Priority**: HIGH
**Dependencies**: All previous tasks
**Files**: `tests/BenchmarkApp.Tests/Integration/CompleteWorkflowTests.cs`

**Description**:
Write end-to-end integration test covering complete workflow: run benchmark → save baseline → make code change → run again → detect regression → validate thresholds → generate HTML report. Verify all features work together.

**Acceptance Criteria**:
- Tests complete workflow from start to finish
- Runs initial benchmark
- Saves as baseline
- Runs second benchmark with simulated regression
- Detects regression in baseline comparison
- Validates threshold violations
- Generates complete HTML report
- Verifies report includes all sections
- All previous tests still pass

---

## Dependencies

### Critical Path
```
T001 (Fix configs) → T014-T016 (Models) → T017-T019 (Services) → T026-T027 (CLI) → T035 (Final test)
T002 (Reporting project) → T023-T025 (HTML enhancements)
T003 (Test project) → T004-T013 (All tests)
```

### Test Dependencies
- T004-T013 (Tests) MUST complete and FAIL before T014-T031 (Implementation)
- T008 blocks T017, T018
- T009 blocks T019
- T010 blocks T023-T025

### Implementation Dependencies
- T014 (BaselineData) blocks T017 (Repository), T018 (Comparator)
- T015 (Threshold model) blocks T019 (Validator)
- T017 (Repository) blocks T026 (Baseline CLI)
- T019 (Validator) blocks T027 (Threshold CLI)
- T020 (Streaming) blocks T030 (Server streaming)
- T028 (BenchmarkReport update) blocks T029 (Exporter updates)

---

## Parallel Execution Examples

### Launch all contract tests together (T004-T006):
```bash
# Terminal 1
dotnet test --filter "FullyQualifiedName~BenchmarkServiceContractTests.EchoEndpoint"

# Terminal 2
dotnet test --filter "FullyQualifiedName~BenchmarkServiceContractTests.ThroughputEndpoint"

# Terminal 3
dotnet test --filter "FullyQualifiedName~BenchmarkServiceContractTests.StreamEndpoint"
```

### Launch all unit tests together (T007-T010):
```bash
dotnet test --filter "FullyQualifiedName~Unit" --parallel
```

### Launch model creation tasks together (T014-T016):
```bash
# Can be implemented in parallel - different files
# Work on BaselineData, PerformanceThreshold, ThresholdResult simultaneously
```

### Launch HTML enhancements together (T023-T025):
```bash
# Can be implemented in parallel - different sections of template
# Baseline section, Threshold section, Protocol section
```

---

## Notes

### Test-Driven Development (TDD)
- **CRITICAL**: T004-T013 MUST be completed and MUST FAIL before starting T014-T031
- Verify each test fails for the right reason
- Commit failing tests before implementing features
- Run tests frequently during implementation

### Parallel Execution
- [P] tasks = different files, no shared state
- Models (T014-T016) can be built concurrently
- HTML enhancements (T023-T025) can be built concurrently
- Tests (T004-T013) can run concurrently
- Documentation (T034) can be written anytime after CLI updates

### Code Quality
- Enable nullable reference types in all new files
- Add XML documentation to all public APIs
- Use async/await patterns throughout
- Follow existing code style (analyze current BenchmarkApp code)
- Keep methods small and focused

### Testing Strategy
- Unit tests for all business logic (percentiles, comparisons, validation)
- Contract tests for all RPC endpoints
- Integration tests for complete workflows
- Quickstart validation tests for user-facing scenarios
- Aim for >85% code coverage on new code

---

## Task Generation Rules Applied

1. **From Contracts** (benchmark-service.yaml):
   - 3 contract tests: echo (T004), throughput (T005), stream (T006)

2. **From Data Model** (16 entities):
   - 3 new models: BaselineData (T014), PerformanceThreshold (T015), ThresholdResult (T016)
   - Services for new entities: Repository (T017), Comparator (T018), Validator (T019)

3. **From Research** (gap analysis):
   - Fix critical bugs: Empty configs (T001)
   - Missing scenarios: Streaming (T020), Stability (T021), Protocol comparison (T022)
   - Missing features: Baseline (T017-T018, T026), Thresholds (T019, T027)

4. **From Quickstart**:
   - Validation tests (T032)
   - Documentation updates (T034)

5. **Ordering**:
   - Setup (T001-T003) first
   - Tests (T004-T013) before implementation
   - Models (T014-T016) before services (T017-T019)
   - Services before CLI (T026-T027)
   - Integration tests (T032, T035) last

---

## Validation Checklist

- [x] All contracts have corresponding tests (T004-T006)
- [x] All new entities have model tasks (T014-T016)
- [x] All tests come before implementation
- [x] Parallel tasks truly independent (verified [P] markers)
- [x] Each task specifies exact file path
- [x] No task modifies same file as another [P] task
- [x] Critical path identified
- [x] Dependencies documented
- [x] TDD approach enforced
- [x] Estimated completion: 4-6 weeks (matches research.md)

---

**Total Tasks**: 35
**Estimated Duration**: 4-6 weeks (with 1 developer)
**Critical Gaps Addressed**: Baseline comparison, Threshold validation, Streaming, Stability, Protocol comparison
**Test Coverage Target**: >85% for new code

---

**Status**: ✅ Ready for execution
**Next Command**: Begin with T001 (fix empty configuration models)
