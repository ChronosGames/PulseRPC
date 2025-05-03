using System.Threading.Tasks;
using PulseRPC.Protocol.Messages;
using PulseRPC.Protocol.Network;

namespace PulseRPC.Protocol.Handlers
{
    /// <summary>
    /// 登录请求处理器
    /// </summary>
    public class LoginRequestHandler : RequestHandlerBase<LoginRequest, LoginResponse>
    {
        /// <summary>
        /// 处理登录请求
        /// </summary>
        /// <param name="context">会话上下文</param>
        /// <param name="request">登录请求</param>
        /// <returns>登录响应</returns>
        protected override async Task<LoginResponse> ProcessRequestAsync(SessionContext context, LoginRequest request)
        {
            // 实际项目中这里会处理登录逻辑，如验证用户名密码、生成令牌等
            // 此处仅作为示例

            await Task.Delay(100); // 模拟处理时间

            // 检查请求是否有效
            if (!request.IsValid)
            {
                return new LoginResponse
                {
                    Status = ResponseStatus.InvalidParameter,
                    ErrorMessage = "用户名或密码不能为空",
                };
            }

            // 假设登录成功
            return new LoginResponse
            {
                Status = ResponseStatus.Success,
                Token = "generated-token-" + System.Guid.NewGuid().ToString("N"),
                PlayerId = 12345,
                ErrorMessage = string.Empty
            };
        }
    }
}
