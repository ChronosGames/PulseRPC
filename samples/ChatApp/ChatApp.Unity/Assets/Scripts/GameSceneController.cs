using System;
using System.Threading.Tasks;
using ChatApp.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace ChatApp
{
    /// <summary>
    /// 游戏场景控制器
    /// </summary>
    public class GameSceneController : MonoBehaviour
    {
        [Header("游戏客户端")]
        [SerializeField] private UnityGameClient _gameClient;

        [Header("玩家管理器")]
        [SerializeField] private PlayerManager _playerManager;

        [Header("UI组件")]
        [SerializeField] private Text _statusText;
        [SerializeField] private Text _playerInfoText;
        [SerializeField] private Button _moveForwardButton;
        [SerializeField] private Button _moveBackwardButton;
        [SerializeField] private Button _moveLeftButton;
        [SerializeField] private Button _moveRightButton;

        [Header("移动设置")]
        [SerializeField] private float _moveDistance = 1.0f;

        // 记录玩家位置
        private float _playerX = 0f;
        private float _playerY = 0f;
        private float _playerZ = 0f;

        private void Start()
        {
            // 初始化UI组件
            InitializeUI();

            // 确保UnityGameClient已初始化
            if (_gameClient == null)
            {
                _gameClient = FindObjectOfType<UnityGameClient>();
                if (_gameClient == null)
                {
                    Debug.LogError("找不到UnityGameClient组件，请确保场景中有UnityGameClient组件");
                    return;
                }
            }

            // 确保PlayerManager已初始化
            if (_playerManager == null)
            {
                _playerManager = FindObjectOfType<PlayerManager>();
                if (_playerManager == null)
                {
                    Debug.LogWarning("找不到PlayerManager组件，玩家可视化将不会显示");
                }
            }

            // 设置状态文本
            UpdateStatus("正在连接到服务器...");
        }

        private void InitializeUI()
        {
            // 设置按钮事件
            if (_moveForwardButton != null)
                _moveForwardButton.onClick.AddListener(MoveForward);

            if (_moveBackwardButton != null)
                _moveBackwardButton.onClick.AddListener(MoveBackward);

            if (_moveLeftButton != null)
                _moveLeftButton.onClick.AddListener(MoveLeft);

            if (_moveRightButton != null)
                _moveRightButton.onClick.AddListener(MoveRight);

            // 初始化状态文本
            if (_statusText != null)
                _statusText.text = "初始化中...";

            if (_playerInfoText != null)
                _playerInfoText.text = "";
        }

        private async void MoveForward()
        {
            _playerZ += _moveDistance;
            await Move();
        }

        private async void MoveBackward()
        {
            _playerZ -= _moveDistance;
            await Move();
        }

        private async void MoveLeft()
        {
            _playerX -= _moveDistance;
            await Move();
        }

        private async void MoveRight()
        {
            _playerX += _moveDistance;
            await Move();
        }

        private async Task Move()
        {
            try
            {
                // 更新位置显示
                UpdatePlayerPosition();

                // 更新本地玩家对象位置
                if (_playerManager != null)
                {
                    _playerManager.UpdateLocalPlayerPosition(new Vector3(_playerX, _playerY, _playerZ));
                }

                // 发送移动请求到服务器
                await _gameClient.MoveAsync(_playerX, _playerY, _playerZ);
            }
            catch (Exception ex)
            {
                Debug.LogError($"移动失败: {ex.Message}");
                UpdateStatus($"移动失败: {ex.Message}");
            }
        }

        public void UpdateStatus(string status)
        {
            if (_statusText != null)
                _statusText.text = status;
        }

        public void UpdatePlayerInfo(string username, Guid playerId)
        {
            if (_playerInfoText != null)
                _playerInfoText.text = $"玩家: {username}\nID: {playerId}";

            UpdatePlayerPosition();
        }

        private void UpdatePlayerPosition()
        {
            if (_playerInfoText != null)
            {
                var baseText = _playerInfoText.text.Split(new[] { '\n' }, 2)[0];
                _playerInfoText.text = $"{baseText}\n位置: ({_playerX:F1}, {_playerY:F1}, {_playerZ:F1})";
            }
        }

        // 为WASD键盘输入添加控制
        private void Update()
        {
            bool moved = false;

            if (Input.GetKeyDown(KeyCode.W))
            {
                _playerZ += _moveDistance;
                moved = true;
            }
            else if (Input.GetKeyDown(KeyCode.S))
            {
                _playerZ -= _moveDistance;
                moved = true;
            }
            else if (Input.GetKeyDown(KeyCode.A))
            {
                _playerX -= _moveDistance;
                moved = true;
            }
            else if (Input.GetKeyDown(KeyCode.D))
            {
                _playerX += _moveDistance;
                moved = true;
            }

            if (moved)
            {
                Move().GetAwaiter().GetResult();
            }
        }
    }
}
