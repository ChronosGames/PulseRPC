using System.Threading.Tasks;
using PulseRPC.Authentication;
using PulseRPC.Server.Authentication;

namespace PulseRPC.Server.Authentication
{
    /// <summary>
    /// 统一认证服务接口
    /// </summary>
    public interface IUnifiedAuthenticationService
    {
        /// <summary>
        /// 客户端认证
        /// </summary>
        /// <param name="credentials">认证凭证（用户名:密码 或 token）</param>
        /// <returns>认证结果</returns>
        Task<AuthenticationResult> AuthenticateClientAsync(string credentials);

        /// <summary>
        /// 服务间认证
        /// </summary>
        /// <param name="serviceId">服务ID</param>
        /// <param name="serviceToken">服务令牌</param>
        /// <returns>认证结果</returns>
        Task<AuthenticationResult> AuthenticateServiceAsync(string serviceId, string serviceToken);

        /// <summary>
        /// Token验证
        /// </summary>
        /// <param name="token">待验证的Token</param>
        /// <param name="expectedType">期望的认证类型</param>
        /// <returns>认证结果</returns>
        Task<AuthenticationResult> ValidateTokenAsync(string token, AuthenticationType expectedType);

        /// <summary>
        /// 生成服务间通信Token
        /// </summary>
        /// <param name="serviceId">服务ID</param>
        /// <param name="scopes">权限范围</param>
        /// <returns>服务Token</returns>
        string GenerateServiceToken(string serviceId, string[]? scopes = null);

        /// <summary>
        /// 验证服务权限范围
        /// </summary>
        /// <param name="serviceId">服务ID</param>
        /// <param name="requiredScope">所需权限范围</param>
        /// <returns>是否具有权限</returns>
        Task<bool> ValidateServiceScopeAsync(string serviceId, string requiredScope);
    }
}
