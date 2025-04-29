using System;
using System.Threading.Tasks;
using PulseRPC;

namespace JwtAuthApp.Shared
{
    public interface ITimerHub : IPulseHub<ITimerHub, ITimerHubReceiver>
    {
        Task SetAsync(TimeSpan interval);
    }

    public interface ITimerHubReceiver
    {
        void OnTick(string message);
    }
}
