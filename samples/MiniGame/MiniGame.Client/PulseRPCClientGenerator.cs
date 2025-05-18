using PulseRPC.Client;
using PulseRPC.Samples.Shared.Messages;
using MiniGame.Shared;

namespace MiniGame.Client;

// 基础接口 - 用于源生成
// 使用WithResultType选项指定返回值类型，解决WithDeadline等方法返回类型问题
[PulseClientGeneration(typeof(IAuthStreamingHub), WithResultType = typeof(IAuthStreamingHub))]
[PulseClientGeneration(typeof(IUserStreamingHub), WithResultType = typeof(IUserStreamingHub))]

// 实际业务接口 - 继承基础接口
// 由于AuthStreamingHub继承自IAuthStreamingHub，需要确保正确生成
[PulseClientGeneration(typeof(IAuthStreamingHub), WithResultType = typeof(IUserStreamingHub))]
// 由于GameStreamingHub继承自IUserStreamingHub和INotificationReceiver，需要单独生成
[PulseClientGeneration(typeof(IUserStreamingHub), WithResultType = typeof(IUserStreamingHub))]
// 单独生成通知接收器
[PulseClientGeneration(typeof(INotificationReceiver), WithResultType = typeof(INotificationReceiver))]
public partial class PulseRPCClientGenerator
{
    // 这个类由源代码生成器使用，不需要实现任何方法
}
