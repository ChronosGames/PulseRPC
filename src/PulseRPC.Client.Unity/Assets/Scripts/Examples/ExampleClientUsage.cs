using System;
using UnityEngine;
using UnityEngine.UI;

namespace PulseRPC.Examples
{
    /// <summary>
    /// 示例客户端用法
    /// </summary>
    public class ExampleClientUsage : MonoBehaviour
    {
        [SerializeField] private UnityClient client;
        [SerializeField] private InputField nameInput;
        [SerializeField] private InputField number1Input;
        [SerializeField] private InputField number2Input;
        [SerializeField] private Button greetButton;
        [SerializeField] private Button calculateButton;
        [SerializeField] private Text resultText;
        [SerializeField] private Text notificationText;

        private void Start()
        {
            // 确保有UnityClient实例
            if (client == null)
            {
                client = FindObjectOfType<UnityClient>();
                if (client == null)
                {
                    Debug.LogError("找不到UnityClient实例，请确保场景中有UnityClient组件");
                    return;
                }
            }

            // 设置按钮事件
            greetButton.onClick.AddListener(OnGreetButtonClicked);
            calculateButton.onClick.AddListener(OnCalculateButtonClicked);

            // 注册服务器通知处理器
            client.RegisterHandler(new ServerNotificationHandler(OnNotificationReceived));

            // 监听连接状态变化
            client.OnConnectionStateChanged += OnConnectionStateChanged;

            // 更新UI状态
            UpdateUIState(client.IsConnected);
        }

        private void OnDestroy()
        {
            // 移除事件监听
            if (client != null)
            {
                client.OnConnectionStateChanged -= OnConnectionStateChanged;
            }
        }

        /// <summary>
        /// 处理问候按钮点击
        /// </summary>
        private async void OnGreetButtonClicked()
        {
            if (!client.IsConnected)
            {
                resultText.text = "未连接到服务器";
                return;
            }

            try
            {
                // 显示加载状态
                resultText.text = "正在发送请求...";

                // 获取输入
                var name = nameInput.text;
                if (string.IsNullOrEmpty(name))
                {
                    name = "游客";
                }

                // 创建请求消息
                var request = new GreetingRequest { Name = name };

                // 发送请求
                var response = await client.SendRequest<GreetingRequest, GreetingResponse>(request);

                // 显示结果
                resultText.text = $"服务器回应: {response.Message}\n时间: {response.Timestamp}";
            }
            catch (Exception ex)
            {
                resultText.text = $"错误: {ex.Message}";
                Debug.LogException(ex);
            }
        }

        /// <summary>
        /// 处理计算按钮点击
        /// </summary>
        private async void OnCalculateButtonClicked()
        {
            if (!client.IsConnected)
            {
                resultText.text = "未连接到服务器";
                return;
            }

            try
            {
                // 显示加载状态
                resultText.text = "正在计算...";

                // 获取输入
                if (!int.TryParse(number1Input.text, out int a))
                {
                    resultText.text = "请输入有效的第一个数字";
                    return;
                }

                if (!int.TryParse(number2Input.text, out int b))
                {
                    resultText.text = "请输入有效的第二个数字";
                    return;
                }

                // 创建请求消息
                var request = new CalculationRequest { A = a, B = b };

                // 发送请求
                var response = await client.SendRequest<CalculationRequest, CalculationResponse>(request);

                // 显示结果
                resultText.text = $"计算结果:\n" +
                                  $"和: {response.Sum}\n" +
                                  $"积: {response.Product}\n" +
                                  $"商: {response.Division:F2}";
            }
            catch (Exception ex)
            {
                resultText.text = $"错误: {ex.Message}";
                Debug.LogException(ex);
            }
        }

        /// <summary>
        /// 处理服务器通知
        /// </summary>
        private void OnNotificationReceived(ServerNotification notification)
        {
            // 在UI上显示通知
            string typeText = notification.Type switch
            {
                NotificationType.Info => "信息",
                NotificationType.Warning => "警告",
                NotificationType.Error => "错误",
                _ => "未知"
            };

            notificationText.text = $"[{typeText}] {notification.Content}\n时间: {notification.Timestamp}";

            // 根据通知类型设置颜色
            notificationText.color = notification.Type switch
            {
                NotificationType.Info => Color.white,
                NotificationType.Warning => Color.yellow,
                NotificationType.Error => Color.red,
                _ => Color.white
            };
        }

        /// <summary>
        /// 处理连接状态变化
        /// </summary>
        private void OnConnectionStateChanged(bool isConnected)
        {
            UpdateUIState(isConnected);
        }

        /// <summary>
        /// 更新UI状态
        /// </summary>
        private void UpdateUIState(bool isConnected)
        {
            greetButton.interactable = isConnected;
            calculateButton.interactable = isConnected;

            if (!isConnected)
            {
                resultText.text = "未连接到服务器";
            }
        }
    }
}
