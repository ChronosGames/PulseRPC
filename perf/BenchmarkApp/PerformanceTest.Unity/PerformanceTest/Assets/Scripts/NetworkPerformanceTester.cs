using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

/// <summary>
/// Unified network performance testing tool for Unity.
/// Supports comparison between:
/// - Unity's built-in NetworkClient
/// - Custom network libraries
/// - ASIO-based implementations
/// </summary>
public class NetworkPerformanceTester : MonoBehaviour
{
    #region UI Components
    [Header("Connection Settings")]
    [SerializeField] private InputField hostInput;
    [SerializeField] private InputField portInput;
    [SerializeField] private InputField connectionsInput;
    [SerializeField] private InputField messagesPerConnectionInput;
    [SerializeField] private InputField messageSizeInput;
    [SerializeField] private InputField durationInput;
    [SerializeField] private Dropdown protocolDropdown;
    [SerializeField] private Dropdown networkLibraryDropdown;

    [Header("Test Controls")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button stopButton;
    [SerializeField] private Button compareButton;

    [Header("Status Display")]
    [SerializeField] private Text statusText;
    [SerializeField] private Text resultsText;
    [SerializeField] private Slider progressSlider;
    [SerializeField] private Image progressBarFill;
    [SerializeField] private Text progressPercentText;
    [SerializeField] private ScrollRect resultsScrollRect;

    [Header("Comparison Charts")]
    [SerializeField] private GameObject chartsPanel;
    [SerializeField] private RectTransform connectionsChart;
    [SerializeField] private RectTransform throughputChart;
    [SerializeField] private RectTransform latencyChart;
    #endregion

    #region Test Parameters
    // Test parameters
    private string _host = "localhost";
    private int _port = 7777;
    private int _connectionCount = 100;
    private int _messagesPerConnection = 10;
    private int _messageSize = 1024;
    private int _durationSeconds = 30;
    private NetworkProtocol _protocol = NetworkProtocol.TCP;
    private NetworkLibrary _networkLibrary = NetworkLibrary.UnityNetwork;

    // Test state
    private bool _isRunning = false;
    private CancellationTokenSource _cts;

    // Clients collections for different libraries
    private readonly List<NetworkClient> _unityNetworkClients = new List<NetworkClient>();
    private readonly List<TcpClient> _asioTcpClients = new List<TcpClient>();
    private readonly List<CustomNetworkClient> _customNetworkClients = new List<CustomNetworkClient>();

    // Results for comparison
    private readonly List<TestResult> _testResults = new List<TestResult>();
    #endregion

    #region Enums
    public enum NetworkProtocol
    {
        TCP,
        UDP,
        WebSockets
    }

    public enum NetworkLibrary
    {
        UnityNetwork,
        CustomNetwork,
        ASIO
    }
    #endregion

    #region Core Metrics
    // Test metrics
    private int _totalConnections = 0;
    private int _successfulConnections = 0;
    private int _failedConnections = 0;
    private int _messagesSent = 0;
    private long _bytesTransferred = 0;
    private int _messagesReceived = 0;
    private long _bytesReceived = 0;
    private int _connectExceptions = 0;
    private int _sendExceptions = 0;
    private int _receiveExceptions = 0;
    private int _otherExceptions = 0;
    private readonly List<long> _latencies = new List<long>();
    private readonly List<long> _roundTripTimes = new List<long>();
    private Stopwatch _totalTime = new Stopwatch();
    #endregion

    #region Initialization
    void Start()
    {
        // Initialize UI components
        InitializeUI();

        // Set default values
        hostInput.text = _host;
        portInput.text = _port.ToString();
        connectionsInput.text = _connectionCount.ToString();
        messagesPerConnectionInput.text = _messagesPerConnection.ToString();
        messageSizeInput.text = _messageSize.ToString();
        durationInput.text = _durationSeconds.ToString();

        // Populate protocol dropdown
        protocolDropdown.ClearOptions();
        protocolDropdown.AddOptions(Enum.GetNames(typeof(NetworkProtocol)).ToList());

        // Populate network library dropdown
        networkLibraryDropdown.ClearOptions();
        networkLibraryDropdown.AddOptions(Enum.GetNames(typeof(NetworkLibrary)).ToList());

        // UI event listeners
        startButton.onClick.AddListener(StartTest);
        stopButton.onClick.AddListener(StopTest);
        compareButton.onClick.AddListener(CompareResults);
        stopButton.interactable = false;
        compareButton.interactable = _testResults.Count > 1;

        // Hide charts initially
        if (chartsPanel != null)
            chartsPanel.SetActive(false);
    }

    private void InitializeUI()
    {
        // This method should be called if any UI components are null
        if (statusText == null)
        {
            Debug.LogError("Status Text UI component not assigned!");
        }

        if (resultsText == null)
        {
            Debug.LogError("Results Text UI component not assigned!");
        }

        // More UI component checks...
    }
    #endregion

    #region Test Control Methods
    private void StartTest()
    {
        if (_isRunning) return;

        // Parse input parameters
        if (!ParseInputs()) return;

        // Reset metrics
        ResetMetrics();

        // Update UI
        startButton.interactable = false;
        stopButton.interactable = true;
        compareButton.interactable = false;
        statusText.text = "Starting test...";
        resultsText.text = "";
        progressSlider.value = 0;
        progressPercentText.text = "0%";

        // Hide charts if visible
        if (chartsPanel != null)
            chartsPanel.SetActive(false);

        // Start the test
        _isRunning = true;
        _cts = new CancellationTokenSource();

        // Start the test in a background thread
        Task.Run(() => RunTestAsync(_cts.Token));

        // Start coroutine for UI updates
        StartCoroutine(UpdateStatusUI());
    }

    private void StopTest()
    {
        if (!_isRunning) return;

        statusText.text = "Stopping test...";
        _cts?.Cancel();
        _isRunning = false;

        // Update UI
        startButton.interactable = true;
        stopButton.interactable = false;

        // Save results for comparison
        SaveTestResults();

        // Display results
        DisplayResults();
    }

    private void CompareResults()
    {
        if (_testResults.Count < 2)
        {
            statusText.text = "Need at least two test results to compare.";
            return;
        }

        // Display comparison charts
        DisplayComparisonCharts();
    }

    private bool ParseInputs()
    {
        // Host
        _host = hostInput.text;
        if (string.IsNullOrEmpty(_host))
        {
            statusText.text = "Error: Host cannot be empty";
            return false;
        }

        // Port
        if (!int.TryParse(portInput.text, out _port) || _port < 1 || _port > 65535)
        {
            statusText.text = "Error: Invalid port number (1-65535)";
            return false;
        }

        // Connection count
        if (!int.TryParse(connectionsInput.text, out _connectionCount) || _connectionCount < 1)
        {
            statusText.text = "Error: Invalid connection count";
            return false;
        }

        // Messages per connection
        if (!int.TryParse(messagesPerConnectionInput.text, out _messagesPerConnection) || _messagesPerConnection < 1)
        {
            statusText.text = "Error: Invalid messages per connection";
            return false;
        }

        // Message size
        if (!int.TryParse(messageSizeInput.text, out _messageSize) || _messageSize < 1)
        {
            statusText.text = "Error: Invalid message size";
            return false;
        }

        // Duration
        if (!int.TryParse(durationInput.text, out _durationSeconds) || _durationSeconds < 0)
        {
            statusText.text = "Error: Invalid duration";
            return false;
        }

        // Protocol
        _protocol = (NetworkProtocol)protocolDropdown.value;

        // Network Library
        _networkLibrary = (NetworkLibrary)networkLibraryDropdown.value;

        return true;
    }

    private void ResetMetrics()
    {
        _totalConnections = 0;
        _successfulConnections = 0;
        _failedConnections = 0;
        _messagesSent = 0;
        _bytesTransferred = 0;
        _messagesReceived = 0;
        _bytesReceived = 0;
        _connectExceptions = 0;
        _sendExceptions = 0;
        _receiveExceptions = 0;
        _otherExceptions = 0;
        _latencies.Clear();
        _roundTripTimes.Clear();
        _totalTime.Reset();

        // Clear client collections
        _unityNetworkClients.Clear();
        _asioTcpClients.Clear();
        _customNetworkClients.Clear();
    }
    #endregion

    #region Test Execution Methods
    private async Task RunTestAsync(CancellationToken cancellationToken)
    {
        _totalTime.Start();

        Debug.Log($"Starting {_networkLibrary} load test: {_host}:{_port}, Protocol: {_protocol}");
        Debug.Log($"Target: {_connectionCount} connections with {_messagesPerConnection} messages per connection");

        try
        {
            // Create task list for all connections
            var tasks = new List<Task>();

            // Create all connections
            for (int i = 0; i < _connectionCount && !cancellationToken.IsCancellationRequested; i++)
            {
                tasks.Add(CreateConnectionAsync(i, cancellationToken));

                // Small delay between connection attempts to avoid overwhelming the server
                await Task.Delay(50, cancellationToken);
            }

            // If duration is specified, wait for that time before stopping
            if (_durationSeconds > 0)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_durationSeconds), cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    // Normal if test is manually cancelled
                }
            }

