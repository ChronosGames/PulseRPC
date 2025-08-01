# GameApp 用户手册

## 概述

GameApp 是一个现代化的多人在线游戏系统，提供完整的用户认证、游戏世界交互和实时战斗功能。本手册将指导您如何使用 GameApp 的各种功能。

## 快速开始

### 系统要求

#### Unity 客户端
- **Unity版本**: 2022.3 LTS 或更高
- **平台**: Windows, macOS, Linux, iOS, Android
- **网络**: 稳定的互联网连接
- **.NET**: .NET Standard 2.1 兼容

#### 网络要求
- **HTTP API**: 端口 5000 (AuthServer)
- **TCP连接**: 端口 7000 (GameServer)
- **UDP连接**: 端口 7001 (GameServer KCP)
- **TCP连接**: 端口 8000 (BattleServer)
- **UDP连接**: 端口 8001 (BattleServer KCP)

### 第一次使用

#### 1. 账户注册

首先需要创建一个游戏账户：

```csharp
// Unity 客户端示例
public async Task<bool> RegisterAccount(string username, string email, string password)
{
    var request = new RegisterRequest
    {
        username = username,
        email = email,
        password = password,
        confirmPassword = password
    };

    string json = JsonUtility.ToJson(request);
    byte[] data = System.Text.Encoding.UTF8.GetBytes(json);

    using var www = UnityWebRequest.PostWwwForm("http://localhost:5000/api/auth/register", "");
    www.uploadHandler = new UploadHandlerRaw(data);
    www.SetRequestHeader("Content-Type", "application/json");

    await www.SendWebRequest();

    if (www.result == UnityWebRequest.Result.Success)
    {
        var response = JsonUtility.FromJson<RegisterResponse>(www.downloadHandler.text);
        return response.success;
    }

    Debug.LogError($"注册失败: {www.error}");
    return false;
}
```

#### 2. 用户登录

使用注册的账户信息登录系统：

```csharp
public async Task<bool> Login(string username, string password)
{
    var request = new LoginRequest
    {
        username = username,
        password = password,
        rememberMe = true
    };

    string json = JsonUtility.ToJson(request);
    byte[] data = System.Text.Encoding.UTF8.GetBytes(json);

    using var www = UnityWebRequest.PostWwwForm("http://localhost:5000/api/auth/login", "");
    www.uploadHandler = new UploadHandlerRaw(data);
    www.SetRequestHeader("Content-Type", "application/json");

    await www.SendWebRequest();

    if (www.result == UnityWebRequest.Result.Success)
    {
        var response = JsonUtility.FromJson<LoginResponse>(www.downloadHandler.text);
        if (response.success)
        {
            // 保存访问令牌
            PlayerPrefs.SetString("AccessToken", response.accessToken);
            PlayerPrefs.SetString("RefreshToken", response.refreshToken);
            return true;
        }
    }

    return false;
}
```

#### 3. 选择游戏区服

登录成功后，选择游戏区服并获取游戏票据：

```csharp
public async Task<GameTicketResponse> SelectZone(string zoneId)
{
    var request = new GameTicketRequest
    {
        zoneId = zoneId
    };

    string json = JsonUtility.ToJson(request);
    byte[] data = System.Text.Encoding.UTF8.GetBytes(json);

    using var www = UnityWebRequest.PostWwwForm("http://localhost:5000/api/auth/game-ticket", "");
    www.uploadHandler = new UploadHandlerRaw(data);
    www.SetRequestHeader("Content-Type", "application/json");
    www.SetRequestHeader("Authorization", $"Bearer {PlayerPrefs.GetString("AccessToken")}");

    await www.SendWebRequest();

    if (www.result == UnityWebRequest.Result.Success)
    {
        return JsonUtility.FromJson<GameTicketResponse>(www.downloadHandler.text);
    }

    return null;
}
```

## 核心功能

### 用户认证系统

#### 账户管理

**注册新账户**
- 用户名：3-20个字符，支持字母、数字、下划线
- 密码：6-50个字符，建议包含大小写字母、数字和特殊字符
- 邮箱：用于账户恢复和通知

**登录选项**
- 用户名/邮箱登录
- 记住登录状态（7天免登录）
- 设备绑定（可选）

