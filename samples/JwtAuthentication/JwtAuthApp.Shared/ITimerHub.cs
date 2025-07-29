using System;
using System.Threading.Tasks;
using PulseRPC;

namespace JwtAuthApp.Shared
{
    public interface ITimerHub : IPulseService
    {
        Task SetAsync(TimeSpan interval);

        // 定义事件名称常量
        public static class Events
        {
            public const string OnTick = nameof(OnTick);
        }
    }
}
