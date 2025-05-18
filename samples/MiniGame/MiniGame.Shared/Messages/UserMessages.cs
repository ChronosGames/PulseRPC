using MemoryPack;

namespace PulseRPC.Samples.Shared.Messages;

public interface IUserStreamingHub : IStreamingHub<IUserStreamingHub>
{
    // 额外的游戏特定方法可以在这里添加
    Task<GameStatusResponse> GetGameStatusAsync();

    Task<GetUserInfoResponse> GetUserInfoAsync(int userId);

    Task<ValueTuple<ResponseStatus, string, int>> UpdateUserInfoAsync(UpdateUserInfoRequest request);
}

/// <summary>
/// 游戏状态响应
/// </summary>
[MemoryPack.MemoryPackable]
public partial class GameStatusResponse
{
    /// <summary>
    /// 游戏状态
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 在线玩家数量
    /// </summary>
    public int OnlinePlayers { get; set; }

    /// <summary>
    /// 服务器时间
    /// </summary>
    public DateTime ServerTime { get; set; }
}

/// <summary>
/// 获取用户信息响应
/// </summary>
[MemoryPackable]
public partial class GetUserInfoResponse
{
    /// <summary>
    /// 响应状态
    /// </summary>
    public ResponseStatus Status { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// 用户ID
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// 用户昵称
    /// </summary>
    public string Nickname { get; set; } = string.Empty;

    /// <summary>
    /// 用户头像URL
    /// </summary>
    public string AvatarUrl { get; set; } = string.Empty;

    /// <summary>
    /// 用户状态
    /// </summary>
    public UserStatus UserStatus { get; set; }

    /// <summary>
    /// 注册时间
    /// </summary>
    public DateTime RegisterTime { get; set; }

    /// <summary>
    /// 最后登录时间
    /// </summary>
    public DateTime LastLoginTime { get; set; }
}

/// <summary>
/// 更新用户信息请求
/// </summary>
[MemoryPackable]
public partial class UpdateUserInfoRequest
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// 用户昵称
    /// </summary>
    public string Nickname { get; set; } = string.Empty;

    /// <summary>
    /// 用户头像URL
    /// </summary>
    public string AvatarUrl { get; set; } = string.Empty;
}