**安全功能**
- 连续登录失败5次将锁定账户10分钟
- 支持双因素认证（2FA）
- 异地登录提醒
- 密码强度检测

#### 令牌管理

```csharp
public class TokenManager : MonoBehaviour
{
    private string accessToken;
    private string refreshToken;
    private DateTime tokenExpiry;

    public async Task<bool> RefreshTokenIfNeeded()
    {
        if (DateTime.Now < tokenExpiry.AddMinutes(-5))
            return true; // Token 仍然有效

        return await RefreshAccessToken();
    }

    private async Task<bool> RefreshAccessToken()
    {
        var request = new RefreshTokenRequest
        {
            refreshToken = refreshToken
        };

        // 发送刷新请求...
        // 更新 accessToken 和 tokenExpiry
        return true;
    }
}
```

### 游戏服务连接

#### 连接 GameServer

```csharp
public class GameClient : MonoBehaviour
{
    private IPulseClient pulseClient;
    private IPlayerService playerService;
    private IWorldService worldService;

    public async Task<bool> ConnectToGameServer(string host, int tcpPort, int kcpPort)
    {
        try
        {
            // 创建 PulseRPC 客户端
            pulseClient = new PulseClient();

            // 连接 TCP 和 KCP 通道
            await pulseClient.ConnectAsync(host, tcpPort); // TCP
            await pulseClient.ConnectAsync(host, kcpPort); // KCP

            // 获取服务代理
            playerService = pulseClient.GetService<IPlayerService>();
            worldService = pulseClient.GetService<IWorldService>();

            // 验证游戏票据
            var gameTicket = PlayerPrefs.GetString("GameTicket");
            var authResult = await playerService.AuthenticateAsync(gameTicket);

            if (authResult.Success)
            {
                Debug.Log("成功连接到游戏服务器");
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"连接游戏服务器失败: {ex.Message}");
        }

        return false;
    }

    public async Task DisconnectFromGameServer()
    {
        if (pulseClient != null)
        {
            await pulseClient.DisconnectAsync();
            pulseClient = null;
        }
    }
}
```

#### 处理服务器事件

```csharp
public class GameEventHandler : MonoBehaviour, IPlayerEvents, IWorldEvents
{
    void Start()
    {
        // 注册事件监听器
        pulseClient.RegisterEventHandler<IPlayerEvents>(this);
        pulseClient.RegisterEventHandler<IWorldEvents>(this);
    }

    // 玩家事件处理
    public async Task OnPlayerLevelUpAsync(PlayerLevelUpEvent eventData)
    {
        Debug.Log($"恭喜！你升到了 {eventData.NewLevel} 级！");
        // 更新UI显示
        UpdatePlayerLevel(eventData.NewLevel);

        // 播放升级特效
        PlayLevelUpEffect();
    }

    public async Task OnPlayerInventoryUpdatedAsync(PlayerInventoryUpdatedEvent eventData)
    {
        Debug.Log("背包物品已更新");
        UpdateInventoryUI(eventData.UpdatedItems);
    }

    // 世界事件处理
    public async Task OnPlayerJoinedAsync(PlayerJoinedEvent eventData)
    {
        Debug.Log($"玩家 {eventData.PlayerName} 加入了游戏");
        AddPlayerToWorld(eventData.PlayerId, eventData.Position);
    }

    public async Task OnPlayerLeftAsync(PlayerLeftEvent eventData)
    {
        Debug.Log($"玩家 {eventData.PlayerName} 离开了游戏");
        RemovePlayerFromWorld(eventData.PlayerId);
    }

    public async Task OnWorldChatAsync(WorldChatEvent eventData)
    {
        Debug.Log($"[世界频道] {eventData.PlayerName}: {eventData.Message}");
        DisplayChatMessage(eventData);
    }
}
```

### 玩家管理

#### 获取玩家信息

```csharp
public async Task LoadPlayerData()
{
    try
    {
        var playerInfo = await playerService.GetPlayerInfoAsync();

        if (playerInfo.Success)
        {
            // 更新玩家UI
            UpdatePlayerUI(playerInfo.Data);

            Debug.Log($"玩家: {playerInfo.Data.CharacterName}");
            Debug.Log($"等级: {playerInfo.Data.Level}");
            Debug.Log($"经验: {playerInfo.Data.Experience}");
        }
    }
    catch (Exception ex)
    {
        Debug.LogError($"加载玩家数据失败: {ex.Message}");
    }
}
```

