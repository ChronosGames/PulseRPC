using System.Threading.Tasks;
using DistributedGameApp.Shared.Messages;
using PulseRPC;

namespace DistributedGameApp.Shared.Hubs;

/// <summary>
/// Connection 认证服务接口
/// </summary>
/// <remarks>
/// <para><strong>职责</strong>：</para>
/// <list type="bullet">
/// <item><description>处理连接建立后的 JWT 认证请求</description></item>
/// <item><description>验证 Token 并设置 Connection Context</description></item>
/// <item><description>返回认证结果和用户信息</description></item>
/// </list>
/// <para><strong>使用场景</strong>：</para>
/// <para>客户端连接到服务器后，必须在 5 秒内调用此服务完成认证，否则连接将被断开</para>
/// </remarks>
public interface IConnectionAuthenticationService : IPulseHub
{
    /// <summary>
    /// 认证连接
    /// </summary>
    /// <param name="request">认证请求（包含 JWT Access Token）</param>
    /// <returns>认证响应（包含用户信息或错误信息）</returns>
    Task<ConnectionAuthResponse> AuthenticateAsync(ConnectionAuthRequest request);
}
