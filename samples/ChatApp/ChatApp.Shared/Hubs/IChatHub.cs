using ChatApp.Shared.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChatApp.Shared.Hubs
{
    /// <summary>
    /// Client -> Server API (Streaming)
    /// </summary>
    public interface IChatHub
    {
        Task<bool> JoinAsync(JoinRequest request);

        Task<bool> LeaveAsync();

        Task<bool> SendMessageAsync(string message);

        Task<bool> GenerateException(string message);

        // 定义客户端->服务器的事件ID
        public static class MethodIds
        {
            public const int Join = 1;
            public const int Leave = 2;
            public const int SendMessage = 3;
            public const int GenerateException = 4;
        }
    }
}
