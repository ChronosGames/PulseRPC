using UnityEngine;
using ChatApp.Unity;

namespace ChatApp
{
    /// <summary>
    /// 简单的玩家移动控制器 - 用于测试UnityGameClient的移动功能
    /// </summary>
    public class SimplePlayerController : MonoBehaviour
    {
        [Header("移动设置")]
        [SerializeField] private float _moveSpeed = 5.0f;
        [SerializeField] private float _moveSendInterval = 0.1f; // 发送移动更新的间隔

        [Header("游戏客户端")]
        [SerializeField] private UnityGameClient _gameClient;

        private float _lastMoveTime;
        private Vector3 _lastPosition;

        private void Awake()
        {
            // 如果没有指定游戏客户端，尝试查找
            if (_gameClient == null)
            {
                _gameClient = FindObjectOfType<UnityGameClient>();
            }
        }

        private void Update()
        {
            HandleMovementInput();
        }

        private void HandleMovementInput()
        {
            // 获取输入
            float horizontal = Input.GetAxis("Horizontal");
            float vertical = Input.GetAxis("Vertical");

            // 计算移动向量
            Vector3 movement = new Vector3(horizontal, 0, vertical) * _moveSpeed * Time.deltaTime;

            // 如果有移动输入
            if (movement.magnitude > 0.01f)
            {
                // 更新本地位置
                transform.position += movement;

                // 检查是否需要发送移动更新到服务器
                if (Time.time - _lastMoveTime >= _moveSendInterval)
                {
                    SendMoveToServer();
                    _lastMoveTime = Time.time;
                }
            }

            // 处理键盘快捷键
            HandleKeyboardShortcuts();
        }

        private void HandleKeyboardShortcuts()
        {
            // T键：测试随机移动
            if (Input.GetKeyDown(KeyCode.T))
            {
                TestRandomMove();
            }

            // R键：重置位置
            if (Input.GetKeyDown(KeyCode.R))
            {
                ResetPosition();
            }
        }

        private void SendMoveToServer()
        {
            if (_gameClient != null)
            {
                var position = transform.position;
                _ = _gameClient.MoveAsync(position.x, position.y, position.z);
                _lastPosition = position;
            }
        }

        /// <summary>
        /// 测试随机移动
        /// </summary>
        [ContextMenu("测试随机移动")]
        public void TestRandomMove()
        {
            var randomX = Random.Range(-10f, 10f);
            var randomZ = Random.Range(-10f, 10f);
            var newPosition = new Vector3(randomX, transform.position.y, randomZ);

            transform.position = newPosition;
            SendMoveToServer();

            Debug.Log($"[SimplePlayerController] 随机移动到: ({randomX:F1}, {transform.position.y:F1}, {randomZ:F1})");
        }

        /// <summary>
        /// 重置位置到原点
        /// </summary>
        [ContextMenu("重置位置")]
        public void ResetPosition()
        {
            transform.position = Vector3.zero;
            SendMoveToServer();

            Debug.Log("[SimplePlayerController] 位置已重置到原点");
        }

        /// <summary>
        /// 手动发送当前位置到服务器
        /// </summary>
        [ContextMenu("发送当前位置")]
        public void SendCurrentPosition()
        {
            SendMoveToServer();
            Debug.Log($"[SimplePlayerController] 发送当前位置: {transform.position}");
        }

        private void OnGUI()
        {
            // 在屏幕上显示控制说明
            GUILayout.BeginArea(new Rect(10, 10, 300, 200));
            GUILayout.Label("移动控制:");
            GUILayout.Label("WASD 或 方向键 - 移动");
            GUILayout.Label("T - 测试随机移动");
            GUILayout.Label("R - 重置位置");
            GUILayout.Label($"当前位置: {transform.position:F1}");

            if (_gameClient != null)
            {
                GUILayout.Label("游戏客户端: 已连接");
            }
            else
            {
                GUILayout.Label("游戏客户端: 未找到");
            }

            GUILayout.EndArea();
        }
    }
}
