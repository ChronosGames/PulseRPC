using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using JwtAuthApp.Shared;
using Microsoft.AspNetCore.Authorization;
using PulseRPC;
using PulseRPC.Internal;

namespace JwtAuthApp.Server.Hubs
{
    [Authorize]
    public class TimerHub : PulseHub<ITimerHub, ITimerHubReceiver>, ITimerHub
    {
        private Task? _timerLoopTask;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private TimeSpan _interval = TimeSpan.FromSeconds(1);
        private ClaimsPrincipal? _userPrincipal;

        public async Task SetAsync(TimeSpan interval)
        {
            if (_timerLoopTask != null) throw new InvalidOperationException("The timer has been already started.");

            _userPrincipal = Context.User;
            _interval = interval;
            _timerLoopTask = Task.Run(async () =>
            {
                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    await Task.Delay(_interval, _cancellationTokenSource.Token);

                    if (_userPrincipal != null)
                    {
                        Client.OnTick($"UserId={_userPrincipal.Claims.First(x => x.Type == ClaimTypes.NameIdentifier).Value}; Name={_userPrincipal.Identity?.Name}");
                    }
                }
            });
        }

        protected ValueTask DisconnectedAsync()
        {
            _cancellationTokenSource.Cancel();
        }
    }
}
