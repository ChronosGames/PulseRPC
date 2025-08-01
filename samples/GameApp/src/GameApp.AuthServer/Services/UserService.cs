using MongoDB.Driver;
using GameApp.AuthServer.Models;
using BCrypt.Net;

namespace GameApp.AuthServer.Services;

/// <summary>
/// 用户服务实现
/// </summary>
public class UserService : IUserService
{
    private readonly IMongoCollection<User> _userCollection;
    private readonly ILogger<UserService> _logger;

    public UserService(IMongoDatabase database, ILogger<UserService> logger)
    {
        _userCollection = database.GetCollection<User>("users");
        _logger = logger;
    }

    /// <summary>
    /// 根据用户名获取用户
    /// </summary>
    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        try
        {
            var filter = Builders<User>.Filter.Eq(u => u.Username, username);
            var user = await _userCollection.Find(filter).FirstOrDefaultAsync();

            if (user != null)
            {
                _logger.LogDebug("Found user by username: {Username}", username);
            }

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user by username: {Username}", username);
            throw;
        }
    }

    /// <summary>
    /// 根据邮箱获取用户
    /// </summary>
    public async Task<User?> GetUserByEmailAsync(string email)
    {
        try
        {
            var filter = Builders<User>.Filter.Eq(u => u.Email, email);
            var user = await _userCollection.Find(filter).FirstOrDefaultAsync();

            if (user != null)
            {
                _logger.LogDebug("Found user by email: {Email}", email);
            }

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user by email: {Email}", email);
            throw;
        }
    }

    /// <summary>
    /// 根据用户ID获取用户
    /// </summary>
    public async Task<User?> GetUserByIdAsync(int userId)
    {
        try
        {
            var filter = Builders<User>.Filter.Eq(u => u.UserId, userId);
            var user = await _userCollection.Find(filter).FirstOrDefaultAsync();

            if (user != null)
            {
                _logger.LogDebug("Found user by ID: {UserId}", userId);
            }

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user by ID: {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// 创建新用户
    /// </summary>
    public async Task<User> CreateUserAsync(RegisterRequest request)
    {
        try
        {
            // 检查用户名是否已存在
            var existingUser = await GetUserByUsernameAsync(request.Username);
            if (existingUser != null)
            {
                throw new InvalidOperationException($"Username '{request.Username}' already exists");
            }

            // 检查邮箱是否已存在
            var existingEmailUser = await GetUserByEmailAsync(request.Email);
            if (existingEmailUser != null)
            {
                throw new InvalidOperationException($"Email '{request.Email}' already exists");
            }

            // 生成新的用户ID
            var nextUserId = await GetNextUserIdAsync();

            // 生成密码哈希
            var (passwordHash, salt) = GeneratePasswordHash(request.Password);

            // 创建用户对象
            var user = new User
            {
                UserId = nextUserId,
                Username = request.Username,
                Email = request.Email,
                PasswordHash = passwordHash,
                Salt = salt,
                Status = "active",
                RegistrationTime = DateTime.UtcNow,
                LastLoginTime = DateTime.UtcNow,
                LoginCount = 0,
                Profile = new UserProfile
                {
                    Nickname = request.Username, // 默认使用用户名作为昵称
                    Level = 1,
                    VipLevel = 0,
                    Language = "zh-CN"
                },
                Security = new UserSecurity
                {
                    TwoFactorEnabled = false,
                    SecurityQuestions = new List<SecurityQuestion>(),
                    LoginHistory = new List<LoginHistory>()
                },
                Preferences = new UserPreferences
                {
                    Language = "zh-CN",
                    SoundEnabled = true,
                    MusicEnabled = true,
                    NotificationEnabled = true
                },
                GameData = new UserGameData
                {
                    TotalPlayTime = 0,
                    AchievementPoints = 0,
                    LastSelectedZone = string.Empty
                },
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // 插入到数据库
            await _userCollection.InsertOneAsync(user);

            _logger.LogInformation("Created new user: {Username} (ID: {UserId})", request.Username, nextUserId);
            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user: {Username}", request.Username);
            throw;
        }
    }

    /// <summary>
    /// 更新用户登录信息
    /// </summary>
    public async Task UpdateUserLoginInfoAsync(int userId, string ip, string userAgent)
    {
        try
        {
            var filter = Builders<User>.Filter.Eq(u => u.UserId, userId);

            var loginHistory = new LoginHistory
            {
                Ip = ip,
                UserAgent = userAgent,
                LoginTime = DateTime.UtcNow,
                Success = true
            };

            var update = Builders<User>.Update
                .Set(u => u.LastLoginTime, DateTime.UtcNow)
                .Inc(u => u.LoginCount, 1)
                .Push(u => u.Security.LoginHistory, loginHistory)
                .Set(u => u.UpdatedAt, DateTime.UtcNow);

            await _userCollection.UpdateOneAsync(filter, update);

            // TODO: 实现登录历史记录数量限制（保留最近50条）
            // 这可以通过定期清理任务或聚合管道来实现

            _logger.LogDebug("Updated login info for user: {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user login info: {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// 验证密码
    /// </summary>
    public bool VerifyPassword(string password, string passwordHash, string salt)
    {
        try
        {
            // 使用 BCrypt 验证密码
            return BCrypt.Net.BCrypt.Verify(password + salt, passwordHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying password");
            return false;
        }
    }

    /// <summary>
    /// 生成密码哈希
    /// </summary>
    public (string hash, string salt) GeneratePasswordHash(string password)
    {
        try
        {
            // 生成随机盐
            var salt = BCrypt.Net.BCrypt.GenerateSalt();

            // 加盐处理
            var saltedPassword = password + salt;

            // 生成哈希
            var hash = BCrypt.Net.BCrypt.HashPassword(saltedPassword);

            return (hash, salt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating password hash");
            throw;
        }
    }

    /// <summary>
    /// 获取下一个用户ID
    /// </summary>
    private async Task<int> GetNextUserIdAsync()
    {
        try
        {
            // 查找最大的用户ID
            var sort = Builders<User>.Sort.Descending(u => u.UserId);
            var lastUser = await _userCollection.Find(_ => true)
                .Sort(sort)
                .Limit(1)
                .FirstOrDefaultAsync();

            return lastUser?.UserId + 1 ?? 1000001; // 从1000001开始
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting next user ID");
            throw;
        }
    }
}
