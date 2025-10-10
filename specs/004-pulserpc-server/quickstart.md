# Quickstart Validation Scenarios

## Overview
This document defines integration test scenarios that validate the complete message dispatch-process-response pipeline. These scenarios map directly to the acceptance scenarios in spec.md and must all pass before the feature is considered complete.

---

## Scenario 1: Normal Request-Response Flow

### Setup
```csharp
// Start server with test service
var server = new PulseServer();
server.RegisterService<TestService>("TestService");
await server.StartAsync();

// Connect test client
var client = await PulseClient.ConnectAsync("localhost:8080");
```

### Execution
```csharp
// Send request: TestService.Echo("Hello")
var request = new RpcMessage {
    RequestId = Guid.NewGuid(),
    ServiceName = "TestService",
    MethodName = "Echo",
    Payload = Serialize("Hello")
};

var response = await client.SendAsync(request);
```

### Validation
```csharp
// Assert response received
Assert.NotNull(response);
Assert.Equal(request.RequestId, response.RequestId);
Assert.True(response.IsSuccess);

// Assert correct payload
var result = Deserialize<string>(response.Payload);
Assert.Equal("Hello", result);

// Assert latency within target
Assert.True(response.DurationMs < 5, "P95 latency must be < 5ms");
```

### Expected Behavior
- ✅ Request received and parsed successfully
- ✅ Routed to TestService.Echo method
- ✅ Method executed and returned "Hello"
- ✅ Response serialized and transmitted
- ✅ Complete round trip < 5ms (P95 target)

---

## Scenario 2: Concurrent Multi-Client Load

### Setup
```csharp
// Start server
var server = new PulseServer();
server.RegisterService<TestService>("TestService");
await server.StartAsync();

// Connect 5000 clients
var clients = new List<PulseClient>();
for (int i = 0; i < 5000; i++) {
    clients.Add(await PulseClient.ConnectAsync("localhost:8080"));
}
```

### Execution
```csharp
// All clients send simultaneously (50,000 requests/second)
var tasks = clients.SelectMany(client =>
    Enumerable.Range(0, 10).Select(_ =>
        client.SendAsync(CreateTestRequest())
    )
).ToArray();

await Task.WhenAll(tasks);
```

### Validation
```csharp
// Assert all requests succeeded
var responses = tasks.Select(t => t.Result).ToArray();
Assert.Equal(50000, responses.Length);
Assert.All(responses, r => Assert.True(r.IsSuccess));

// Assert FIFO ordering per connection
foreach (var client in clients) {
    var clientResponses = GetResponsesForClient(client);
    Assert.True(IsOrderPreserved(clientResponses));
}

// Assert P99 latency under target
var latencies = responses.Select(r => r.DurationMs).ToArray();
var p99 = CalculatePercentile(latencies, 0.99);
Assert.True(p99 < 10, "P99 latency must be < 10ms under load");
```

### Expected Behavior
- ✅ All 50,000 requests processed successfully
- ✅ No requests dropped or lost
- ✅ FIFO ordering maintained per client connection
- ✅ Load distributed across CPU cores
- ✅ P99 latency < 10ms during sustained load

---

## Scenario 3: Service Method Throws Exception

### Setup
```csharp
// Register service with failing method
server.RegisterService<FaultyService>("FaultyService");
```

### Execution
```csharp
// Call method that throws ArgumentException
var request = new RpcMessage {
    ServiceName = "FaultyService",
    MethodName = "ThrowException",
    Payload = Serialize("trigger")
};

var response = await client.SendAsync(request);
```

### Validation
```csharp
// Assert error response received
Assert.NotNull(response);
Assert.False(response.IsSuccess);
Assert.NotNull(response.ExceptionDetails);

// Assert exception details correct
Assert.Equal("System.ArgumentException", response.ExceptionDetails.ExceptionType);
Assert.Contains("trigger", response.ExceptionDetails.Message);
Assert.NotNull(response.ExceptionDetails.StackTrace);

// Assert server still processing other requests
var healthRequest = CreateHealthCheckRequest();
var healthResponse = await client.SendAsync(healthRequest);
Assert.True(healthResponse.IsSuccess, "Server must continue after exception");
```

### Expected Behavior
- ✅ Exception caught without crashing server
- ✅ Exception serialized to structured error response
- ✅ Error response sent to client
- ✅ Other requests continue processing normally

