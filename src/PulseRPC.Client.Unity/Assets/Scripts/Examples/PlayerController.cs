// GameExample.Unity/PlayerController.cs
public class PlayerController : MonoBehaviour, IPlayerEvents
{
    [SerializeField] private UnityNetworkClient _networkClient;

    private IPlayerService _playerService;
    private ISubscriptionToken _eventSubscription;

    private void Start()
    {
        // 获取服务代理
        _playerService = _networkClient.GetService<IPlayerService>();

        // 订阅事件
        var eventHandler = _networkClient.GetEventHandler<IPlayerEvents>();
        _eventSubscription = eventHandler.Subscribe(this);

        // 连接服务器
        ConnectToServer();
    }

    private void OnDestroy()
    {
        // 取消事件订阅
        _eventSubscription?.Dispose();
    }

    private async void ConnectToServer()
    {
        try
        {
            await _networkClient.ConnectAsync();

            // 登录
            var response = await _playerService.LoginAsync(new LoginRequest
            {
                Username = "Player" + UnityEngine.Random.Range(1000, 9999),
                Password = "password"
            });

            if (response.Success)
            {
                Debug.Log($"登录成功: {response.Player.Username}");
            }
            else
            {
                Debug.LogError($"登录失败: {response.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    // 发送移动命令
    public async void MovePlayer(Vector3 position, float rotation)
    {
        try
        {
            await _playerService.MoveAsync(new MoveRequest
            {
                X = position.x,
                Y = position.y,
                Z = position.z,
                RotationY = rotation
            });
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    // IPlayerEvents 实现

    public void OnPlayerJoined(PlayerJoinedEvent eventData)
    {
        Debug.Log($"玩家加入: {eventData.PlayerName} (ID: {eventData.PlayerId})");
        // 创建玩家游戏对象...
    }

    public void OnPlayerLeft(PlayerLeftEvent eventData)
    {
        Debug.Log($"玩家离开: {eventData.PlayerId}, 原因: {eventData.Reason}");
        // 移除玩家游戏对象...
    }

    public void OnPlayerMoved(PlayerMovedEvent eventData)
    {
        // 更新其他玩家位置
        // 使用协程或Unity主线程分发器确保在主线程更新
        MainThreadDispatcher.Enqueue(() =>
        {
            var playerObject = GetPlayerObject(eventData.PlayerId);
            if (playerObject != null)
            {
                var position = new Vector3(eventData.X, eventData.Y, eventData.Z);
                playerObject.transform.position = position;
                playerObject.transform.rotation = Quaternion.Euler(0, eventData.RotationY, 0);
            }
        });
    }

    // 其他游戏逻辑...
}
