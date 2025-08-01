# AuthServer API 文档

## 概述

AuthServer 提供用户认证、令牌管理和区服选择功能，使用 HTTP REST API 和 JSON 格式。

**Base URL**: `http://localhost:5000/api/auth`

## 认证流程

### 1. 用户注册

**POST** `/register`

注册新用户账户。

#### 请求体
```json
{
  "username": "string",        // 用户名 (3-20字符)
  "password": "string",        // 密码 (6-50字符)
  "email": "string",          // 邮箱地址
  "confirmPassword": "string"  // 确认密码
}
```

#### 响应
```json
{
  "success": true,
  "message": "注册成功",
  "user": {
    "id": "string",
    "username": "string",
    "email": "string",
    "createdAt": "2024-01-01T00:00:00Z"
  }
}
```

#### 错误响应
- `400 Bad Request`: 参数验证失败
- `409 Conflict`: 用户名或邮箱已存在

### 2. 用户登录

**POST** `/login`

用户登录，获取访问令牌。

#### 请求体
```json
{
  "username": "string",     // 用户名或邮箱
  "password": "string",     // 密码
  "rememberMe": false,      // 是否记住登录
  "deviceInfo": {           // 设备信息 (可选)
    "deviceId": "string",
    "deviceType": "string",
    "platform": "string"
  }
}
```

#### 响应
```json
{
  "success": true,
  "accessToken": "string",    // JWT访问令牌
  "refreshToken": "string",   // 刷新令牌
  "expiresIn": 3600,         // 过期时间(秒)
  "tokenType": "Bearer",
  "user": {
    "id": "string",
    "username": "string",
    "email": "string",
    "level": 1,
    "lastLoginAt": "2024-01-01T00:00:00Z"
  }
}
```

#### 错误响应
- `400 Bad Request`: 参数错误
- `401 Unauthorized`: 用户名或密码错误
- `423 Locked`: 账户被锁定

### 3. 刷新令牌

**POST** `/refresh`

使用刷新令牌获取新的访问令牌。

#### 请求体
```json
{
  "refreshToken": "string"
}
```

#### 响应
```json
{
  "success": true,
  "accessToken": "string",
  "expiresIn": 3600,
  "tokenType": "Bearer"
}
```

### 4. 用户登出

**POST** `/logout`

**Headers**: `Authorization: Bearer <token>`

登出用户，撤销令牌。

#### 响应
```json
{
  "success": true,
  "message": "登出成功"
}
```

## 用户管理

### 获取用户信息

**GET** `/profile`

**Headers**: `Authorization: Bearer <token>`

获取当前用户的详细信息。

#### 响应
```json
{
  "success": true,
  "user": {
    "id": "string",
    "username": "string",
    "email": "string",
    "level": 1,
    "experience": 0,
    "coins": 1000,
    "createdAt": "2024-01-01T00:00:00Z",
    "lastLoginAt": "2024-01-01T00:00:00Z",
    "loginCount": 10,
    "profile": {
      "nickname": "string",
      "avatar": "string",
      "signature": "string"
    }
  }
}
```

### 修改密码

**PUT** `/password`

**Headers**: `Authorization: Bearer <token>`

修改用户密码。

#### 请求体
```json
{
  "currentPassword": "string",
  "newPassword": "string",
  "confirmPassword": "string"
}
```

#### 响应
```json
{
  "success": true,
  "message": "密码修改成功"
}
```

## 游戏区服

### 获取区服列表

**GET** `/zones`

获取可用的游戏区服列表。

#### 查询参数
- `status`: 区服状态筛选 (`Online`, `Maintenance`, `Offline`)
- `region`: 地区筛选

#### 响应
```json
{
  "success": true,
  "zones": [
    {
      "id": "zone001",
      "name": "梦想大陆",
      "region": "CN-East",
      "status": "Online",
      "population": "Medium",
      "currentPlayers": 1234,
      "maxPlayers": 5000,
      "isRecommended": true,
      "isNew": false,
      "description": "全新服务器，等你来战！"
    }
  ]
}
```

### 选择区服并获取游戏票据

**POST** `/game-ticket`

**Headers**: `Authorization: Bearer <token>`

选择区服并获取游戏服务器访问票据。

#### 请求体
```json
{
  "zoneId": "zone001"
}
```

#### 响应
```json
{
  "success": true,
  "gameTicket": "string",      // 游戏票据
  "gameServerEndpoint": {
    "host": "127.0.0.1",
    "tcpPort": 7000,
    "kcpPort": 7001
  },
  "battleServerEndpoint": {
    "host": "127.0.0.1",
    "tcpPort": 8000,
    "kcpPort": 8001
  },
  "expiresIn": 300            // 票据有效期(秒)
}
```

## 错误码

| 错误码 | HTTP状态 | 描述 |
|--------|----------|------|
| 1001 | 400 | 参数验证失败 |
| 1002 | 400 | 用户名格式错误 |
| 1003 | 400 | 密码格式错误 |
| 1004 | 400 | 邮箱格式错误 |
| 2001 | 401 | 用户名或密码错误 |
| 2002 | 401 | 令牌无效或过期 |
| 2003 | 423 | 账户被锁定 |
| 3001 | 409 | 用户名已存在 |
| 3002 | 409 | 邮箱已被注册 |
| 4001 | 404 | 用户不存在 |
| 4002 | 404 | 区服不存在 |
| 5001 | 500 | 服务器内部错误 |

## 速率限制

- **登录接口**: 每IP每分钟最多5次请求
- **注册接口**: 每IP每小时最多3次请求
- **其他接口**: 每用户每分钟最多60次请求

## 安全性

### JWT令牌
- 使用HS256算法签名
- 默认有效期1小时
- 包含用户ID、用户名、角色等声明

### 密码安全
- 使用BCrypt哈希算法
- 最小长度6位，建议包含数字和字母
- 连续登录失败5次将锁定账户10分钟

### HTTPS
生产环境强制使用HTTPS协议，所有敏感数据传输均加密保护。

## 示例代码

### C# 客户端
```csharp
public class AuthClient
{
    private readonly HttpClient _httpClient;

    public async Task<LoginResponse> LoginAsync(string username, string password)
    {
        var request = new LoginRequest
        {
            Username = username,
            Password = password
        };

        var response = await _httpClient.PostAsJsonAsync("/api/auth/login", request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<LoginResponse>();
    }
}
```

### Unity 客户端
```csharp
[System.Serializable]
public class LoginRequest
{
    public string username;
    public string password;
    public bool rememberMe;
}

public class AuthManager : MonoBehaviour
{
    public async Task<bool> LoginAsync(string username, string password)
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
            PlayerPrefs.SetString("AccessToken", response.accessToken);
            return response.success;
        }

        return false;
    }
}
```