            // Wait for all connection tasks to complete
            await Task.WhenAll(tasks.Where(t => !t.IsCanceled));
        }
        catch (OperationCanceledException)
        {
            Debug.Log("Test cancelled");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error in test: {ex.Message}");
            _otherExceptions++;
        }
        finally
        {
            // Disconnect all clients
            await CleanupClientsAsync();

            _totalTime.Stop();
            _isRunning = false;

            UnityMainThreadDispatcher.Instance().Enqueue(() => {
                startButton.interactable = true;
                stopButton.interactable = false;
                compareButton.interactable = _testResults.Count > 1;
                statusText.text = "Test completed";
                DisplayResults();
            });
        }
    }

    private async Task CreateConnectionAsync(int connectionId, CancellationToken cancellationToken)
    {
        _totalConnections++;

        try
        {
            var connectStopwatch = Stopwatch.StartNew();

            // Create connection based on library type
            bool success = false;

            switch (_networkLibrary)
            {
                case NetworkLibrary.UnityNetwork:
                    success = await CreateUnityNetworkClientAsync(connectionId, cancellationToken);
                    break;
                case NetworkLibrary.CustomNetwork:
                    success = await CreateCustomNetworkClientAsync(connectionId, cancellationToken);
                    break;
                case NetworkLibrary.ASIO:
                    success = await CreateAsioClientAsync(connectionId, cancellationToken);
                    break;
            }

            connectStopwatch.Stop();

            if (success)
            {
                _latencies.Add(connectStopwatch.ElapsedMilliseconds);
                _successfulConnections++;

                // Send messages
                await SendMessagesAsync(connectionId, cancellationToken);
            }
            else
            {
                _failedConnections++;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _failedConnections++;
            _otherExceptions++;
            Debug.LogError($"Connection {connectionId} error: {ex.Message}");
        }
    }
    #endregion

    #region Unity NetworkClient Implementation
    private async Task<bool> CreateUnityNetworkClientAsync(int connectionId, CancellationToken cancellationToken)
    {
        NetworkClient client = null;

        // Use Unity's main thread dispatcher for Unity API calls
        await UnityMainThreadDispatcher.Instance().EnqueueAsync(() => {
            try
            {
                var config = new ConnectionConfig();

                switch (_protocol)
                {
                    case NetworkProtocol.TCP:
                        config.AddChannel(QosType.Reliable);
                        break;
                    case NetworkProtocol.UDP:
                        config.AddChannel(QosType.Unreliable);
                        break;
                    case NetworkProtocol.WebSockets:
                        config.AddChannel(QosType.Reliable);
                        break;
                }

                client = new NetworkClient();
                client.Configure(config, 1);

                // Set up client handlers
                client.RegisterHandler(MsgType.Connect, OnUnityClientConnected);
                client.RegisterHandler(MsgType.Error, OnUnityClientError);
                client.RegisterHandler(MsgType.Disconnect, OnUnityClientDisconnected);
                client.RegisterHandler(100, OnUnityClientMessageReceived); // Custom message type

                // Connect to server
                if (_protocol == NetworkProtocol.WebSockets)
                {
                    client.ConnectWithSimulator(_host, _port, 50, 0.1f); // Using simulator for websockets
                }
                else
                {
                    client.Connect(_host, _port);
                }

                _unityNetworkClients.Add(client);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to create Unity NetworkClient {connectionId}: {ex.Message}");
                _connectExceptions++;
                client = null;
            }
        });

        return client != null;
    }

    private void OnUnityClientConnected(NetworkMessage netMsg)
    {
        Debug.Log($"Unity client connected: Connection ID {netMsg.conn.connectionId}");
    }

    private void OnUnityClientError(NetworkMessage netMsg)
    {
        Debug.LogError($"Unity client error: Connection ID {netMsg.conn.connectionId}");
    }

    private void OnUnityClientDisconnected(NetworkMessage netMsg)
    {
        Debug.Log($"Unity client disconnected: Connection ID {netMsg.conn.connectionId}");
    }

    private void OnUnityClientMessageReceived(NetworkMessage netMsg)
    {
        // Process received message
        _messagesReceived++;
        _bytesReceived += netMsg.reader.Length;

        // Calculate round trip time if the message contains timing information
        try
        {
            if (netMsg.reader.Length >= 8)
            {
                long timestamp = netMsg.reader.ReadInt64();
                long rtt = (long)(DateTime.UtcNow - new DateTime(timestamp)).TotalMilliseconds;
                if (rtt > 0 && rtt < 100000) // Sanity check
                {
                    _roundTripTimes.Add(rtt);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error processing message timestamp: {ex.Message}");
        }
    }
    #endregion

    #region Custom Network Implementation
    private async Task<bool> CreateCustomNetworkClientAsync(int connectionId, CancellationToken cancellationToken)
    {
        try
        {
            // Create instance of your custom network client
            var client = new CustomNetworkClient();

            // Connect to server
            bool connected = await client.ConnectAsync(_host, _port, _protocol);

            if (connected)
            {
                // Setup message handler
                client.OnMessageReceived += (sender, messageEventArgs) => {
                    _messagesReceived++;
                    _bytesReceived += messageEventArgs.MessageSize;

                    // Process round trip time
                    if (messageEventArgs.RoundTripTime > 0)
                    {
                        _roundTripTimes.Add(messageEventArgs.RoundTripTime);
                    }
                };

                _customNetworkClients.Add(client);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to create custom network client {connectionId}: {ex.Message}");
            _connectExceptions++;
            return false;
        }
    }
    #endregion

    #region ASIO Implementation
    private async Task<bool> CreateAsioClientAsync(int connectionId, CancellationToken cancellationToken)
    {
        try
        {
            // Using standard TcpClient as a stand-in for ASIO
            // In a real implementation, you would use your ASIO-based client
            var client = new TcpClient();

            // Connect to server
            await client.ConnectAsync(_host, _port);

            if (client.Connected)
            {
                _asioTcpClients.Add(client);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to create ASIO client {connectionId}: {ex.Message}");
            _connectExceptions++;
            return false;
        }
    }
    #endregion

    #region Message Sending Methods
    private async Task SendMessagesAsync(int connectionId, CancellationToken cancellationToken)
    {
        // Create test message
        byte[] message = new byte[_messageSize];

        // Include timestamp in the first 8 bytes for round-trip calculation
        byte[] timestampBytes = BitConverter.GetBytes(DateTime.UtcNow.Ticks);
        Array.Copy(timestampBytes, message, Math.Min(8, _messageSize));

        // Fill rest with incrementing values
        for (int i = 8; i < _messageSize; i++)
        {
            message[i] = (byte)(i % 256);
        }

        // Send messages
        for (int i = 0; i < _messagesPerConnection && !cancellationToken.IsCancellationRequested; i++)
        {
            try
            {
                var sendStopwatch = Stopwatch.StartNew();

                bool sent = false;

                switch (_networkLibrary)
                {
                    case NetworkLibrary.UnityNetwork:
                        sent = await SendUnityNetworkMessageAsync(connectionId, message, cancellationToken);
                        break;
                    case NetworkLibrary.CustomNetwork:
                        sent = await SendCustomNetworkMessageAsync(connectionId, message, cancellationToken);
                        break;
                    case NetworkLibrary.ASIO:
                        sent = await SendAsioMessageAsync(connectionId, message, cancellationToken);
                        break;
                }

                sendStopwatch.Stop();

                if (sent)
                {
                    _latencies.Add(sendStopwatch.ElapsedMilliseconds);
                    _messagesSent++;
                    _bytesTransferred += _messageSize;
                }

                // Small delay between messages
                await Task.Delay(100, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _sendExceptions++;
                Debug.LogError($"Send error on client {connectionId}: {ex.Message}");
            }
        }
    }

    private async Task<bool> SendUnityNetworkMessageAsync(int connectionId, byte[] message, CancellationToken cancellationToken)
    {
        if (connectionId >= _unityNetworkClients.Count)
            return false;

        bool sent = false;
        await UnityMainThreadDispatcher.Instance().EnqueueAsync(() => {
            try
            {
                var client = _unityNetworkClients[connectionId];
                if (client != null && client.isConnected)
                {
                    // Create network message
                    var msgType = 100; // Custom message type
                    var writer = new NetworkWriter();
                    writer.StartMessage(msgType);
                    writer.Write(message);
                    writer.FinishMessage();

                    // Send message
                    client.SendWriter(writer, 0);
                    sent = true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Unity Network send error: {ex.Message}");
                _sendExceptions++;
            }
        });

        return sent;
    }

    private async Task<bool> SendCustomNetworkMessageAsync(int connectionId, byte[] message, CancellationToken cancellationToken)
    {
        if (connectionId >= _customNetworkClients.Count)
            return false;

        try
        {
            var client = _customNetworkClients[connectionId];
            return await client.SendMessageAsync(message);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Custom Network send error: {ex.Message}");
            _sendExceptions++;
            return false;
        }
    }

    private async Task<bool> SendAsioMessageAsync(int connectionId, byte[] message, CancellationToken cancellationToken)
    {
        if (connectionId >= _asioTcpClients.Count)
            return false;

        try
        {
            var client = _asioTcpClients[connectionId];
            if (client != null && client.Connected)
            {
                var stream = client.GetStream();
                if (stream.CanWrite)
                {
                    await stream.WriteAsync(message, 0, message.Length, cancellationToken);
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"ASIO send error: {ex.Message}");
            _sendExceptions++;
            return false;
        }
    }
    #endregion

    #region Cleanup Methods
    private async Task CleanupClientsAsync()
    {
        // Clean up Unity Network clients
        await UnityMainThreadDispatcher.Instance().EnqueueAsync(() => {
            foreach (var client in _unityNetworkClients)
            {
                try
                {
                    if (client != null)
                    {
                        client.Disconnect();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error closing Unity client: {ex.Message}");
                }
            }
            _unityNetworkClients.Clear();
        });

        // Clean up Custom Network clients
        foreach (var client in _customNetworkClients)
        {
            try
            {
                await client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error closing custom client: {ex.Message}");
            }
        }
        _customNetworkClients.Clear();

        // Clean up ASIO clients
        foreach (var client in _asioTcpClients)
        {
            try
            {
                client.Close();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error closing ASIO client: {ex.Message}");
            }
        }
        _asioTcpClients.Clear();
    }
    #endregion

    #region UI Update Methods
    private IEnumerator UpdateStatusUI()
    {
        while (_isRunning)
        {
            // Update status text
            var elapsedSeconds = _totalTime.Elapsed.TotalSeconds;

            if (elapsedSeconds > 0)
            {
                var connectionsPerSecond = _totalConnections / elapsedSeconds;
                var messagesPerSecond = _messagesSent / elapsedSeconds;
                var mbPerSecond = _bytesTransferred / (1024.0 * 1024.0) / elapsedSeconds;

                statusText.text = $"Time: {elapsedSeconds:F1}s | " +
                                  $"Conn: {_successfulConnections}/{_totalConnections} | " +
                                  $"Msgs: {_messagesSent} ({messagesPerSecond:F1}/s) | " +
                                  $"Data: {_bytesTransferred / (1024.0 * 1024.0):F2} MB ({mbPerSecond:F2} MB/s)";
            }

            // Update progress bar
            if (_durationSeconds > 0)
            {
                float progress = (float)Math.Min(1.0, _totalTime.Elapsed.TotalSeconds / _durationSeconds);
                progressSlider.value = progress;
                progressPercentText.text = $"{(progress * 100):F0}%";

                // Adjust color based on progress
                if (progressBarFill != null)
                {
                    progressBarFill.color = Color.Lerp(Color.green, Color.red, progress);
                }
            }

            yield return new WaitForSeconds(0.5f);
        }
    }

    private void DisplayResults()
    {
        if (_totalTime.Elapsed.TotalSeconds <= 0)
        {
            resultsText.text = "No results to display";
            return;
        }

        var elapsedSeconds = _totalTime.Elapsed.TotalSeconds;
        var sb = new StringBuilder();

        sb.AppendLine($"TEST RESULTS - {_networkLibrary} ({_protocol})");
        sb.AppendLine("======================================");
        sb.AppendLine($"Test duration: {_totalTime.Elapsed.TotalSeconds:F1} seconds");
        sb.AppendLine($"Protocol: {_protocol}");
        sb.AppendLine($"Library: {_networkLibrary}");
        sb.AppendLine($"Connections: {_successfulConnections} successful, {_failedConnections} failed, {_totalConnections} total");
        sb.AppendLine($"Connection rate: {_successfulConnections / elapsedSeconds:F1} connections/second");
        sb.AppendLine($"Messages sent: {_messagesSent}");
        sb.AppendLine($"Message rate: {_messagesSent / elapsedSeconds:F1} messages/second");
        sb.AppendLine($"Data transferred: {_bytesTransferred / (1024.0 * 1024.0):F2} MB");
        sb.AppendLine($"Transfer rate: {_bytesTransferred / (1024.0 * 1024.0) / elapsedSeconds:F2} MB/second");

        if (_messagesReceived > 0)
        {
            sb.AppendLine($"Messages received: {_messagesReceived}");
            sb.AppendLine($"Data received: {_bytesReceived / (1024.0 * 1024.0):F2} MB");
            sb.AppendLine($"Receive rate: {_bytesReceived / (1024.0 * 1024.0) / elapsedSeconds:F2} MB/second");
            sb.AppendLine($"Packet loss: {(1.0 - ((double)_messagesReceived / _messagesSent)) * 100:F2}%");
        }

        if (_latencies.Count > 0)
        {
            var latencyArray = _latencies.ToArray();
            Array.Sort(latencyArray);

            sb.AppendLine("\nLatency Statistics (ms):");
            sb.AppendLine($"  Min: {latencyArray.Min()}");
            sb.AppendLine($"  Avg: {latencyArray.Average():F1}");
            sb.AppendLine($"  Max: {latencyArray.Max()}");
            sb.AppendLine($"  Median: {GetMedian(latencyArray):F1}");
            sb.AppendLine($"  P90: {GetPercentile(latencyArray, 0.9):F1}");
            sb.AppendLine($"  P95: {GetPercentile(latencyArray, 0.95):F1}");
            sb.AppendLine($"  P99: {GetPercentile(latencyArray, 0.99):F1}");
        }

        if (_roundTripTimes.Count > 0)
        {
            var rttArray = _roundTripTimes.ToArray();
            Array.Sort(rttArray);

            sb.AppendLine("\nRound-Trip Time Statistics (ms):");
            sb.AppendLine($"  Min: {rttArray.Min()}");
            sb.AppendLine($"  Avg: {rttArray.Average():F1}");
            sb.AppendLine($"  Max: {rttArray.Max()}");
            sb.AppendLine($"  Median: {GetMedian(rttArray):F1}");
            sb.AppendLine($"  P90: {GetPercentile(rttArray, 0.9):F1}");
            sb.AppendLine($"  P95: {GetPercentile(rttArray, 0.95):F1}");
            sb.AppendLine($"  P99: {GetPercentile(rttArray, 0.99):F1}");
        }

        sb.AppendLine("\nError Statistics:");
        sb.AppendLine($"  Connection errors: {_connectExceptions}");
        sb.AppendLine($"  Send errors: {_sendExceptions}");
        sb.AppendLine($"  Receive errors: {_receiveExceptions}");
        sb.AppendLine($"  Other errors: {_otherExceptions}");

        resultsText.text = sb.ToString();

        // Log results to console as well
        Debug.Log(sb.ToString());
    }
    #endregion

    #region Test Results Handling
    private void SaveTestResults()
    {
        // Create test result object
        var result = new TestResult()
        {
            Timestamp = DateTime.Now,
            Library = _networkLibrary,
            Protocol = _protocol,
            Duration = _totalTime.Elapsed,
            ConnectionCount = _successfulConnections,
            TotalConnections = _totalConnections,
            MessageCount = _messagesSent,
            MessageSize = _messageSize,
            BytesTransferred = _bytesTransferred,
            MessagesReceived = _messagesReceived,
            BytesReceived = _bytesReceived,
            ThroughputMBps = _bytesTransferred / (1024.0 * 1024.0) / _totalTime.Elapsed.TotalSeconds,
            ConnectionsPerSecond = _successfulConnections / _totalTime.Elapsed.TotalSeconds,
            MessagesPerSecond = _messagesSent / _totalTime.Elapsed.TotalSeconds,
            AverageLatencyMs = _latencies.Count > 0 ? _latencies.Average() : 0,
            MedianLatencyMs = _latencies.Count > 0 ? GetMedian(_latencies.ToArray()) : 0,
            P95LatencyMs = _latencies.Count > 0 ? GetPercentile(_latencies.ToArray(), 0.95) : 0,
            AverageRttMs = _roundTripTimes.Count > 0 ? _roundTripTimes.Average() : 0,
            MedianRttMs = _roundTripTimes.Count > 0 ? GetMedian(_roundTripTimes.ToArray()) : 0,
            P95RttMs = _roundTripTimes.Count > 0 ? GetPercentile(_roundTripTimes.ToArray(), 0.95) : 0,
            ErrorCount = _connectExceptions + _sendExceptions + _receiveExceptions + _otherExceptions,
            PacketLossPercent = _messagesSent > 0 ? (1.0 - ((double)_messagesReceived / _messagesSent)) * 100 : 0
        };

        // Add to list
        _testResults.Add(result);

        // Enable compare button if we have multiple results
        compareButton.interactable = _testResults.Count > 1;

        // Save to file for persistence (optional)
        SaveResultsToFile(result);
    }

    private void SaveResultsToFile(TestResult result)
    {
        try
        {
            string directory = Path.Combine(Application.persistentDataPath, "NetworkTests");
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string filename = $"test_{result.Library}_{result.Protocol}_{result.Timestamp:yyyyMMdd_HHmmss}.json";
            string path = Path.Combine(directory, filename);

            // Simple JSON serialization
            string json = JsonUtility.ToJson(result, true);
            File.WriteAllText(path, json);

            Debug.Log($"Test results saved to: {path}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to save test results: {ex.Message}");
        }
    }
    #endregion

    #region Comparison Visualization
    private void DisplayComparisonCharts()
    {
        if (_testResults.Count < 2)
            return;

        // Show charts panel
        if (chartsPanel != null)
            chartsPanel.SetActive(true);

        // Generate comparison text
        var sb = new StringBuilder();
        sb.AppendLine("PERFORMANCE COMPARISON");
        sb.AppendLine("=====================");

        // Sort results by their throughput (highest first)
        var sortedResults = _testResults.OrderByDescending(r => r.ThroughputMBps).ToList();

        // Display table header
        sb.AppendLine($"{"Library",15} | {"Protocol",10} | {"Conn/s",8} | {"Msgs/s",8} | {"MB/s",8} | {"Avg Lat",8} | {"RTT",8} | {"Loss %",6}");
        sb.AppendLine(new string('-', 85));

        // Display each result
        foreach (var result in sortedResults)
        {
            sb.AppendLine($"{result.Library,15} | {result.Protocol,10} | {result.ConnectionsPerSecond,8:F1} | " +
                          $"{result.MessagesPerSecond,8:F1} | {result.ThroughputMBps,8:F2} | " +
                          $"{result.AverageLatencyMs,8:F1} | {result.AverageRttMs,8:F1} | {result.PacketLossPercent,6:F2}");
        }

        // Calculate performance ratios (using best as baseline)
        sb.AppendLine("\nPERFORMANCE RATIOS (higher is better)");
        sb.AppendLine("====================================");

        // Throughput comparison
        var bestThroughput = sortedResults.First().ThroughputMBps;
        sb.AppendLine("\nThroughput (MB/s):");
        foreach (var result in sortedResults)
        {
            double ratio = result.ThroughputMBps / bestThroughput;
            sb.AppendLine($"{result.Library} ({result.Protocol}): {result.ThroughputMBps:F2} MB/s ({ratio:P0})");
        }

        // Latency comparison (lower is better, so invert the ratio)
        if (sortedResults.All(r => r.AverageLatencyMs > 0))
        {
            var bestLatency = sortedResults.Min(r => r.AverageLatencyMs);
            sb.AppendLine("\nLatency (ms):");
            foreach (var result in sortedResults.OrderBy(r => r.AverageLatencyMs))
            {
                double ratio = bestLatency / result.AverageLatencyMs;
                sb.AppendLine($"{result.Library} ({result.Protocol}): {result.AverageLatencyMs:F1} ms ({ratio:P0})");
            }
        }

        // Connection rate comparison
        var bestConnectionRate = sortedResults.Max(r => r.ConnectionsPerSecond);
        sb.AppendLine("\nConnection Rate (conn/s):");
        foreach (var result in sortedResults.OrderByDescending(r => r.ConnectionsPerSecond))
        {
            double ratio = result.ConnectionsPerSecond / bestConnectionRate;
            sb.AppendLine($"{result.Library} ({result.Protocol}): {result.ConnectionsPerSecond:F1} conn/s ({ratio:P0})");
        }

        // Update results text
        resultsText.text = sb.ToString();

        // Scroll to top
        if (resultsScrollRect != null)
        {
            resultsScrollRect.verticalNormalizedPosition = 1;
        }

        // TODO: Generate actual visual charts here
        // For a real implementation, you would use a charting library like Unity UI extensions
        // or implement custom chart rendering
        GenerateSimpleCharts(sortedResults);
    }

    private void GenerateSimpleCharts(List<TestResult> results)
    {
        // This is a placeholder for actual chart generation
        // In a real implementation, you would create visual bar charts

        // For throughput chart
        if (throughputChart != null)
        {
            // Clear existing children
            foreach (Transform child in throughputChart)
            {
                Destroy(child.gameObject);
            }

            // Generate simple bars for each result
            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];

                // Create bar GameObject
                var barGo = new GameObject($"Bar_{result.Library}_{result.Protocol}");
                barGo.transform.SetParent(throughputChart, false);

                // Add image component for the bar
                var image = barGo.AddComponent<Image>();
                image.color = GetColorForIndex(i);

                // Add text label
                var labelGo = new GameObject($"Label_{result.Library}_{result.Protocol}");
                labelGo.transform.SetParent(barGo.transform, false);
                var text = labelGo.AddComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                text.text = $"{result.Library} ({result.Protocol}): {result.ThroughputMBps:F2} MB/s";
                text.color = Color.black;
                text.alignment = TextAnchor.MiddleLeft;

                // Position the bar
                float height = throughputChart.rect.height / (results.Count + 1);
                float maxWidth = throughputChart.rect.width * 0.8f;
                float width = (float)(maxWidth * (result.ThroughputMBps / results.Max(r => r.ThroughputMBps)));

                var rectTransform = barGo.GetComponent<RectTransform>();
                rectTransform.anchorMin = new Vector2(0, 1);
                rectTransform.anchorMax = new Vector2(0, 1);
                rectTransform.pivot = new Vector2(0, 1);
                rectTransform.sizeDelta = new Vector2(width, height * 0.8f);
                rectTransform.anchoredPosition = new Vector2(10, -height * (i + 0.5f));

                // Position the label
                var labelRectTransform = labelGo.GetComponent<RectTransform>();
                labelRectTransform.anchorMin = new Vector2(0, 0);
                labelRectTransform.anchorMax = new Vector2(1, 1);
                labelRectTransform.offsetMin = new Vector2(5, 0);
                labelRectTransform.offsetMax = new Vector2(-5, 0);
            }
        }

        // Similar implementations would be added for other charts
    }

    private Color GetColorForIndex(int index)
    {
        // Predefined colors for chart elements
        Color[] colors = new Color[]
        {
            new Color(0.2f, 0.6f, 1.0f), // Blue
            new Color(1.0f, 0.6f, 0.2f), // Orange
            new Color(0.2f, 0.8f, 0.2f), // Green
            new Color(1.0f, 0.2f, 0.2f), // Red
            new Color(0.8f, 0.2f, 0.8f), // Purple
            new Color(0.5f, 0.5f, 0.5f)  // Gray
        };

        return colors[index % colors.Length];
    }
    #endregion

    #region Utility Methods
    private double GetMedian(long[] sortedArray)
    {
        if (sortedArray.Length == 0)
            return 0;

        Array.Sort(sortedArray);
        var mid = sortedArray.Length / 2;
        if (sortedArray.Length % 2 == 0)
            return (sortedArray[mid - 1] + sortedArray[mid]) / 2.0;
        else
            return sortedArray[mid];
    }

    private double GetPercentile(long[] sortedArray, double percentile)
    {
        if (sortedArray.Length == 0)
            return 0;

        Array.Sort(sortedArray);
        var index = (int)Math.Ceiling(percentile * sortedArray.Length) - 1;
        index = Math.Max(0, Math.Min(sortedArray.Length - 1, index));
        return sortedArray[index];
    }
    #endregion
}

/// <summary>
/// Custom network client implementation.
/// This is a placeholder - replace with your actual custom network implementation.
/// </summary>
public class CustomNetworkClient
{
    public event EventHandler<MessageEventArgs> OnMessageReceived;

    private TcpClient _tcpClient;
    private NetworkStream _stream;
    private bool _isConnected;

    public CustomNetworkClient()
    {
        _tcpClient = new TcpClient();
        _isConnected = false;
    }

    public async Task<bool> ConnectAsync(string host, int port, NetworkPerformanceTester.NetworkProtocol protocol)
    {
        try
        {
            // In a real implementation, you would use your own custom network layer
            // This is just a placeholder using TcpClient
            await _tcpClient.ConnectAsync(host, port);
            _stream = _tcpClient.GetStream();
            _isConnected = _tcpClient.Connected;

            // Start receiving messages
            if (_isConnected)
            {
                _ = ReceiveMessagesAsync();
            }

            return _isConnected;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Custom network connect error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SendMessageAsync(byte[] message)
    {
        if (!_isConnected || _stream == null)
            return false;

        try
        {
            await _stream.WriteAsync(message, 0, message.Length);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Custom network send error: {ex.Message}");
            return false;
        }
    }

    private async Task ReceiveMessagesAsync()
    {
        try
        {
            byte[] buffer = new byte[4096];

            while (_isConnected)
            {
                int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    // Connection closed
                    _isConnected = false;
                    break;
                }

                // Process received data
                if (bytesRead > 0)
                {
                    // Extract timestamp if present (first 8 bytes)
                    long rtt = 0;
                    if (bytesRead >= 8)
                    {
                        long timestamp = BitConverter.ToInt64(buffer, 0);
                        rtt = (long)(DateTime.UtcNow - new DateTime(timestamp)).TotalMilliseconds;
                    }

                    // Raise event
                    OnMessageReceived?.Invoke(this, new MessageEventArgs
                    {
                        Message = buffer,
                        MessageSize = bytesRead,
                        RoundTripTime = rtt
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Custom network receive error: {ex.Message}");
            _isConnected = false;
        }
    }

    public async Task DisconnectAsync()
    {
        _isConnected = false;

        try
        {
            if (_stream != null)
            {
                _stream.Close();
                _stream = null;
            }

            if (_tcpClient != null)
            {
                _tcpClient.Close();
                _tcpClient = null;
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Custom network disconnect error: {ex.Message}");
        }
    }

    public class MessageEventArgs : EventArgs
    {
        public byte[] Message { get; set; }
        public int MessageSize { get; set; }
        public long RoundTripTime { get; set; }
    }
}

/// <summary>
/// Test result data structure for storing and comparing test outcomes
/// </summary>
[System.Serializable]
public class TestResult
{
    public DateTime Timestamp;
    public NetworkPerformanceTester.NetworkLibrary Library;
    public NetworkPerformanceTester.NetworkProtocol Protocol;
    public TimeSpan Duration;
    public int ConnectionCount;
    public int TotalConnections;
    public int MessageCount;
    public int MessageSize;
    public long BytesTransferred;
    public int MessagesReceived;
    public long BytesReceived;
    public double ThroughputMBps;
    public double ConnectionsPerSecond;
    public double MessagesPerSecond;
    public double AverageLatencyMs;
    public double MedianLatencyMs;
    public double P95LatencyMs;
    public double AverageRttMs;
    public double MedianRttMs;
    public double P95RttMs;
    public int ErrorCount;
    public double PacketLossPercent;
}