#### 更新玩家数据

```csharp
public async Task UpdatePlayerPosition(Vector3 position)
{
    var request = new UpdatePlayerPositionRequest
    {
        X = position.x,
        Y = position.y,
        Z = position.z,
        MapId = currentMapId
    };

    try
    {
        var result = await playerService.UpdatePlayerPositionAsync(request);
        if (!result.Success)
        {
            Debug.LogWarning($"更新位置失败: {result.Message}");
        }
    }
    catch (Exception ex)
    {
        Debug.LogError($"更新位置异常: {ex.Message}");
    }
}
```

### 世界交互

#### 世界聊天

```csharp
public class ChatSystem : MonoBehaviour
{
    [SerializeField] private InputField chatInput;
    [SerializeField] private Text chatDisplay;

    public async Task SendWorldChat(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var request = new SendWorldChatRequest
        {
            Message = message,
            Channel = ChatChannel.World
        };

        try
        {
            var result = await worldService.SendChatAsync(request);
            if (result.Success)
            {
                chatInput.text = "";
            }
            else
            {
                Debug.LogWarning($"发送消息失败: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"发送消息异常: {ex.Message}");
        }
    }

    public void DisplayChatMessage(WorldChatEvent chatEvent)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var chatText = $"[{timestamp}] {chatEvent.PlayerName}: {chatEvent.Message}\n";
        chatDisplay.text += chatText;

        // 自动滚动到底部
        StartCoroutine(ScrollToBottom());
    }
}
```

#### 组队系统

```csharp
public class PartySystem : MonoBehaviour
{
    public async Task CreateParty()
    {
        try
        {
            var result = await worldService.CreatePartyAsync();
            if (result.Success)
            {
                Debug.Log("组队创建成功");
                UpdatePartyUI(result.Data);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"创建组队失败: {ex.Message}");
        }
    }

    public async Task InvitePlayer(string playerName)
    {
        var request = new InviteToPartyRequest
        {
            PlayerName = playerName
        };

        try
        {
            var result = await worldService.InviteToPartyAsync(request);
            if (result.Success)
            {
                Debug.Log($"邀请 {playerName} 加入组队");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"邀请玩家失败: {ex.Message}");
        }
    }
}
```

### 战斗系统

#### 连接 BattleServer

```csharp
public class BattleClient : MonoBehaviour
{
    private IPulseClient battleClient;
    private IBattleService battleService;
    private ISkillService skillService;

    public async Task<bool> ConnectToBattleServer(string host, int tcpPort, int kcpPort)
    {
        try
        {
            battleClient = new PulseClient();

            // 战斗服务器主要使用 KCP 进行低延迟通信
            await battleClient.ConnectAsync(host, kcpPort); // KCP 优先
            await battleClient.ConnectAsync(host, tcpPort);  // TCP 备用

            battleService = battleClient.GetService<IBattleService>();
            skillService = battleClient.GetService<ISkillService>();

            // 注册战斗事件监听
            battleClient.RegisterEventHandler<IBattleEvents>(this);

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"连接战斗服务器失败: {ex.Message}");
            return false;
        }
    }
}
```

#### 战斗匹配

```csharp
public async Task JoinBattleQueue()
{
    var request = new JoinBattleQueueRequest
    {
        BattleType = BattleType.Ranked,
        PreferredMap = "arena_01"
    };

    try
    {
        var result = await battleService.JoinBattleQueueAsync(request);
        if (result.Success)
        {
            Debug.Log("已加入战斗队列，等待匹配...");
            ShowMatchmakingUI();
        }
    }
    catch (Exception ex)
    {
        Debug.LogError($"加入战斗队列失败: {ex.Message}");
    }
}

// 战斗事件处理
public async Task OnBattleMatchFoundAsync(BattleMatchFoundEvent eventData)
{
    Debug.Log($"找到对手！战斗ID: {eventData.BattleId}");

    // 自动进入战斗房间
    await EnterBattle(eventData.BattleId);
}

public async Task OnBattleStartedAsync(BattleStartedEvent eventData)
{
    Debug.Log("战斗开始！");

    // 隐藏匹配UI，显示战斗UI
    HideMatchmakingUI();
    ShowBattleUI();

    // 加载战斗场景
    StartCoroutine(LoadBattleScene(eventData.MapId));
}
```

