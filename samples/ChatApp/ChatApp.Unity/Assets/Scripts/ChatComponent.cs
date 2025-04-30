using System;
using ChatApp.Shared.Hubs;
using ChatApp.Shared.Models;
using ChatApp.Shared.Services;
using ChatApp.Unity.Network;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityTCP;

namespace Assets.Scripts
{
    public class ChatComponent : MonoBehaviour, IChatHubReceiver
    {
        private CancellationTokenSource shutdownCancellation = new CancellationTokenSource();
        private TCPNetworkManager networkManager;
        private ChatHubClient streamingClient;
        private ChatServiceClient client;

        private bool isJoin;
        private bool isSelfDisConnected;

        // 服务器地址配置
        public string ServerIP = "localhost";
        public int ServerPort = 12345;

        public Text ChatText;
        public Button JoinOrLeaveButton;
        public Text JoinOrLeaveButtonText;
        public Button SendMessageButton;
        public InputField Input;
        public InputField ReportInput;
        public Button SendReportButton;
        public Button DisconnectButon;
        public Button ExceptionButton;
        public Button UnaryExceptionButton;
        public Text LabelRtt;

        async void Start()
        {
            // 初始化网络管理器
            networkManager = gameObject.AddComponent<TCPNetworkManager>();

            await this.InitializeClientAsync();
            this.InitializeUi();
        }

        async void OnDestroy()
        {
            // 清理资源
            shutdownCancellation.Cancel();

            if (this.streamingClient != null)
            {
                this.streamingClient.Dispose();
            }

            if (networkManager != null)
            {
                networkManager.DisconnectClient();
                Destroy(networkManager);
            }
        }

        private async Task InitializeClientAsync()
        {
            // 初始化Hub客户端
            while (!shutdownCancellation.IsCancellationRequested)
            {
                try
                {
                    Debug.Log($"Connecting to the server...");

                    // 连接到服务器
                    await networkManager.ConnectToServer(ServerIP, ServerPort);

                    // 创建ChatHub客户端
                    this.streamingClient = new ChatHubClient(networkManager, this);

                    // 注册断开连接事件
                    this.streamingClient.Disconnected += OnStreamingClientDisconnected;
                    this.streamingClient.HeartbeatReceived += rtt => LabelRtt.text = $"RTT: {rtt.TotalMilliseconds:#,0}ms";

                    Debug.Log($"Connection is established.");
                    break;
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }

                Debug.Log($"Failed to connect to the server. Retry after 5 seconds...");
                await Task.Delay(5 * 1000);
            }

            // 创建服务客户端
            this.client = new ChatServiceClient(networkManager);
        }

        private void InitializeUi()
        {
            this.isJoin = false;

            this.SendMessageButton.interactable = false;
            this.ChatText.text = string.Empty;
            this.Input.text = string.Empty;
            this.Input.placeholder.GetComponent<Text>().text = "Please enter your name.";
            this.JoinOrLeaveButtonText.text = "Enter the room";
            this.ExceptionButton.interactable = false;
        }

        private void OnStreamingClientDisconnected()
        {
            try
            {
                Debug.Log($"Disconnected from the server.");

                if (this.isSelfDisConnected)
                {
                    // 设置重连延迟
                    _ = Task.Delay(2000).ContinueWith(_ => ReconnectServerAsync());
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }

        public void DisconnectServer()
        {
            this.isSelfDisConnected = true;

            this.JoinOrLeaveButton.interactable = false;
            this.SendMessageButton.interactable = false;
            this.SendReportButton.interactable = false;
            this.DisconnectButon.interactable = false;
            this.ExceptionButton.interactable = false;
            this.UnaryExceptionButton.interactable = false;

            if (this.isJoin)
                this.JoinOrLeave();

            this.streamingClient.Dispose();
            networkManager.DisconnectClient();
        }

        public async void ReconnectInitializedServer()
        {
            if (networkManager != null)
            {
                networkManager.DisconnectClient();
            }

            if (streamingClient != null)
            {
                streamingClient.Dispose();
                streamingClient = null;
            }

            await this.InitializeClientAsync();
            this.InitializeUi();
        }

        private async Task ReconnectServerAsync()
        {
            Debug.Log($"Reconnecting to the server...");

            try
            {
                // 重新连接
                await networkManager.ConnectToServer(ServerIP, ServerPort);

                // 创建新的客户端
                this.streamingClient = new ChatHubClient(networkManager, this);
                this.streamingClient.Disconnected += OnStreamingClientDisconnected;

                // 创建新的服务客户端
                this.client = new ChatServiceClient(networkManager);

                Debug.Log("Reconnected.");

                this.JoinOrLeaveButton.interactable = true;
                this.SendMessageButton.interactable = false;
                this.SendReportButton.interactable = true;
                this.DisconnectButon.interactable = true;
                this.ExceptionButton.interactable = true;
                this.UnaryExceptionButton.interactable = true;

                this.isSelfDisConnected = false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Reconnection failed: {ex.Message}");
                // 尝试再次重连
                await Task.Delay(5000);
                _ = ReconnectServerAsync();
            }
        }

        #region Client -> Server (Streaming)
        public async void JoinOrLeave()
        {
            if (this.isJoin)
            {
                await this.streamingClient.LeaveAsync();
                this.InitializeUi();
            }
            else
            {
                var request = new JoinRequest { RoomName = "SampleRoom", UserName = this.Input.text };
                await this.streamingClient.JoinAsync(request);

                this.isJoin = true;
                this.SendMessageButton.interactable = true;
                this.JoinOrLeaveButtonText.text = "Leave the room";
                this.Input.text = string.Empty;
                this.Input.placeholder.GetComponent<Text>().text = "Please enter a comment.";
                this.ExceptionButton.interactable = true;
            }
        }

        public async void SendMessage()
        {
            if (!this.isJoin)
                return;

            await this.streamingClient.SendMessageAsync(this.Input.text);
            this.Input.text = string.Empty;
        }

        public async void GenerateException()
        {
            if (!this.isJoin) return;
            await this.streamingClient.GenerateException("client exception(streaminghub)!");
        }
        #endregion

        #region Server -> Client (Streaming)
        public void OnJoin(string name)
        {
            this.ChatText.text += $"{name} entered the room.\n";
        }

        public void OnLeave(string name)
        {
            this.ChatText.text += $"{name} left the room.\n";
        }

        public void OnSendMessage(MessageResponse message)
        {
            this.ChatText.text += $"{message.UserName}: {message.Message}\n";
        }

        public Task<string> HelloAsync(string name, int age)
        {
            Debug.Log($"HelloAsync is called with {name}, {age}");
            return Task.FromResult($"Hello {name}, you are {age} years old!");
        }
        #endregion

        public async void SendReport()
        {
            await this.client.SendReportAsync(this.ReportInput.text);
            ReportInput.text = string.Empty;
        }

        public async void UnaryGenerateException()
        {
            try
            {
                await this.client.GenerateException("client exception(unary)!");
            }
            catch (Exception e)
            {
                Debug.LogError($"UnaryGenerateException: {e.Message}");
            }
        }
    }
}
