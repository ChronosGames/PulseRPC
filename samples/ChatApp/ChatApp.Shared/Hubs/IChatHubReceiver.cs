using System.Threading.Tasks;
using ChatApp.Shared.Models;

namespace ChatApp.Shared.Hubs
{
    /// <summary>
    /// Server -> Client API
    /// </summary>
    public interface IChatHubReceiver
    {
        void OnJoin(string name);

        void OnLeave(string name);

        void OnSendMessage(MessageResponse message);

        Task<string> HelloAsync(string name, int age);

        // 定义服务器->客户端的事件ID
        public static class EventIds
        {
            public const int OnJoin = 1;
            public const int OnLeave = 2;
            public const int OnSendMessage = 3;
            public const int HelloAsync = 4;
        }
    }
}