---

## Scenario 4: Slow Service Method

### Setup
```csharp
// Register service with slow method (5 seconds)
server.RegisterService<SlowService>("SlowService");
server.ConfigureTimeout("SlowService", TimeSpan.FromSeconds(2)); // 2s timeout
```

### Execution
```csharp
// Call slow method that takes 5 seconds
var request = new RpcMessage {
    ServiceName = "SlowService",
    MethodName = "SlowOperation",
    Payload = Serialize(5000) // 5000ms delay
};

var response = await client.SendAsync(request, timeout: TimeSpan.FromSeconds(3));
```

### Validation
```csharp
// Assert timeout error received
Assert.NotNull(response);
Assert.False(response.IsSuccess);
Assert.Contains("timeout", response.ExceptionDetails.Message.ToLower());

// Assert other clients not blocked
var fastClient = clients[0];
var fastRequest = CreateFastRequest();
var stopwatch = Stopwatch.StartNew();
var fastResponse = await fastClient.SendAsync(fastRequest);
stopwatch.Stop();

Assert.True(stopwatch.ElapsedMilliseconds < 100, "Fast requests must not be blocked by slow ones");
```

### Expected Behavior
- ✅ Timeout enforced via CancellationToken
- ✅ Slow operation cancelled after 2 seconds
- ✅ Timeout error response sent to client
- ✅ Other clients' requests processed concurrently

---

## Scenario 5: Message Parsing Failure

### Setup
```csharp
// Server running normally
var server = await StartServerAsync();
```

### Execution
```csharp
// Send malformed/corrupted data directly to socket
var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
await socket.ConnectAsync("localhost", 8080);

// Send garbage data (not valid RPC message)
byte[] garbageData = new byte[] { 0xFF, 0xFE, 0xFD, 0xFC, /* ... */ };
await socket.SendAsync(garbageData, SocketFlags.None);

// Attempt to read response
var buffer = new byte[1024];
var bytesReceived = await socket.ReceiveAsync(buffer, SocketFlags.None);
var response = ParseResponse(buffer, bytesReceived);
```

### Validation
```csharp
// Assert protocol error response received
Assert.NotNull(response);
Assert.False(response.IsSuccess);
Assert.Equal(MessageType.Error, response.MessageType);
Assert.Contains("protocol", response.ExceptionDetails.Message.ToLower());

// Assert server still healthy
var normalClient = await PulseClient.ConnectAsync("localhost:8080");
var healthResponse = await normalClient.SendAsync(CreateHealthCheckRequest());
Assert.True(healthResponse.IsSuccess);
```

### Expected Behavior
- ✅ Malformed message detected during parsing
- ✅ Server does not crash or enter invalid state
- ✅ Protocol error response sent to client
- ✅ Malformed message logged for debugging
- ✅ Server continues processing valid messages

---

## Scenario 6: Connection Loss During Processing

### Setup
```csharp
// Server with long-running operation
server.RegisterService<LongRunningService>("LongRunningService");
```

### Execution
```csharp
// Start long operation
var client = await PulseClient.ConnectAsync("localhost:8080");
var request = new RpcMessage {
    ServiceName = "LongRunningService",
    MethodName = "ProcessFor10Seconds"
};

var responseTask = client.SendAsync(request);

// Drop connection mid-processing
await Task.Delay(1000); // Let it start processing
client.Disconnect(); // Simulate network failure
```

### Validation
```csharp
// Assert operation was cancelled
await Assert.ThrowsAsync<OperationCanceledException>(() => responseTask);

// Assert server cleaned up resources
await Task.Delay(500); // Give time for cleanup
var serverMetrics = server.GetMetrics();
Assert.Equal(0, serverMetrics.ActiveRequests);
Assert.Equal(0, serverMetrics.PendingResponses);

// Assert new connections still work
var newClient = await PulseClient.ConnectAsync("localhost:8080");
var testResponse = await newClient.SendAsync(CreateTestRequest());
Assert.True(testResponse.IsSuccess);
```

### Expected Behavior
- ✅ Connection loss detected
- ✅ In-flight operation cancelled via CancellationToken
- ✅ Resources cleaned up (memory, file handles)
- ✅ Client removed from active connection list
- ✅ No attempt to send response to disconnected client

---

## Scenario 7: Backpressure Under Extreme Load

