using System.Threading.Tasks;
using PulseRPC;

namespace JwtAuthApp.Shared;

/// <summary>
/// 问候服务 - 演示需要连接已认证（任意用户）才能调用的方法。
/// </summary>
public interface IGreeterHub : IPulseHub
{
    /// <summary>
    /// 向当前已认证用户问好；未认证时由实现抛出异常。
    /// </summary>
    [Authorize]
    Task<string> HelloAsync();
}
