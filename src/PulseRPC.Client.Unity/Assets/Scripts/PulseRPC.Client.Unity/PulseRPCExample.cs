using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PulseRPC.Client.Unity.Generated;
using UnityEngine;
using UnityEngine.UI;

namespace PulseRPC.Client.Unity
{
    /// <summary>
    /// PulseRPC客户端使用示例
    /// </summary>
    public class PulseRPCExample : MonoBehaviour, IExampleHubReceiver
    {
        [Header("连接设置")]
        [SerializeField] private string serverUrl = "ws://localhost:5000/pulse";
        [SerializeField] private bool connectOnStart = true;

        [Header("UI引用")]
        [SerializeField] private Text statusText;
        [SerializeField] private Text logText;
        [SerializeField] private InputField messageInput;
        [SerializeField] private Button sendButton;
        [SerializeField] private Button connectButton;
        [SerializeField] private Button disconnectButton;

        // 连接对象
        private PulseWebSocketConnection _connection;

        // 服务客户端
        private IExampleService _exampleService;

        // Hub客户端
        private IExampleHub _exampleHub;

        // 连接状态
        private bool _isConnected = false;

        private void Start()
        {
            if (connectOnStart)
            {
                Connect();
            }

            // 设置UI事件
            if (sendButton != null)
                sendButton.onClick.AddListener(OnSendButtonClicked);

            if (connectButton != null)
                connectButton.onClick.AddListener(Connect);

            if (disconnectButton != null)
                disconnectButton.onClick.AddListener(Disconnect);

            UpdateUI();
        }

        private void OnDestroy()
        {
            // 断开连接
            if (_connection != null && _connection.IsConnected)
            {
                _ = _connection.DisconnectAsync();
            }
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        public async void Connect()
        {
            if (_isConnected)
                return;

            UpdateStatus("正在连接...");

            try
            {
                // 创建连接
                _connection = new PulseWebSocketConnection(serverUrl);
                await _connection.ConnectAsync();

                // 创建服务客户端
                _exampleService = PulseClientFactory.Create<IExampleService>(_connection);

                // 创建Hub客户端
                _exampleHub = PulseClientFactory.ConnectToHub<IExampleHub, IExampleHubReceiver>(_connection, this);

                _isConnected = true;
                UpdateStatus("已连接");
                Log("成功连接到服务器");

                // 测试服务调用
                await TestServiceCall();

                // 加入聊天室
                await _exampleHub.JoinRoomAsync("general");
            }
            catch (Exception ex)
            {
                UpdateStatus("连接失败");
                Log($"连接错误: {ex.Message}");
            }

            UpdateUI();
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public async void Disconnect()
        {
            if (!_isConnected || _connection == null)
                return;

            UpdateStatus("正在断开连接...");

            try
            {
                await _connection.DisconnectAsync();
                _isConnected = false;
                UpdateStatus("已断开连接");
                Log("已断开与服务器的连接");
            }
            catch (Exception ex)
            {
                Log($"断开连接错误: {ex.Message}");
            }

            UpdateUI();
        }

        /// <summary>
        /// 当发送按钮点击时
        /// </summary>
        private async void OnSendButtonClicked()
        {
            if (!_isConnected || _exampleHub == null || string.IsNullOrEmpty(messageInput.text))
                return;

            string message = messageInput.text;
            messageInput.text = "";

            try
            {
                await _exampleHub.SendMessageAsync(message);
                Log($"已发送: {message}");
            }
            catch (Exception ex)
            {
                Log($"发送消息错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 测试服务调用
        /// </summary>
        private async Task TestServiceCall()
        {
            try
            {
                // 调用Add方法
                int result = await _exampleService.AddAsync(40, 2);
                Log($"调用Add(40, 2)结果: {result}");

                // 调用Greet方法
                string greeting = await _exampleService.GreetAsync("Unity客户端");
                Log($"调用Greet结果: {greeting}");
            }
            catch (Exception ex)
            {
                Log($"测试调用错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新UI状态
        /// </summary>
        private void UpdateUI()
        {
            if (connectButton != null)
                connectButton.interactable = !_isConnected;

            if (disconnectButton != null)
                disconnectButton.interactable = _isConnected;

            if (sendButton != null)
                sendButton.interactable = _isConnected;

            if (messageInput != null)
                messageInput.interactable = _isConnected;
        }

        /// <summary>
        /// 更新状态文本
        /// </summary>
        private void UpdateStatus(string status)
        {
            if (statusText != null)
                statusText.text = status;
        }

        /// <summary>
        /// 添加日志消息
        /// </summary>
        private void Log(string message)
        {
            Debug.Log($"[PulseRPC] {message}");

            if (logText != null)
            {
                logText.text += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
            }
        }

        #region IExampleHubReceiver Implementation

        public Task OnMessageAsync(string message)
        {
            Log($"收到消息: {message}");
            return Task.CompletedTask;
        }

        public Task OnUserJoinedAsync(string user)
        {
            Log($"用户加入: {user}");
            return Task.CompletedTask;
        }

        public Task OnUserLeftAsync(string userId)
        {
            Log($"用户离开: {userId}");
            return Task.CompletedTask;
        }

        #endregion
    }
}
