using System;
using UnityEngine;

namespace GameApp.Unity.Managers
{
    /// <summary>
    /// UI管理器 - 负责处理所有UI相关的操作
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 显示聊天消息
        /// </summary>
        /// <param name="playerName">玩家名称</param>
        /// <param name="message">消息内容</param>
        /// <param name="chatType">聊天类型</param>
        public void ShowChatMessage(string playerName, string message, string chatType)
        {
            Debug.Log($"[{chatType.ToUpper()}] {playerName}: {message}");

            // 这里可以添加具体的UI显示逻辑
            // 例如：更新聊天窗口、显示消息气泡等

            // 示例：如果存在聊天UI组件，可以调用其方法
            // var chatUI = FindObjectOfType<ChatUI>();
            // if (chatUI != null)
            // {
            //     chatUI.AddMessage(playerName, message, chatType);
            // }
        }

        /// <summary>
        /// 显示系统消息
        /// </summary>
        /// <param name="message">消息内容</param>
        /// <param name="messageType">消息类型</param>
        public void ShowSystemMessage(string message, string messageType = "System")
        {
            Debug.Log($"[{messageType.ToUpper()}] {message}");

            // 这里可以添加系统消息的UI显示逻辑
        }

        /// <summary>
        /// 显示通知
        /// </summary>
        /// <param name="title">标题</param>
        /// <param name="content">内容</param>
        /// <param name="duration">显示时长</param>
        public void ShowNotification(string title, string content, float duration = 3f)
        {
            Debug.Log($"Notification: {title} - {content}");

            // 这里可以添加通知UI的显示逻辑
        }

        /// <summary>
        /// 显示加载界面
        /// </summary>
        /// <param name="show">是否显示</param>
        /// <param name="message">加载消息</param>
        public void ShowLoadingScreen(bool show, string message = "加载中...")
        {
            Debug.Log($"Loading screen: {(show ? "Show" : "Hide")} - {message}");

            // 这里可以添加加载界面的显示/隐藏逻辑
        }

        /// <summary>
        /// 更新玩家状态UI
        /// </summary>
        /// <param name="playerName">玩家名称</param>
        /// <param name="level">等级</param>
        /// <param name="health">生命值</param>
        /// <param name="maxHealth">最大生命值</param>
        public void UpdatePlayerStatusUI(string playerName, int level, float health, float maxHealth)
        {
            Debug.Log($"Update player status UI: {playerName} Lv.{level} HP:{health}/{maxHealth}");

            // 这里可以添加玩家状态UI的更新逻辑
        }

        /// <summary>
        /// 显示世界事件通知
        /// </summary>
        /// <param name="eventName">事件名称</param>
        /// <param name="eventType">事件类型</param>
        public void ShowWorldEventNotification(string eventName, string eventType)
        {
            Debug.Log($"World Event Notification: {eventName} ({eventType})");

            // 这里可以添加世界事件通知的UI显示逻辑
            // 例如：显示弹窗、播放音效、更新事件列表等
        }
    }
}
