using System;
using System.Threading.Tasks;
using ChatApp.Shared.Services;
using Microsoft.Extensions.Logging;

namespace ChatApp.Server.Services
{
    /// <summary>
    /// 聊天服务实现
    /// </summary>
    public class ChatService : IChatService
    {
        private readonly ILogger<ChatService> _logger;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        public ChatService(ILogger<ChatService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 实现WithDeadline方法
        /// </summary>
        public IChatService WithDeadline(DateTime deadline)
        {
            // 在实际实现中，这个方法在客户端代理中使用，服务端实现可以忽略
            return this;
        }

        /// <summary>
        /// 实现WithCancellationToken方法
        /// </summary>
        public IChatService WithCancellationToken(System.Threading.CancellationToken cancellationToken)
        {
            // 在实际实现中，这个方法在客户端代理中使用，服务端实现可以忽略
            return this;
        }

        /// <summary>
        /// 实现WithHost方法
        /// </summary>
        public IChatService WithHost(string host)
        {
            // 在实际实现中，这个方法在客户端代理中使用，服务端实现可以忽略
            return this;
        }

        /// <summary>
        /// 生成异常（测试错误处理）
        /// </summary>
        public Task<bool> GenerateException(string message)
        {
            _logger.LogWarning("生成异常: {Message}", message);
            throw new InvalidOperationException($"测试异常: {message}");
        }

        /// <summary>
        /// 发送报告
        /// </summary>
        public Task<bool> SendReportAsync(string message)
        {
            _logger.LogInformation("收到报告: {Message}", message);
            return Task.FromResult(true);
        }
    }
} 