#### 技能释放

```csharp
public class SkillSystem : MonoBehaviour
{
    public async Task CastSkill(int skillId, Vector3 targetPosition)
    {
        var request = new CastSkillRequest
        {
            SkillId = skillId,
            TargetX = targetPosition.x,
            TargetY = targetPosition.y,
            TargetZ = targetPosition.z,
            CastTime = Time.time
        };

        try
        {
            var result = await skillService.CastSkillAsync(request);
            if (result.Success)
            {
                // 播放技能动画
                PlaySkillAnimation(skillId, targetPosition);

                // 开始冷却
                StartSkillCooldown(skillId, result.Data.CooldownTime);
            }
            else
            {
                Debug.LogWarning($"技能释放失败: {result.Message}");
                ShowSkillError(result.Message);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"技能释放异常: {ex.Message}");
        }
    }

    // 处理技能命中事件
    public async Task OnSkillHitAsync(SkillHitEvent eventData)
    {
        Debug.Log($"技能命中！伤害: {eventData.Damage}");

        // 显示伤害数字
        ShowDamageText(eventData.TargetId, eventData.Damage);

        // 播放命中特效
        PlayHitEffect(eventData.HitPosition);

        // 更新血量显示
        UpdateHealthBar(eventData.TargetId, eventData.NewHealth);
    }
}
```

## 高级功能

### 性能监控

#### 客户端性能监控

```csharp
public class PerformanceMonitor : MonoBehaviour
{
    [SerializeField] private Text fpsText;
    [SerializeField] private Text latencyText;

    private float fps;
    private float latency;

    void Update()
    {
        // 计算FPS
        fps = 1.0f / Time.deltaTime;

        // 更新显示
        fpsText.text = $"FPS: {fps:F1}";
        latencyText.text = $"延迟: {latency:F0}ms";
    }

    public async Task MeasureLatency()
    {
        var startTime = DateTime.UtcNow;

        try
        {
            await playerService.PingAsync();
            latency = (float)(DateTime.UtcNow - startTime).TotalMilliseconds;
        }
        catch
        {
            latency = -1; // 连接失败
        }
    }
}
```

#### 网络质量检测

```csharp
public class NetworkQualityMonitor : MonoBehaviour
{
    public async Task<NetworkQuality> CheckNetworkQuality()
    {
        var pingTests = new List<float>();

        // 进行10次ping测试
        for (int i = 0; i < 10; i++)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                await playerService.PingAsync();
                var ping = (float)(DateTime.UtcNow - startTime).TotalMilliseconds;
                pingTests.Add(ping);
            }
            catch
            {
                pingTests.Add(1000f); // 超时
            }

            await Task.Delay(100); // 等待100ms
        }

        var averagePing = pingTests.Average();
        var packetLoss = pingTests.Count(p => p >= 1000) / 10.0f;

        // 评估网络质量
        if (averagePing < 50 && packetLoss < 0.01f)
            return NetworkQuality.Excellent;
        else if (averagePing < 100 && packetLoss < 0.05f)
            return NetworkQuality.Good;
        else if (averagePing < 200 && packetLoss < 0.1f)
            return NetworkQuality.Fair;
        else
            return NetworkQuality.Poor;
    }
}
```

### 错误处理

#### 网络错误处理

```csharp
public class ErrorHandler : MonoBehaviour
{
    public void HandleNetworkError(Exception ex)
    {
        switch (ex)
        {
            case TimeoutException:
                ShowErrorDialog("网络超时", "请检查网络连接后重试");
                break;

            case UnauthorizedAccessException:
                ShowErrorDialog("认证失败", "登录已过期，请重新登录");
                ReturnToLoginScreen();
                break;

            case ConnectionException:
                ShowErrorDialog("连接失败", "无法连接到服务器，请稍后重试");
                break;

            default:
                ShowErrorDialog("未知错误", $"发生了未知错误: {ex.Message}");
                break;
        }
    }

    public async Task<bool> RetryWithExponentialBackoff(Func<Task> operation, int maxRetries = 3)
    {
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                await operation();
                return true;
            }
            catch (Exception ex)
            {
                if (attempt == maxRetries - 1)
                {
                    HandleNetworkError(ex);
                    return false;
                }

                // 指数退避重试
                var delay = (int)Math.Pow(2, attempt) * 1000;
                await Task.Delay(delay);
            }
        }

        return false;
    }
}
```

