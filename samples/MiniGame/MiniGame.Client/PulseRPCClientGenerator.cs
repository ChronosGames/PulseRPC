using PulseRPC.Client;
using PulseRPC.Samples.Shared.Messages;
using MiniGame.Shared;

namespace MiniGame.Client;

// 基础接口 - 用于源生成
[PulseClientGeneration(typeof(IAuthStreamingHub))]
[PulseClientGeneration(typeof(IUserStreamingHub))]

// 实际业务接口 - 继承基础接口
[PulseClientGeneration(typeof(AuthStreamingHub))]
[PulseClientGeneration(typeof(GameStreamingHub))]
[PulseClientGeneration(typeof(INotificationReceiver))]
public partial class PulseRPCClientGenerator;
