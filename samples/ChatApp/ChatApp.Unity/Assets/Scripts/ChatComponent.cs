using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using ChatApp.Unity;
using ChatApp.Shared;

namespace Assets.Scripts
{
    /// <summary>
    /// 聊天组件 - 简化版本，专注于UI显示
    /// </summary>
    public class ChatComponent : MonoBehaviour
    {
        [Header("UI组件")]
        public Text ChatText;
        public Button JoinOrLeaveButton;
        public Text JoinOrLeaveButtonText;
        public Button SendMessageButton;
        public InputField Input;
        public InputField ReportInput;
        public Button SendReportButton;
        public Button DisconnectButton;
        public Button ExceptionButton;
        public Button UnaryExceptionButton;
        public Text LabelRtt;

        [Header("游戏客户端")]
        public UnityGameClient gameClient;

        private CancellationTokenSource shutdownCancellation = new CancellationTokenSource();
        private bool isJoin;

        async void Start()
        {
            // 如果没有指定游戏客户端，尝试查找
            if (gameClient == null)
            {
                gameClient = FindObjectOfType<UnityGameClient>();
            }

            this.InitializeUi();

            // 订阅游戏客户端事件
            if (gameClient != null)
            {
                gameClient.OnStatusUpdate += OnStatusUpdate;
                gameClient.OnLoginSuccess += OnLoginSuccess;
                gameClient.OnLoginFailed += OnLoginFailed;
                gameClient.OnPlayerJoined += OnPlayerJoined;
                gameClient.OnPlayerLeft += OnPlayerLeft;
                gameClient.OnPlayerMoved += OnPlayerMoved;
            }
        }

        async void OnDestroy()
        {
            // 清理资源
            shutdownCancellation.Cancel();

            // 取消订阅事件
            if (gameClient != null)
            {
                gameClient.OnStatusUpdate -= OnStatusUpdate;
                gameClient.OnLoginSuccess -= OnLoginSuccess;
                gameClient.OnLoginFailed -= OnLoginFailed;
                gameClient.OnPlayerJoined -= OnPlayerJoined;
                gameClient.OnPlayerLeft -= OnPlayerLeft;
                gameClient.OnPlayerMoved -= OnPlayerMoved;
            }
        }

        private void InitializeUi()
        {
            this.isJoin = false;

            if (SendMessageButton != null)
                this.SendMessageButton.interactable = false;

            if (ChatText != null)
                this.ChatText.text = string.Empty;

            if (Input != null)
            {
                this.Input.text = string.Empty;
                if (Input.placeholder != null)
                    this.Input.placeholder.GetComponent<Text>().text = "等待连接...";
            }

            if (JoinOrLeaveButtonText != null)
                this.JoinOrLeaveButtonText.text = "等待连接";

            if (ExceptionButton != null)
                this.ExceptionButton.interactable = false;
        }

        #region 游戏客户端事件处理

        private void OnStatusUpdate(string status)
        {
            if (ChatText != null)
            {
                ChatText.text += $"[系统] {status}\n";
            }
        }

        private void OnLoginSuccess(PlayerInfo playerInfo)
        {
            if (ChatText != null)
            {
                ChatText.text += $"[系统] 登录成功: {playerInfo.Username}\n";
            }

            if (JoinOrLeaveButtonText != null)
            {
                JoinOrLeaveButtonText.text = "已连接";
            }

            if (SendMessageButton != null)
            {
                SendMessageButton.interactable = true;
            }

            if (Input != null && Input.placeholder != null)
            {
                Input.placeholder.GetComponent<Text>().text = "输入移动坐标 (x,z)...";
            }
        }

        private void OnLoginFailed(string error)
        {
            if (ChatText != null)
            {
                ChatText.text += $"[系统] 登录失败: {error}\n";
            }
        }

        private void OnPlayerJoined(Guid playerId, string playerName, System.Numerics.Vector3 position)
        {
            if (ChatText != null)
            {
                ChatText.text += $"[玩家] {playerName} 加入了游戏\n";
            }
        }

        private void OnPlayerLeft(Guid playerId, string reason)
        {
            if (ChatText != null)
            {
                ChatText.text += $"[玩家] 玩家离开: {reason}\n";
            }
        }

        private void OnPlayerMoved(Guid playerId, System.Numerics.Vector3 position)
        {
            if (ChatText != null)
            {
                ChatText.text += $"[移动] 玩家移动到: ({position.X:F1}, {position.Y:F1}, {position.Z:F1})\n";
            }
        }

        #endregion

        #region UI按钮处理

        public async void JoinOrLeave()
        {
            // 简单的占位符功能
            if (ChatText != null)
            {
                ChatText.text += "[系统] 按钮功能待实现\n";
            }
        }

        public async void SendMessage()
        {
            if (!string.IsNullOrEmpty(Input?.text) && gameClient != null)
            {
                // 尝试解析输入为移动坐标
                var parts = Input.text.Split(',');
                if (parts.Length >= 2 &&
                    float.TryParse(parts[0].Trim(), out float x) &&
                    float.TryParse(parts[1].Trim(), out float z))
                {
                    await gameClient.MoveAsync(x, 0, z);

                    if (ChatText != null)
                    {
                        ChatText.text += $"[移动] 移动到: ({x:F1}, 0, {z:F1})\n";
                    }
                }
                else
                {
                    if (ChatText != null)
                    {
                        ChatText.text += $"[消息] {Input.text}\n";
                    }
                }

                Input.text = string.Empty;
            }
        }

        public void DisconnectServer()
        {
            if (ChatText != null)
            {
                ChatText.text += "[系统] 断开连接功能待实现\n";
            }
        }

        public async void GenerateException()
        {
            if (ChatText != null)
            {
                ChatText.text += "[系统] 异常测试功能待实现\n";
            }
        }

        public async void SendReport()
        {
            if (ChatText != null)
            {
                ChatText.text += "[系统] 报告功能待实现\n";
            }
        }

        public async void UnaryGenerateException()
        {
            if (ChatText != null)
            {
                ChatText.text += "[系统] 异常测试功能待实现\n";
            }
        }

        #endregion
    }
}