### 配置管理

#### 游戏配置

```csharp
[CreateAssetMenu(fileName = "GameConfig", menuName = "GameApp/GameConfig")]
public class GameConfig : ScriptableObject
{
    [Header("服务器配置")]
    public string authServerUrl = "http://localhost:5000";
    public string gameServerHost = "127.0.0.1";
    public int gameServerTcpPort = 7000;
    public int gameServerKcpPort = 7001;
    public string battleServerHost = "127.0.0.1";
    public int battleServerTcpPort = 8000;
    public int battleServerKcpPort = 8001;

    [Header("网络配置")]
    public int connectionTimeout = 30;
    public int heartbeatInterval = 30;
    public int maxRetryAttempts = 3;

    [Header("游戏配置")]
    public float playerMoveSpeed = 5.0f;
    public int maxChatHistory = 100;
    public bool enableDebugLog = true;

    [Header("UI配置")]
    public bool showFPS = true;
    public bool showLatency = true;
    public Color chatColor = Color.white;
    public Color systemMessageColor = Color.yellow;
}
```

## 故障排除

### 常见问题

#### 1. 连接失败

**问题**: 无法连接到服务器
**解决方案**:
1. 检查网络连接
2. 确认服务器地址和端口
3. 检查防火墙设置
4. 验证服务器是否正在运行

#### 2. 登录失败

**问题**: 用户名或密码错误
**解决方案**:
1. 确认用户名和密码正确
2. 检查大小写敏感性
3. 确认账户未被锁定
4. 尝试密码重置

#### 3. 游戏卡顿

**问题**: 游戏运行不流畅
**解决方案**:
1. 降低图形质量设置
2. 检查网络延迟
3. 关闭不必要的后台程序
4. 更新显卡驱动

#### 4. 战斗匹配失败

**问题**: 无法找到对手
**解决方案**:
1. 检查服务器在线玩家数
2. 尝试更换匹配模式
3. 确认网络连接稳定
4. 联系客服支持

### 调试工具

#### 网络调试

```csharp
public class NetworkDebugger : MonoBehaviour
{
    [SerializeField] private bool enableDebugLog = true;
    [SerializeField] private Text debugInfoText;

    void Update()
    {
        if (enableDebugLog && debugInfoText != null)
        {
            var debugInfo = $"连接状态: {GetConnectionStatus()}\n";
            debugInfo += $"延迟: {GetLatency()}ms\n";
            debugInfo += $"发送包: {GetSentPackets()}\n";
            debugInfo += $"接收包: {GetReceivedPackets()}\n";
            debugInfo += $"丢包率: {GetPacketLoss():P2}";

            debugInfoText.text = debugInfo;
        }
    }
}
```

## 更新和维护

### 客户端更新

GameApp 支持自动更新机制：

1. **版本检查**: 启动时自动检查最新版本
2. **增量更新**: 只下载变更的文件
3. **后台下载**: 在游戏运行时后台下载更新
4. **热更新**: 支持部分功能的热更新

### 数据备份

重要的游戏数据会自动同步到服务器：

- 玩家角色数据
- 游戏进度
- 好友列表
- 聊天记录（最近100条）

## 支持和帮助

### 技术支持

- **官方文档**: https://docs.gameapp.com
- **开发者论坛**: https://forum.gameapp.com
- **GitHub仓库**: https://github.com/gameapp/client
- **邮件支持**: support@gameapp.com

### 社区资源

- **官方QQ群**: 123456789
- **Discord服务器**: https://discord.gg/gameapp
- **微信交流群**: 扫描二维码加入
- **Bilibili视频教程**: https://space.bilibili.com/gameapp

---

**感谢使用 GameApp！**

如果您在使用过程中遇到任何问题，请随时联系我们的技术支持团队。我们致力于为您提供最佳的游戏体验。