### Setup
```csharp
// Server with limited capacity
var serverOptions = new ServerOptions {
    MaxQueueDepth = 1000,
    BackpressureThreshold = 0.9, // 90% capacity
    ConnectionThrottleThreshold = 0.95 // 95% capacity
};
var server = new PulseServer(serverOptions);
await server.StartAsync();
```

### Execution
```csharp
// Flood server with 10x normal capacity
var clients = await ConnectClients(10000);
var requestTasks = new List<Task>();

foreach (var client in clients) {
    for (int i = 0; i < 100; i++) {
        requestTasks.Add(client.SendAsync(CreateTestRequest()));
    }
}

// 1,000,000 requests at once (far exceeds capacity)
await Task.WhenAll(requestTasks);
```

### Validation
```csharp
// Assert server applied backpressure
var metrics = server.GetMetrics();
Assert.True(metrics.BackpressureActivated, "Backpressure should have triggered");
Assert.True(metrics.RejectedConnectionCount > 0, "Some connections should be rejected");

// Assert server did not crash or OOM
Assert.True(server.IsHealthy);
Assert.True(metrics.MemoryUsageMB < serverOptions.MaxMemoryMB);

// Assert system recovered after load subsided
await Task.Delay(10000); // Wait for queue to drain
var postLoadMetrics = server.GetMetrics();
Assert.True(postLoadMetrics.QueueDepth < 100, "Queue should drain");
Assert.False(postLoadMetrics.BackpressureActivated, "Backpressure should release");

// Assert new requests processed normally
var testClient = await PulseClient.ConnectAsync("localhost:8080");
var testResponse = await testClient.SendAsync(CreateTestRequest());
Assert.True(testResponse.IsSuccess);
```

### Expected Behavior
- ✅ Queue depth monitoring detects overload
- ✅ Connection accept rate throttled at 90% capacity
- ✅ New connections rejected with 503 at 100% capacity
- ✅ Existing clients continue processing
- ✅ No out-of-memory errors
- ✅ System recovers automatically when load decreases

---

## Performance Validation

### Throughput Test (FR-032)
```csharp
[Fact]
public async Task Server_MustAchieve100K_RequestsPerSecond() {
    // Setup: 8-core server, 5000 clients, small payloads
    var throughput = await MeasureThroughput(
        duration: TimeSpan.FromSeconds(60),
        clients: 5000,
        payloadSize: 256
    );

    Assert.True(throughput >= 100_000,
        $"Throughput {throughput} req/s must be >= 100K req/s");
}
```

### Latency Test (FR-033, FR-034)
```csharp
[Fact]
public async Task Server_MustMaintainLatencyTargets_UnderNormalLoad() {
    // Setup: 50% capacity, small payloads
    var latencies = await MeasureLatencies(
        duration: TimeSpan.FromSeconds(60),
        loadPercent: 0.5,
        payloadSize: 1024
    );

    var p95 = CalculatePercentile(latencies, 0.95);
    var p99 = CalculatePercentile(latencies, 0.99);

    Assert.True(p95 < 5, $"P95 {p95}ms must be < 5ms");
    Assert.True(p99 < 10, $"P99 {p99}ms must be < 10ms");
}
```

### Scalability Test (FR-035)
```csharp
[Fact]
public async Task Server_MustSupport10K_ConcurrentConnections() {
    // Gradually ramp up connections
    var clients = new List<PulseClient>();
    for (int i = 0; i < 10_000; i++) {
        var client = await PulseClient.ConnectAsync("localhost:8080");
        clients.Add(client);

        if (i % 100 == 0) {
            await Task.Delay(10); // Gradual ramp
        }
    }

    // Verify all connected
    Assert.Equal(10_000, clients.Count);
    Assert.All(clients, c => Assert.True(c.IsConnected));

    // Verify latency stable with 10K connections
    var testResponse = await clients[0].SendAsync(CreateTestRequest());
    Assert.True(testResponse.DurationMs < 10);
}
```

---

## Success Criteria

All scenarios must pass with:
- ✅ 100% success rate (no false negatives)
- ✅ Performance targets met
- ✅ No resource leaks detected
- ✅ All assertions pass

Feature is complete when:
1. All 7 integration scenarios pass
2. All 3 performance tests pass
3. 72-hour stress test completes without degradation
4. Production canary deployment succeeds
